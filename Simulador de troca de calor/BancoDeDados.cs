using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using MySqlConnector;

namespace SimuladorTrocaCalor
{
    // "Foto" do estado da malha em uma interacao.
    // Precisa ser uma copia: o motor continua alterando os corpos enquanto
    // o gravador ainda esta mandando os dados da interacao anterior pro banco.
    public class RegistroInteracao
    {
        public int NumInteracao { get; }
        public double TempoSimulado { get; }
        public double[,] Temperaturas { get; }  // Kelvin
        public Material[,] Materiais { get; }
        public double Lado { get; }

        private RegistroInteracao(int numInteracao, double tempoSimulado, double[,] temperaturas,
                                  Material[,] materiais, double lado)
        {
            NumInteracao = numInteracao;
            TempoSimulado = tempoSimulado;
            Temperaturas = temperaturas;
            Materiais = materiais;
            Lado = lado;
        }

        // copia temperatura e material de todos os corpos no momento da chamada
        public static RegistroInteracao Capturar(MotorSimulacao motor)
        {
            Grid grid = motor.Grid;
            int n = grid.Tamanho;
            var temperaturas = new double[n, n];
            var materiais = new Material[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    temperaturas[i, j] = grid.Corpos[i, j].TemperaturaAtual;
                    materiais[i, j] = grid.Corpos[i, j].Material;
                }
            }

            return new RegistroInteracao(motor.NumeroInteracao, motor.TempoSimulado,
                                         temperaturas, materiais, grid.Corpos[0, 0].Lado);
        }
    }

    // Simulacao lida do banco, pronta para continuar de onde parou
    public class SimulacaoCarregada
    {
        public int NumSimulacao { get; init; }
        public MotorSimulacao Motor { get; init; } = null!;
    }

    // Todo o acesso ao MySQL fica nesta classe (comandos SQL de insert e select)
    public class RepositorioSimulacao
    {
        private readonly string stringConexao;

        // numeros vao direto no texto do SQL, sempre com ponto decimal (cultura invariante),
        // senao o Windows em portugues escreveria "0,01" e o MySQL leria como dois valores
        private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

        // quantos corpos por comando INSERT (evita um comando gigante em malhas grandes)
        private const int LinhasPorInsert = 2000;

        public RepositorioSimulacao(string stringConexao)
        {
            this.stringConexao = stringConexao;
        }

        private MySqlConnection AbrirConexao()
        {
            var conexao = new MySqlConnection(stringConexao);
            conexao.Open();
            return conexao;
        }

        // Garante que a tabela Material tem exatamente os materiais das classes do C#.
        // ON DUPLICATE KEY UPDATE: se o Id ja existe, atualiza os valores em vez de dar erro.
        public void SincronizarMateriais()
        {
            using var conexao = AbrirConexao();
            foreach (Material m in Material.MateriaisDisponiveis)
            {
                using var cmd = new MySqlCommand(
                    @"INSERT INTO Material (IdMaterial, Nome, Classe, CondutividadeTermica, CalorEspecifico, Densidade)
                      VALUES (@id, @nome, @classe, @k, @c, @dens)
                      ON DUPLICATE KEY UPDATE Nome = @nome, Classe = @classe,
                          CondutividadeTermica = @k, CalorEspecifico = @c, Densidade = @dens", conexao);
                cmd.Parameters.AddWithValue("@id", m.Id);
                cmd.Parameters.AddWithValue("@nome", m.Nome);
                cmd.Parameters.AddWithValue("@classe", m.Classe);
                cmd.Parameters.AddWithValue("@k", m.CondutividadeTermica);
                cmd.Parameters.AddWithValue("@c", m.CalorEspecifico);
                cmd.Parameters.AddWithValue("@dens", m.Densidade);
                cmd.ExecuteNonQuery();
            }
        }

        // Cria a linha da simulacao e devolve o NumSimulacao gerado pelo AUTO_INCREMENT
        public int CriarSimulacao(MotorSimulacao motor)
        {
            using var conexao = AbrirConexao();
            using var cmd = new MySqlCommand(
                @"INSERT INTO Simulacao (DataSimulacao, QtdCorposInicial, NumInteracoes, TempoInteracoes,
                                         TamanhoGrid, LadoCorpo, ModoProcessamento)
                  VALUES (NOW(), @qtd, 0, @passo, @tamanho, @lado, @modo);
                  SELECT LAST_INSERT_ID();", conexao);
            int n = motor.Grid.Tamanho;
            cmd.Parameters.AddWithValue("@qtd", n * n);
            cmd.Parameters.AddWithValue("@passo", motor.PassoDeTempo);
            cmd.Parameters.AddWithValue("@tamanho", n);
            cmd.Parameters.AddWithValue("@lado", motor.Grid.Corpos[0, 0].Lado);
            cmd.Parameters.AddWithValue("@modo", motor.UsarParalelismo ? "Paralelo" : "Sequencial");
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Grava um lote de interacoes dentro de UMA transacao.
        // Gravar uma linha por vez seria muito lento (uma ida ao servidor por corpo);
        // aqui cada INSERT leva varias linhas de uma vez: VALUES (...), (...), (...)
        public void GravarInteracoes(int numSimulacao, IReadOnlyList<RegistroInteracao> lote)
        {
            if (lote.Count == 0) return;

            using var conexao = AbrirConexao();
            using var transacao = conexao.BeginTransaction();

            // 1) uma linha em Resultados para cada interacao do lote
            var sqlResultados = new StringBuilder("INSERT INTO Resultados (NumSimulacao, NumInteracao, TempoSimulado) VALUES ");
            for (int r = 0; r < lote.Count; r++)
            {
                if (r > 0) sqlResultados.Append(',');
                sqlResultados.Append(Invariante, $"({numSimulacao},{lote[r].NumInteracao},{lote[r].TempoSimulado:R})");
            }
            Executar(conexao, transacao, sqlResultados.ToString());

            // 2) uma linha em Corpos para cada corpo de cada interacao
            const string cabecalhoCorpos =
                "INSERT INTO Corpos (NumSimulacao, NumInteracao, Linha, Coluna, NomeCorpo, IdMaterial, " +
                "MassaCorpo, DensidadeCorpo, Temperatura, CalorSensivel) VALUES ";
            var sqlCorpos = new StringBuilder(cabecalhoCorpos);
            int linhasNoComando = 0;

            foreach (RegistroInteracao reg in lote)
            {
                int n = reg.Temperaturas.GetLength(0);
                double volume = reg.Lado * reg.Lado * reg.Lado;

                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        Material mat = reg.Materiais[i, j];
                        double temperatura = reg.Temperaturas[i, j];
                        double massa = mat.Densidade * volume;
                        double calorSensivel = massa * mat.CalorEspecifico * temperatura; // Q = m.c.T

                        if (linhasNoComando > 0) sqlCorpos.Append(',');
                        sqlCorpos.Append(Invariante,
                            $"({numSimulacao},{reg.NumInteracao},{i},{j},'C[{i},{j}]',{mat.Id},{massa:R},{mat.Densidade:R},{temperatura:R},{calorSensivel:R})");
                        linhasNoComando++;

                        // comando cheio: envia e comeca outro
                        if (linhasNoComando == LinhasPorInsert)
                        {
                            Executar(conexao, transacao, sqlCorpos.ToString());
                            sqlCorpos.Clear().Append(cabecalhoCorpos);
                            linhasNoComando = 0;
                        }
                    }
                }
            }
            if (linhasNoComando > 0) Executar(conexao, transacao, sqlCorpos.ToString());

            // 3) atualiza quantas interacoes a simulacao ja tem
            Executar(conexao, transacao,
                $"UPDATE Simulacao SET NumInteracoes = GREATEST(NumInteracoes, {lote[^1].NumInteracao}) WHERE NumSimulacao = {numSimulacao}");

            // so confirma se tudo deu certo; se der erro no meio, nada do lote fica pela metade
            transacao.Commit();
        }

        private static void Executar(MySqlConnection conexao, MySqlTransaction transacao, string sql)
        {
            using var cmd = new MySqlCommand(sql, conexao, transacao);
            cmd.ExecuteNonQuery();
        }

        // REMONTA a simulacao a partir do banco: le a configuracao em Simulacao e o estado
        // de cada corpo (material e temperatura) na interacao pedida.
        // interacao = null -> usa a ultima interacao gravada.
        public SimulacaoCarregada CarregarSimulacao(int numSimulacao, int? interacao = null)
        {
            using var conexao = AbrirConexao();

            int tamanho;
            double lado, passo;
            string modo;
            int ultimaInteracao;
            using (var cmd = new MySqlCommand(
                "SELECT TamanhoGrid, LadoCorpo, TempoInteracoes, ModoProcessamento, NumInteracoes " +
                "FROM Simulacao WHERE NumSimulacao = @num", conexao))
            {
                cmd.Parameters.AddWithValue("@num", numSimulacao);
                using var leitor = cmd.ExecuteReader();
                if (!leitor.Read())
                    throw new InvalidOperationException($"Simulacao {numSimulacao} nao encontrada no banco.");
                tamanho = leitor.GetInt32(0);
                lado = leitor.GetDouble(1);
                passo = leitor.GetDouble(2);
                modo = leitor.GetString(3);
                ultimaInteracao = leitor.GetInt32(4);
            }

            int interacaoAlvo = interacao ?? ultimaInteracao;

            double tempoSimulado;
            using (var cmd = new MySqlCommand(
                "SELECT TempoSimulado FROM Resultados WHERE NumSimulacao = @num AND NumInteracao = @int", conexao))
            {
                cmd.Parameters.AddWithValue("@num", numSimulacao);
                cmd.Parameters.AddWithValue("@int", interacaoAlvo);
                object? valor = cmd.ExecuteScalar();
                if (valor == null)
                    throw new InvalidOperationException($"Interacao {interacaoAlvo} nao encontrada na simulacao {numSimulacao}.");
                tempoSimulado = Convert.ToDouble(valor);
            }

            // cria a malha com qualquer material e depois sobrescreve corpo a corpo com o que foi gravado
            var grid = new Grid(tamanho, lado, Material.MateriaisDisponiveis[0], 0);
            int corposLidos = 0;
            using (var cmd = new MySqlCommand(
                "SELECT Linha, Coluna, IdMaterial, Temperatura FROM Corpos " +
                "WHERE NumSimulacao = @num AND NumInteracao = @int", conexao))
            {
                cmd.Parameters.AddWithValue("@num", numSimulacao);
                cmd.Parameters.AddWithValue("@int", interacaoAlvo);
                using var leitor = cmd.ExecuteReader();
                while (leitor.Read())
                {
                    Corpo corpo = grid.Corpos[leitor.GetInt32(0), leitor.GetInt32(1)];
                    corpo.Material = Material.ObterPorId(leitor.GetInt32(2));
                    corpo.TemperaturaAtual = leitor.GetDouble(3);
                    corposLidos++;
                }
            }
            if (corposLidos != tamanho * tamanho)
                throw new InvalidOperationException(
                    $"A interacao {interacaoAlvo} tem {corposLidos} corpos gravados, eram esperados {tamanho * tamanho}.");

            var motor = new MotorSimulacao(grid, passo)
            {
                UsarParalelismo = modo == "Paralelo",
                NumeroInteracao = interacaoAlvo,
                TempoSimulado = tempoSimulado
            };
            return new SimulacaoCarregada { NumSimulacao = numSimulacao, Motor = motor };
        }
    }

    // Grava no banco em SEGUNDO PLANO (modelo produtor/consumidor).
    //
    // Produtor: a simulacao, que a cada passo coloca um RegistroInteracao na fila (Enfileirar).
    // Consumidor: uma Task separada que tira os registros da fila e grava em lotes.
    // Assim a simulacao e a tela nao ficam esperando o MySQL responder: calculo e gravacao
    // acontecem ao mesmo tempo, em threads diferentes. O Channel e uma fila feita
    // para ser usada por varias threads ao mesmo tempo, sem precisar de lock.
    public class GravadorAssincrono
    {
        private readonly RepositorioSimulacao repositorio;
        private readonly Channel<RegistroInteracao> fila = Channel.CreateUnbounded<RegistroInteracao>();
        private readonly Task tarefaConsumidora;
        private const int TamanhoMaximoLote = 100; // interacoes por transacao

        public int NumSimulacao { get; }
        public int InteracoesGravadas { get; private set; }
        public Exception? Erro { get; private set; } // preenchido se a gravacao falhar

        public GravadorAssincrono(RepositorioSimulacao repositorio, int numSimulacao)
        {
            this.repositorio = repositorio;
            NumSimulacao = numSimulacao;
            tarefaConsumidora = Task.Run(ConsumirFilaAsync);
        }

        // chamado pela simulacao; so coloca na fila e volta na hora
        public void Enfileirar(RegistroInteracao registro)
        {
            if (Erro == null) fila.Writer.TryWrite(registro);
        }

        private async Task ConsumirFilaAsync()
        {
            var lote = new List<RegistroInteracao>();
            try
            {
                // espera ter pelo menos um registro na fila
                while (await fila.Reader.WaitToReadAsync())
                {
                    // pega tudo que ja estiver na fila (ate o limite) e grava de uma vez
                    while (lote.Count < TamanhoMaximoLote && fila.Reader.TryRead(out RegistroInteracao? reg))
                        lote.Add(reg);

                    repositorio.GravarInteracoes(NumSimulacao, lote);
                    InteracoesGravadas += lote.Count;
                    lote.Clear();
                }
            }
            catch (Exception ex)
            {
                Erro = ex; // a interface verifica e avisa o usuario
            }
        }

        // fecha a fila e espera gravar o que ainda falta (usar ao resetar ou fechar a janela)
        public Task FinalizarAsync()
        {
            fila.Writer.TryComplete();
            return tarefaConsumidora;
        }
    }
}

using System;
using System.Collections.Generic;

namespace SimuladorTrocaCalor
{
    // ==========================================================
    // 1) MATERIAL
    // Representa as propriedades físicas de um tipo de material.
    // Não tem comportamento, só dados - é um "value object".
    // ==========================================================
    public class Material
    {
        public string Nome { get; }
        public double CondutividadeTermica { get; }   // k, em W/(m.K)
        public double CalorEspecifico { get; }         // c, em J/(kg.K)
        public double Densidade { get; }               // rho, em kg/m^3

        public Material(string nome, double k, double c, double densidade)
        {
            Nome = nome;
            CondutividadeTermica = k;
            CalorEspecifico = c;
            Densidade = densidade;
        }

        // TODO: crie uma lista estática (ou um Dictionary<string, Material>)
        // com pelo menos 8 materiais pré-cadastrados usando a tabela de
        // referência (cobre, aluminio, ferro, vidro, madeira, isopor, etc).
        // Isso facilita popular o ComboBox na interface gráfica depois.
        public static List<Material> MateriaisDisponiveis = new List<Material>
        {
            // new Material("Cobre", 385, 385, 8960),
            // ...
        };
    }

    // ==========================================================
    // 2) CORPO
    // Representa um cubo individual na malha.
    // ==========================================================
    public class Corpo
    {
        public int Linha { get; }
        public int Coluna { get; }
        public double Lado { get; }          // em metros
        public Material Material { get; set; } // precisa ser alteravel (set) - requisito do trabalho
        public double TemperaturaAtual { get; set; } // em Kelvin

        public Corpo(int linha, int coluna, double lado, Material material, double temperaturaInicial)
        {
            Linha = linha;
            Coluna = coluna;
            Lado = lado;
            Material = material;
            TemperaturaAtual = temperaturaInicial;
        }

        // Área da face de contato entre dois corpos vizinhos (face do cubo).
        // Como decidimos usar lado fixo para todos os corpos da malha,
        // a área de contato é simplesmente a área de uma face do cubo.
        public double AreaContato()
        {
            return Lado * Lado;
        }

        public double Volume()
        {
            return Math.Pow(Lado, 3);
        }

        // massa = densidade do material * volume do corpo
        public double Massa()
        {
            return Material.Densidade * Volume();
        }

        // Calor sensivel: Q = m . c . deltaT, considerando deltaT de 0K até a temp atual
        // (ou seja, deltaT = TemperaturaAtual - 0 = TemperaturaAtual)
        public double CalorSensivel()
        {
            return Massa() * Material.CalorEspecifico * TemperaturaAtual;
        }
    }

    // ==========================================================
    // 3) GRID (Malha)
    // Guarda a matriz NxN de corpos e sabe encontrar vizinhos.
    // ==========================================================
    public class Grid
    {
        public int Tamanho { get; }
        public Corpo[,] Corpos { get; }

        public Grid(int tamanho, double ladoPadrao, Material materialPadrao, double temperaturaInicialPadrao)
        {
            Tamanho = tamanho;
            Corpos = new Corpo[tamanho, tamanho];

            // TODO: preencher a matriz criando um Corpo em cada posicao
            // for (int i = 0; i < tamanho; i++)
            //     for (int j = 0; j < tamanho; j++)
            //         Corpos[i, j] = new Corpo(i, j, ladoPadrao, materialPadrao, temperaturaInicialPadrao);
        }

        // Retorna os corpos vizinhos validos (cima, baixo, esquerda, direita)
        // de uma posicao. Cuidado com bordas da matriz!
        public List<Corpo> ObterVizinhos(int linha, int coluna)
        {
            var vizinhos = new List<Corpo>();
            int[] deltaLinha = { -1, 1, 0, 0 };
            int[] deltaColuna = { 0, 0, -1, 1 };

            for (int k = 0; k < 4; k++)
            {
                int l = linha + deltaLinha[k];
                int c = coluna + deltaColuna[k];

                // só adiciona se estiver dentro dos limites da matriz
                if (l >= 0 && l < Tamanho && c >= 0 && c < Tamanho)
                {
                    vizinhos.Add(Corpos[l, c]);
                }
            }

            return vizinhos;
        }

        // Vizinho da direita, ou null se o corpo estiver na última coluna.
        // Usado pelo motor de simulação para varrer cada par de corpos
        // adjacentes uma única vez (ver ObterVizinhoBaixo também).
        public Corpo? ObterVizinhoDireita(int linha, int coluna)
        {
            if (coluna + 1 >= Tamanho) return null;
            return Corpos[linha, coluna + 1];
        }

        // Vizinho de baixo, ou null se o corpo estiver na última linha.
        public Corpo? ObterVizinhoBaixo(int linha, int coluna)
        {
            if (linha + 1 >= Tamanho) return null;
            return Corpos[linha + 1, coluna];
        }
    }

    // ==========================================================
    // 4) MOTOR DE SIMULACAO
    // Orquestra os calculos de troca de calor a cada "passo" (tick).
    // ==========================================================
    public class MotorSimulacao
    {
        public Grid Grid { get; }
        public double PassoDeTempo { get; set; } // deltaT da simulacao, em segundos

        public MotorSimulacao(Grid grid, double passoDeTempo)
        {
            Grid = grid;
            PassoDeTempo = passoDeTempo;
        }

        // Calcula a taxa de calor (q, em Watts) do corpo "origem" para o corpo
        // "destino", usando a Lei de Fourier: q = k . A . deltaT
        //
        // Convenção de sinal adotada: deltaT = TempOrigem - TempDestino.
        // Se origem estiver mais quente, q sai positivo (calor flui de origem
        // para destino, como esperado fisicamente). Se origem estiver mais
        // fria, q sai negativo, o que já representa corretamente um fluxo no
        // sentido contrário - não precisamos de nenhum "if" para decidir quem
        // cede calor pra quem, a própria equação resolve isso.
        //
        // Se os materiais forem diferentes, usamos o menor k dos dois, pois
        // o material menos condutor "restringe" o quanto pode fluir.
        private double CalcularFluxoDeCalor(Corpo origem, Corpo destino)
        {
            double k = Math.Min(origem.Material.CondutividadeTermica,
                                 destino.Material.CondutividadeTermica);

            // como todos os corpos tem o mesmo lado, a area de contato de
            // qualquer um deles serve (origem ou destino dá no mesmo)
            double area = origem.AreaContato();

            double deltaT = origem.TemperaturaAtual - destino.TemperaturaAtual;

            return k * area * deltaT;
        }

        // Executa UM passo da simulacao em toda a malha.
        //
        // FASE 1: calcula todos os fluxos de calor com base no estado atual
        // (sem alterar nenhuma temperatura ainda) e acumula quanto de energia
        // cada corpo ganhou/perdeu. Para não contar cada par de vizinhos duas
        // vezes, cada corpo só calcula a troca com o vizinho da DIREITA e o
        // de BAIXO - os vizinhos da esquerda/cima já foram contabilizados
        // quando o corpo vizinho correspondente foi processado.
        //
        // FASE 2: só depois de terminar a varredura inteira, aplicamos as
        // variações de temperatura de uma vez. Isso garante que a ordem de
        // varredura da matriz não influencie o resultado.
        public void ExecutarPasso()
        {
            int n = Grid.Tamanho;
            double[,] energiaAcumulada = new double[n, n];

            // ---------- FASE 1: calcular e acumular ----------
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Corpo atual = Grid.Corpos[i, j];

                    Corpo? vizinhoDireita = Grid.ObterVizinhoDireita(i, j);
                    if (vizinhoDireita != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoDireita);
                        double energiaTrocada = q * PassoDeTempo; // Q = q . deltaT

                        energiaAcumulada[i, j] -= energiaTrocada;     // quem envia, perde
                        energiaAcumulada[i, j + 1] += energiaTrocada; // quem recebe, ganha
                    }

                    Corpo? vizinhoBaixo = Grid.ObterVizinhoBaixo(i, j);
                    if (vizinhoBaixo != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoBaixo);
                        double energiaTrocada = q * PassoDeTempo;

                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i + 1, j] += energiaTrocada;
                    }
                }
            }

            // ---------- FASE 2: aplicar tudo de uma vez ----------
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Corpo corpo = Grid.Corpos[i, j];

                    // deltaT = Q / (m . c)  =>  isolando deltaT da formula de calor sensivel
                    double deltaTemperatura = energiaAcumulada[i, j] /
                        (corpo.Massa() * corpo.Material.CalorEspecifico);

                    corpo.TemperaturaAtual += deltaTemperatura;
                }
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SimuladorTrocaCalor
{
    // os materiais agora ficam em Materiais.cs, separados por classes (heranca)

    // corpo da simulacao, com propriedades geometricas e termicas
    public class Corpo
    {
        public int Linha { get; }
        public int Coluna { get; }
        public double Lado { get; }
        public Material Material { get; set; } 
        public double TemperaturaAtual { get; set; }

        public Corpo(int linha, int coluna, double lado, Material material, double temperaturaInicial)
        {
            Linha = linha;
            Coluna = coluna;
            Lado = lado;
            Material = material;
            TemperaturaAtual = temperaturaInicial;
        }

        public double AreaContato()
        {
            return Lado * Lado;
        }

        public double Volume()
        {
            return Lado * Lado * Lado;
        }

        public double Massa()
        {
            return Material.Densidade * Volume();
        }

        public double CalorSensivel()
        {
            return Massa() * Material.CalorEspecifico * TemperaturaAtual;
        }
    }

    // Matriz DxD de corpos GRID
    public class Grid
    {
        public int Tamanho { get; }
        public Corpo[,] Corpos { get; }

        public Grid(int tamanho, double ladoPadrao, Material materialPadrao, double temperaturaInicialPadrao)
        {
            if (tamanho <= 0) throw new ArgumentException("Tamanho deve ser >= 1", nameof(tamanho));
            if (materialPadrao == null) throw new ArgumentNullException(nameof(materialPadrao));

            Tamanho = tamanho;
            Corpos = new Corpo[tamanho, tamanho];

            for (int i = 0; i < Tamanho; i++)
            {
                for (int j = 0; j < Tamanho; j++)
                {
                    Corpos[i, j] = new Corpo(i, j, ladoPadrao, materialPadrao, temperaturaInicialPadrao);
                }
            }
        }
        public bool EhPosicaoValida(int linha, int coluna)
        {
            return linha >= 0 && linha < Tamanho && coluna >= 0 && coluna < Tamanho;
        }
        public List<Corpo> ObterVizinhos(int linha, int coluna)
        {
            var vizinhos = new List<Corpo>();
            int[] deltaLinha = { -1, 1, 0, 0 };
            int[] deltaColuna = { 0, 0, -1, 1 };

            for (int k = 0; k < 4; k++)
            {
                int l = linha + deltaLinha[k];
                int c = coluna + deltaColuna[k];

                if (l >= 0 && l < Tamanho && c >= 0 && c < Tamanho)
                {
                    vizinhos.Add(Corpos[l, c]);
                }
            }

            return vizinhos;
        }

        // Utilizado no ExecutarPasso para calcular o fluxo de calor entre vizinhos
        public Corpo? ObterVizinhoDireita(int linha, int coluna)
        {
            if (coluna + 1 >= Tamanho) return null;
            return Corpos[linha, coluna + 1];
        }

        public Corpo? ObterVizinhoBaixo(int linha, int coluna)
        {
            if (linha + 1 >= Tamanho) return null;
            return Corpos[linha + 1, coluna];
        }
    }

    // Roda a simulacao passo a passo O MOTOR DE SIMULACAO
    public class MotorSimulacao
    {
        public Grid Grid { get; }
        public double PassoDeTempo { get; set; }

        // false = processamento sequencial (versao da N1), true = paralelo (versao da N2)
        public bool UsarParalelismo { get; set; }

        // contadores usados para gravar cada interacao no banco e remontar a simulacao depois
        public int NumeroInteracao { get; set; }   // quantos passos ja foram executados
        public double TempoSimulado { get; set; }  // em segundos (NumeroInteracao * PassoDeTempo)

        public MotorSimulacao(Grid grid, double passoDeTempo)
        {
            Grid = grid;
            PassoDeTempo = passoDeTempo;
        }


        private double CalcularFluxoDeCalor(Corpo origem, Corpo destino)
        {
            double k = Math.Min(origem.Material.CondutividadeTermica,
                                 destino.Material.CondutividadeTermica);
            double area = origem.AreaContato();
            double deltaT = origem.TemperaturaAtual - destino.TemperaturaAtual;

            return k * area * deltaT;
        }

      
        public void ExecutarPasso()
        {
            
            double passoMaximo = 0.05;
            double tempoRestante = PassoDeTempo;

            
            while (tempoRestante > 0)
            {
                double dt = Math.Min(passoMaximo, tempoRestante);

                if (UsarParalelismo)
                    ExecutarSubPassoParalelo(dt);
                else
                    ExecutarSubPassoFisico(dt);

                tempoRestante -= dt;
            }

            NumeroInteracao++;
            TempoSimulado += PassoDeTempo;
        }

        // VERSAO SEQUENCIAL (N1): uma thread so varre a malha inteira
        public void ExecutarSubPassoFisico(double dt)
        {
            int n = Grid.Tamanho;
            double[,] energiaAcumulada = new double[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Corpo atual = Grid.Corpos[i, j];

                    Corpo? vizinhoDireita = Grid.ObterVizinhoDireita(i, j);
                    if (vizinhoDireita != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoDireita);
                        // energia = fluxo * tempo do SUB-passo (dt). Antes estava PassoDeTempo,
                        // o que aplicava o passo inteiro em cada sub-passo e multiplicava a troca.
                        double energiaTrocada = q * dt;

                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i, j + 1] += energiaTrocada;
                    }

                    Corpo? vizinhoBaixo = Grid.ObterVizinhoBaixo(i, j);
                    if (vizinhoBaixo != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoBaixo);
                        double energiaTrocada = q * dt;

                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i + 1, j] += energiaTrocada;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    AplicarEnergia(Grid.Corpos[i, j], energiaAcumulada[i, j]);
                }
            }
        }

        // VERSAO PARALELA (N2): divide o trabalho entre os nucleos do processador com Parallel.For.
        //
        // O cuidado e evitar "condicao de corrida": duas threads somando na mesma posicao
        // de energiaAcumulada ao mesmo tempo, o que perderia valores. Na versao sequencial cada
        // corpo escreve em si e no vizinho da direita E de baixo; se cada thread pegasse uma linha,
        // a thread da linha i escreveria na linha i+1, que e de outra thread.
        //
        // Solucao: separar a troca em duas fases independentes, cada uma sem conflito:
        //   Fase A (horizontal): cada thread pega UMA LINHA e troca os pares (i,j)-(i,j+1).
        //                        So escreve dentro da propria linha.
        //   Fase B (vertical):   cada thread pega UMA COLUNA e troca os pares (i,j)-(i+1,j).
        //                        So escreve dentro da propria coluna.
        // O Parallel.For so retorna quando todas as threads terminam, entao a fase B
        // nunca roda junto com a fase A. Assim nao precisa de lock e cada par continua
        // sendo calculado uma unica vez (mesmo resultado da versao sequencial).
        public void ExecutarSubPassoParalelo(double dt)
        {
            int n = Grid.Tamanho;
            double[,] energiaAcumulada = new double[n, n];
            Corpo[,] corpos = Grid.Corpos;

            // Fase A: pares horizontais, uma linha por tarefa
            Parallel.For(0, n, i =>
            {
                for (int j = 0; j < n - 1; j++)
                {
                    double energiaTrocada = CalcularFluxoDeCalor(corpos[i, j], corpos[i, j + 1]) * dt;
                    energiaAcumulada[i, j] -= energiaTrocada;
                    energiaAcumulada[i, j + 1] += energiaTrocada;
                }
            });

            // Fase B: pares verticais (i,j)-(i+1,j). Cada tarefa pega a "faixa" das linhas i e i+1.
            // Se rodasse todas as faixas juntas, a faixa 0-1 e a faixa 1-2 escreveriam ambas na linha 1.
            // Por isso roda em duas rodadas: primeiro as faixas que comecam em linha PAR (0-1, 2-3, ...),
            // depois as que comecam em linha IMPAR (1-2, 3-4, ...). Dentro de cada rodada nenhuma
            // faixa encosta na outra.
            // (Percorrer por linha em vez de por coluna tambem e melhor para o cache, porque
            // o C# guarda a matriz [i, j] linha por linha na memoria.)
            for (int paridade = 0; paridade < 2; paridade++)
            {
                int inicio = paridade;
                int qtdFaixas = (n - 1 - inicio + 1) / 2; // quantas linhas i (com i+1 < n) tem essa paridade
                Parallel.For(0, qtdFaixas, f =>
                {
                    int i = inicio + 2 * f;
                    for (int j = 0; j < n; j++)
                    {
                        double energiaTrocada = CalcularFluxoDeCalor(corpos[i, j], corpos[i + 1, j]) * dt;
                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i + 1, j] += energiaTrocada;
                    }
                });
            }

            // Fase de aplicacao: cada corpo so altera a propria temperatura,
            // entao as linhas podem ser atualizadas em paralelo sem conflito
            Parallel.For(0, n, i =>
            {
                for (int j = 0; j < n; j++)
                {
                    AplicarEnergia(corpos[i, j], energiaAcumulada[i, j]);
                }
            });
        }

        // Q = m * c * dT  ->  dT = Q / (m * c)
        private static void AplicarEnergia(Corpo corpo, double energia)
        {
            corpo.TemperaturaAtual += energia / (corpo.Massa() * corpo.Material.CalorEspecifico);
        }
    }
}
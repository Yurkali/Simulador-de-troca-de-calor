using System;
using System.Collections.Generic;

namespace SimuladorTrocaCalor
{
    // Propriedades físicas dos materiais (k, c, densidade) MATERIAIS
    public class Material
    {
        public string Nome { get; }
        public double CondutividadeTermica { get; } // k - W/(m.K)
        public double CalorEspecifico { get; }       // c - J/(kg.K)
        public double Densidade { get; }             // em kg/m3

        public Material(string nome, double k, double c, double densidade)
        {
            Nome = nome;
            CondutividadeTermica = k;
            CalorEspecifico = c;
            Densidade = densidade;
        }

        // Matreiais cadastrados
        public static List<Material> MateriaisDisponiveis = new List<Material>
        {
                new Material("Cobre", 385, 385, 8960),
                new Material("Aluminio", 205, 900, 2700),
                new Material("Ferro", 80, 450, 7870),
                new Material("Vidro", 1.05, 840, 2500),
                new Material("Madeira", 0.12, 1700, 600),
                new Material("Isopor", 0.033, 1400, 30),
                new Material("Concreto", 1.7, 880, 2400),
                new Material("Aço", 50, 500, 7850)
        };
    }

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
        public Corpo ObterVizinhoDireita(int linha, int coluna)
        {
            if (coluna + 1 >= Tamanho) return null;
            return Corpos[linha, coluna + 1];
        }

        public Corpo ObterVizinhoBaixo(int linha, int coluna)
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

        // calcula todas as trocas 
        public void ExecutarPasso()
        {
            int n = Grid.Tamanho;
            double[,] energiaAcumulada = new double[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Corpo atual = Grid.Corpos[i, j];

                    Corpo vizinhoDireita = Grid.ObterVizinhoDireita(i, j);
                    if (vizinhoDireita != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoDireita);
                        double energiaTrocada = q * PassoDeTempo;

                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i, j + 1] += energiaTrocada;
                    }

                    Corpo vizinhoBaixo = Grid.ObterVizinhoBaixo(i, j);
                    if (vizinhoBaixo != null)
                    {
                        double q = CalcularFluxoDeCalor(atual, vizinhoBaixo);
                        double energiaTrocada = q * PassoDeTempo;

                        energiaAcumulada[i, j] -= energiaTrocada;
                        energiaAcumulada[i + 1, j] += energiaTrocada;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    Corpo corpo = Grid.Corpos[i, j];
                    double deltaTemperatura = energiaAcumulada[i, j] /
                        (corpo.Massa() * corpo.Material.CalorEspecifico);

                    corpo.TemperaturaAtual += deltaTemperatura;
                }
            }
        }
    }
}
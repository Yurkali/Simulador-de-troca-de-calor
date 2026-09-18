using System;
using SimuladorTrocaCalor;

class Program
{
    static void Main()
    {
        int tamanho = 5;
        double lado = 0.01; // m
        var materialPadrao = Material.MateriaisDisponiveis[1]; // Alumínio
        double tempPadrao = 20.0;

        var grid = new Grid(tamanho, lado, materialPadrao, tempPadrao);

        // Condições iniciais: um canto quente e o canto oposto frio
        grid.Corpos[0, 0].TemperaturaAtual = 100.0;
        grid.Corpos[tamanho - 1, tamanho - 1].TemperaturaAtual = 0.0;

        var motor = new MotorSimulacao(grid, passoDeTempo: 0.1); // segundos

        int passos = 200;
        for (int passo = 0; passo <= passos; passo++)
        {
            if (passo % 20 == 0)
            {
                Console.WriteLine($"Passo {passo}");
                ImprimirGrid(grid);
            }

            motor.ExecutarPasso();
        }
    }

    static void ImprimirGrid(Grid grid)
    {
        for (int i = 0; i < grid.Tamanho; i++)
        {
            for (int j = 0; j < grid.Tamanho; j++)
            {
                Console.Write($"{grid.Corpos[i, j].TemperaturaAtual,6:F1} ");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }
}
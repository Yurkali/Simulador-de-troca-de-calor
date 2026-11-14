using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SimuladorTrocaCalor;

class Program
{
    // dotnet run -c Release                 -> teste simples no console (como na N1)
    // dotnet run -c Release -- benchmark    -> compara sequencial (N1) x paralelo (N2)
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("benchmark", StringComparison.OrdinalIgnoreCase))
            Benchmark.Executar();
        else
            TesteConsole();
    }

    static void TesteConsole()
    {
        int tamanho = 5;
        double lado = 0.01; // m
        var materialPadrao = Material.MateriaisDisponiveis[1]; // Alumínio
        double tempPadrao = 20.0 + 273.15; // o motor trabalha em Kelvin

        var grid = new Grid(tamanho, lado, materialPadrao, tempPadrao);

        // Condições iniciais: um canto quente e o canto oposto frio
        grid.Corpos[0, 0].TemperaturaAtual = 100.0 + 273.15;
        grid.Corpos[tamanho - 1, tamanho - 1].TemperaturaAtual = 0.0 + 273.15;

        var motor = new MotorSimulacao(grid, passoDeTempo: 0.1); // segundos

        int passos = 100;
        for (int passo = 0; passo <= passos; passo++)
        {
            if (passo % 100 == 0)
            {
                Console.WriteLine($"Passo {passo}");
                ImprimirGrid(grid);
            }

            motor.ExecutarPasso();
        }
        Console.WriteLine("Resultado final:");
        ImprimirGrid(grid);
    }

    // imprime em Celsius
    static void ImprimirGrid(Grid grid)
    {
        for (int i = 0; i < grid.Tamanho; i++)
        {
            for (int j = 0; j < grid.Tamanho; j++)
            {
                Console.Write($"{grid.Corpos[i, j].TemperaturaAtual - 273.15,6:F1} ");
            }
            Console.WriteLine();
        }
        Console.WriteLine();
    }
}

// Mede o tempo de processamento das duas versoes do motor, para o relatorio N1 x N2.
// So mede o CALCULO (sem tela e sem banco), para comparar apenas o efeito do paralelismo.
static class Benchmark
{
    // tamanhos de malha testados (N x N)
    static readonly int[] Tamanhos = { 5, 10, 25, 50, 100, 200, 400, 800 };
    const int Repeticoes = 5; // cada medicao roda 5 vezes e usa a mediana (tira ruido do SO)

    public static void Executar()
    {
        Console.WriteLine($"Processador: {Environment.ProcessorCount} nucleos logicos");
        Console.WriteLine($".NET {Environment.Version}, {(Debugger.IsAttached ? "com debugger" : "sem debugger")}");
        Console.WriteLine();
        Console.WriteLine($"{"N x N",9} {"Corpos",8} {"Passos",7} {"Seq (ms)",10} {"Par (ms)",10} {"Speedup",8} {"Dif. max (K)",13}");

        var linhasCsv = new List<string> { "Tamanho;Corpos;Passos;SequencialMs;ParaleloMs;Speedup;DiferencaMaximaK" };

        foreach (int n in Tamanhos)
        {
            // passos suficientes para cada medicao demorar em torno de 0,5 a 1 s.
            // Com medicoes curtas (~50 ms) qualquer coisa rodando no PC, ou o Windows trocando
            // a thread entre nucleos rapidos (P) e economicos (E), mudava muito o resultado
            // de uma rodada para outra. Medindo mais tempo esse ruido fica pequeno.
            int passos = Math.Clamp(40_000_000 / (n * n), 100, 200_000);

            // aquecimento: a 1a execucao inclui compilacao JIT e criacao das threads do pool
            Rodar(n, passos / 10 + 1, paralelo: false, out _);
            Rodar(n, passos / 10 + 1, paralelo: true, out _);

            var temposSeq = new List<double>();
            var temposPar = new List<double>();
            Grid? gridSeq = null, gridPar = null;
            for (int r = 0; r < Repeticoes; r++)
            {
                temposSeq.Add(Rodar(n, passos, paralelo: false, out gridSeq));
                temposPar.Add(Rodar(n, passos, paralelo: true, out gridPar));
            }

            double seq = Mediana(temposSeq);
            double par = Mediana(temposPar);
            double speedup = seq / par;

            // confere que as duas versoes chegam no mesmo resultado (so muda a ordem das somas)
            double difMax = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    difMax = Math.Max(difMax, Math.Abs(gridSeq!.Corpos[i, j].TemperaturaAtual - gridPar!.Corpos[i, j].TemperaturaAtual));

            Console.WriteLine($"{n + "x" + n,9} {n * n,8} {passos,7} {seq,10:F1} {par,10:F1} {speedup,7:F2}x {difMax,13:E1}");
            linhasCsv.Add(string.Join(";", n, n * n, passos,
                seq.ToString("F2", CultureInfo.InvariantCulture),
                par.ToString("F2", CultureInfo.InvariantCulture),
                speedup.ToString("F3", CultureInfo.InvariantCulture),
                difMax.ToString("E2", CultureInfo.InvariantCulture)));
        }

        string arquivo = Path.GetFullPath("resultado_benchmark.csv");
        File.WriteAllLines(arquivo, linhasCsv);
        Console.WriteLine();
        Console.WriteLine("Speedup = tempo sequencial / tempo paralelo (maior que 1 = paralelo mais rapido)");
        Console.WriteLine($"Resultados salvos em {arquivo}");
    }

    // monta a mesma malha de teste e roda 'passos' passos; devolve o tempo em ms
    static double Rodar(int n, int passos, bool paralelo, out Grid grid)
    {
        grid = new Grid(n, 0.01, new Aluminio(), 25 + 273.15);
        grid.Corpos[0, 0].TemperaturaAtual = 100 + 273.15;
        grid.Corpos[n - 1, n - 1].TemperaturaAtual = 0 + 273.15;

        // coloca isopor no meio para os materiais nao serem todos iguais
        grid.Corpos[n / 2, n / 2].Material = new Isopor();

        // passo de 0,05 s = exatamente 1 sub-passo fisico por passo
        var motor = new MotorSimulacao(grid, 0.05) { UsarParalelismo = paralelo };

        var cronometro = Stopwatch.StartNew();
        for (int p = 0; p < passos; p++)
            motor.ExecutarPasso();
        cronometro.Stop();

        return cronometro.Elapsed.TotalMilliseconds;
    }

    static double Mediana(List<double> valores)
    {
        var ordenados = valores.OrderBy(v => v).ToList();
        return ordenados[ordenados.Count / 2];
    }
}

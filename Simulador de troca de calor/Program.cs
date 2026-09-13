using SimuladorTrocaCalor;

Console.WriteLine("Simulador de troca de calor iniciado.");

// Teste rápido: cria um material simples e um corpo, só pra confirmar
// que está tudo compilando e ligado corretamente entre os arquivos.
Material cobre = new Material("Cobre", 385, 385, 8960);
Corpo corpoTeste = new Corpo(0, 0, 0.1, cobre, 350); // lado de 0.1m, 350K

Console.WriteLine($"Massa do corpo teste: {corpoTeste.Massa()} kg");
Console.WriteLine($"Calor sensivel do corpo teste: {corpoTeste.CalorSensivel()} J");
-- Consultas da N1 adaptadas para o simulador de calor.
-- Troque o 1 pelo numero da simulacao que quiser ver.
USE simulacao_db;

-- a) Todos os resultados de uma simulacao
SELECT NumSimulacao, NumInteracao, TempoSimulado
FROM Resultados
WHERE NumSimulacao = 1
ORDER BY NumInteracao;

-- b) Dado um resultado, listar seus corpos (temperatura em Celsius tambem)
SELECT c.NomeCorpo, m.Nome AS Material, m.Classe, c.MassaCorpo, c.DensidadeCorpo,
       c.Temperatura, c.Temperatura - 273.15 AS TemperaturaCelsius, c.CalorSensivel
FROM Corpos c
JOIN Material m ON m.IdMaterial = c.IdMaterial
WHERE c.NumSimulacao = 1
  AND c.NumInteracao = 1
ORDER BY c.Linha, c.Coluna;

-- c) Todos os resultados de uma simulacao, com os respectivos corpos
SELECT r.NumInteracao, r.TempoSimulado, c.NomeCorpo, m.Nome AS Material,
       c.Temperatura - 273.15 AS TemperaturaCelsius
FROM Resultados r
JOIN Corpos c
  ON c.NumSimulacao = r.NumSimulacao
 AND c.NumInteracao = r.NumInteracao
JOIN Material m ON m.IdMaterial = c.IdMaterial
WHERE r.NumSimulacao = 1
ORDER BY r.NumInteracao, c.Linha, c.Coluna;

-- d) Quantidade de resultados de uma simulacao
SELECT COUNT(*) AS QtdResultados
FROM Resultados
WHERE NumSimulacao = 1;

-- e) Quantidade final de corpos de uma simulacao
SELECT COUNT(*) AS QtdFinalCorpos
FROM Corpos
WHERE NumSimulacao = 1
  AND NumInteracao = (SELECT MAX(NumInteracao)
                      FROM Resultados
                      WHERE NumSimulacao = 1);

-- f) Corpos do ultimo resultado de uma simulacao (estado usado para remontar)
SELECT c.Linha, c.Coluna, c.NomeCorpo, m.Nome AS Material,
       c.Temperatura - 273.15 AS TemperaturaCelsius
FROM Corpos c
JOIN Material m ON m.IdMaterial = c.IdMaterial
WHERE c.NumSimulacao = 1
  AND c.NumInteracao = (SELECT MAX(NumInteracao)
                        FROM Resultados
                        WHERE NumSimulacao = 1)
ORDER BY c.Linha, c.Coluna;

-- g) Evolucao de um corpo ao longo do tempo (ex.: canto quente C[0,0])
SELECT r.TempoSimulado, c.Temperatura - 273.15 AS TemperaturaCelsius
FROM Corpos c
JOIN Resultados r
  ON r.NumSimulacao = c.NumSimulacao
 AND r.NumInteracao = c.NumInteracao
WHERE c.NumSimulacao = 1 AND c.Linha = 0 AND c.Coluna = 0
ORDER BY r.NumInteracao;

-- h) Lista de simulacoes gravadas
SELECT NumSimulacao, DataSimulacao, TamanhoGrid, LadoCorpo, TempoInteracoes,
       NumInteracoes, ModoProcessamento
FROM Simulacao
ORDER BY NumSimulacao DESC;

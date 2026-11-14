# Simulador de troca de calor

Trabalho acadêmico em C# (.NET 8): simula a condução de calor (Lei de Fourier) em uma malha N×N de corpos cúbicos.
O usuário responde em português; comentários do código também são em português, escritos como explicação didática
(o professor avalia os comentários). Regra da disciplina: nada de código copiado da internet/colegas.

## Entrega N2 (prazo 16/11/2026)

1. Gravar cada interação (todos os corpos) no MySQL, com dados suficientes para remontar a simulação.
2. Usar computação paralela no processamento.
3. Relatório de performance N1 (sequencial) × N2 (paralelo).
Professor também pediu: materiais separados por classes (feito com herança em `Materiais.cs`).

## Estrutura

- `Simulador de troca de calor/` (biblioteca + console)
  - `Materiais.cs`: `Material` abstrata → categorias abstratas `Metal`, `Ceramico`, `Polimero`, `MaterialNatural` → 8 classes concretas (Cobre, Aluminio, Ferro, Aco, Vidro, Concreto, Isopor, Madeira). Cada uma tem `Id` fixo, usado como chave no banco.
  - `MotorSimulacao.cs`: `Corpo`, `Grid`, `MotorSimulacao`. `UsarParalelismo` escolhe entre `ExecutarSubPassoFisico` (sequencial, N1) e `ExecutarSubPassoParalelo` (N2). `NumeroInteracao` e `TempoSimulado` são contados no motor.
  - `BancoDeDados.cs`: `RegistroInteracao` (cópia do estado), `RepositorioSimulacao` (MySqlConnector: inserts em lote na transação, `CarregarSimulacao` remonta), `GravadorAssincrono` (produtor/consumidor com `Channel`, grava em segundo plano).
  - `Program.cs`: `dotnet run -c Release` = teste no console; `-- benchmark` = medição seq × par, gera `resultado_benchmark.csv` na pasta atual.
- `Interface/`: WPF. Checkboxes "Processamento paralelo" e "Gravar no banco", botão "Carregar última interação" (remonta pelo nº da simulação), status da gravação. A string de conexão fica em `Interface/conexao.txt` (no .gitignore; modelo em `conexao.exemplo.txt`).
- `Banco/simulacao_db.sql`: cria o schema (adaptado do modelo da N1 do usuário, `Documents\Trabalho N1.sql`; apaga as tabelas antigas). `Banco/consultas.sql`: consultas a)–h).
- `Relatorio/`: CSVs do benchmark.
- Solução: `Simulador-TDC.sln`.

## Decisões de projeto (não mudar sem discutir)

1. Mesmo lado para todos os corpos, para a área de contato não ficar ambígua.
2. O sinal de ΔT (origem − destino) define o sentido do fluxo, sem `if`.
3. Materiais diferentes: usa o menor k (pedido do enunciado).
4. Duas fases por passo: calcula todas as trocas e só depois aplica.
5. Cada par de vizinhos é calculado uma vez (só direita e baixo). No paralelo: fase horizontal por linha, fase vertical em faixas de linhas pares e depois ímpares (sem lock, sem condição de corrida).
6. Kelvin internamente e no banco. Celsius só na entrada/exibição da interface.
7. `ExecutarPasso` divide o passo em sub-passos de 0,05 s; a energia usa o `dt` do sub-passo (bug antigo usava `PassoDeTempo`).
8. Banco: interação 0 = estado inicial. Material de cada corpo é gravado em toda interação (pode mudar com clique).

## Benchmark

Paralelo fica mais lento até ~50×50 (custo de dividir em threads > trabalho) e passa a ganhar a partir de ~100×100. Resultados idênticos entre os modos (diferença ≤ 1e-13 K).

- `Relatorio/resultado_benchmark.csv`: rodada de 09/11/2026 no PC com 16 threads lógicas. 0,83× em 50×50, 2,25× em 100×100, pico de 3,28× em 400×400, 1,70× em 800×800. É a que está no relatório N1 × N2.
- `Relatorio/resultado_benchmark_1.csv` e `_2.csv`: rodadas anteriores no notebook i5-13420H (12 threads). 1,22× em 100×100 e 1,8–1,9× em 400×400 e 800×800.
- Essas três rodadas usavam medições curtas (~50 ms, `4_000_000/(n*n)` passos) e variavam muito entre execuções (em 14/11 o 100×100 deu 1,25× numa e 0,67× na outra). Desde 14/11 o benchmark usa `40_000_000/(n*n)` passos (limitado a 100–200 000), e cada medição dura ~0,5–2 s.
- `Relatorio/resultado_benchmark_notebook_longo_1.csv` e `_2.csv`: notebook i5-13420H, 14/11/2026, medição longa, na tomada. 0,91–0,98× em 50×50, 1,5–1,6× em 100×100, 2,35–2,82× em 400×400, 3,0–3,4× em 800×800.

## Teste manual

Rodar `Banco/simulacao_db.sql` no Workbench, criar `Interface/conexao.txt`, abrir a interface, criar malha, Play, depois conferir com `Banco/consultas.sql` e testar "Carregar".

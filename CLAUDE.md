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

## Benchmark (i5-13420H, 4P+4E núcleos, 12 threads)

Paralelo fica mais lento até ~50×50 (custo de dividir em threads > trabalho) e passa a ganhar a partir de ~100×100; ~1,8–1,9× em 400×400 e 800×800. Resultados idênticos entre os modos (diferença ≤ 1e-13 K).

## Teste manual

Rodar `Banco/simulacao_db.sql` no Workbench, criar `Interface/conexao.txt`, abrir a interface, criar malha, Play, depois conferir com `Banco/consultas.sql` e testar "Carregar".

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SimuladorTrocaCalor;

namespace Interface
{
    public partial class MainWindow : Window
    {
        private MotorSimulacao? motor;
        private Button[,]? botoesGrid;

        //timer que roda a simulacao sozinha (botao Play/Pause)
        private DispatcherTimer timer;
        private const int passosPorTick = 20; //quantos passos roda a cada tick antes de redesenhar

        //N2: banco de dados. O gravador manda cada interacao pro MySQL em segundo plano
        private RepositorioSimulacao? repositorio;
        private GravadorAssincrono? gravador;

        //arquivo com a string de conexao, fica do lado do .exe (ver conexao.exemplo.txt)
        private const string arquivoConexao = "conexao.txt";

        public MainWindow()
        {
            InitializeComponent();

            //a cada 50 ms roda alguns passos e atualiza a tela
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += Timer_Tick;

            //definindo oque sera mostrado na combox (Nome vem de cada classe de material)
            cmbMaterial.DisplayMemberPath = "Nome";
            cmbMaterial.ItemsSource = Material.MateriaisDisponiveis;
            cmbMaterial.SelectedIndex = 1;
        }

        //Le um numero decimal digitado pelo usuario, aceitando "0.01" ou "0,01".
        //Sem isso, no Windows em portugues o double.TryParse trata o ponto como separador
        //de MILHAR: "0.01" virava 1 e "1.0" virava 10 (cubo de 1 m e passo de 10 s!).
        //Por isso troca a virgula por ponto e le sempre no formato invariante (ponto decimal),
        //sem permitir separador de milhar.
        private static bool LerNumero(string texto, out double valor)
        {
            return double.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out valor);
        }

        //Botão de "Criar/Resetar Grid"
        private async void btnCriarGrid_Click(object sender, RoutedEventArgs e)
        {
            //validando os campos antes de usar, se algum estiver invalido avisa e cancela
            if (!int.TryParse(txtTamanho.Text, out int tamanho) || tamanho <= 0)
            {
                MessageBox.Show("Tamanho invalido. Digite um numero inteiro maior que zero.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!LerNumero(txtLado.Text, out double lado) || lado <= 0)
            {
                MessageBox.Show("Lado invalido. Digite um numero maior que zero.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //o usuario digita em Celsius, mas nao pode ser menor que o zero absoluto
            if (!LerNumero(txtTempInicial.Text, out double tempInicialCelsius) || tempInicialCelsius < -273.15)
            {
                MessageBox.Show("Temperatura inicial invalida. Digite um valor em Celsius (>= -273,15).",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!LerNumero(txtPassoTempo.Text, out double passoTempo) || passoTempo <= 0)
            {
                MessageBox.Show("Passo de tempo invalido. Digite um numero maior que zero.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (cmbMaterial.SelectedItem == null)
            {
                MessageBox.Show("Selecione um material antes de criar a malha.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Material materialSelecionado = (Material)cmbMaterial.SelectedItem;

            //o grid em que o material vai estar
            //chamando oo codigo do simulador pela variavel grid do codigo original, ele vai fazer os calculos e retornar os resultados o codigo apresentar na interface
            //o motor trabalha em Kelvin (o calor sensivel usa 0 K como referencia),
            //entao converte o que o usuario digitou em Celsius antes de passar
            double tempInicialKelvin = tempInicialCelsius + 273.15;

            SimuladorTrocaCalor.Grid gridLogico = new SimuladorTrocaCalor.Grid(tamanho, lado, materialSelecionado, tempInicialKelvin);

            //condições iniciais (100 C e 0 C, convertidas pra Kelvin)
            gridLogico.Corpos[0, 0].TemperaturaAtual = 100.0 + 273.15;
            gridLogico.Corpos[tamanho - 1, tamanho - 1].TemperaturaAtual = 0.0 + 273.15;

            var novoMotor = new MotorSimulacao(gridLogico, passoTempo)
            {
                UsarParalelismo = chkParalelo.IsChecked == true
            };

            await IniciarSimulacaoAsync(novoMotor, numSimulacaoExistente: null);
        }

        //Botão direito num bloco: muda a temperatura dele para o valor do campo "Temp. do bloco".
        //Serve para criar novas fontes de calor/frio no meio da simulacao.
        private void BotaoGrid_ClickDireito(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (motor == null) return;

            if (!LerNumero(txtTempBloco.Text, out double tempCelsius) || tempCelsius < -273.15)
            {
                MessageBox.Show("Temperatura do bloco invalida. Digite um valor em Celsius (>= -273,15).",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var pos = (Tuple<int, int>)((Button)sender).Tag;

            //o motor trabalha em Kelvin, entao converte antes de gravar no corpo
            motor.Grid.Corpos[pos.Item1, pos.Item2].TemperaturaAtual = tempCelsius + 273.15;

            AtualizarMatrizVisual();
        }

        //Botão "Carregar": remonta uma simulacao gravada no banco e continua a partir dela
        private async void btnCarregar_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtNumSimulacao.Text, out int numSimulacao) || numSimulacao <= 0)
            {
                MessageBox.Show("Digite o numero da simulacao (veja a coluna NumSimulacao no banco).",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RepositorioSimulacao? repo = ObterRepositorio();
            if (repo == null) return;

            try
            {
                //a leitura roda fora da thread da tela pra janela nao congelar
                SimulacaoCarregada carregada = await Task.Run(() => repo.CarregarSimulacao(numSimulacao));
                chkParalelo.IsChecked = carregada.Motor.UsarParalelismo;
                await IniciarSimulacaoAsync(carregada.Motor, carregada.NumSimulacao);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Nao foi possivel carregar a simulacao:\n" + ex.Message,
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        //parte comum de "criar" e "carregar": troca o motor, monta os botoes e prepara a gravacao
        private async Task IniciarSimulacaoAsync(MotorSimulacao novoMotor, int? numSimulacaoExistente)
        {
            //se estava rodando, para antes de trocar o motor
            timer.Stop();
            btnPlayPause.Content = "Play";

            //termina de gravar o que faltou da simulacao anterior
            await FinalizarGravacaoAsync();

            motor = novoMotor;
            int tamanho = motor.Grid.Tamanho;

            //mostando o visual da matriz
            gridMatrizVisual.Children.Clear(); //limpa ela quando iniciada
            gridMatrizVisual.Rows = tamanho;//ajustes de tamanho
            gridMatrizVisual.Columns = tamanho;
            botoesGrid = new Button[tamanho, tamanho];//cria os blocos do grid como botoes

            for (int i = 0; i < tamanho; i++)
            {
                for (int j = 0; j < tamanho; j++)
                {
                    Button btn = new Button //cria o botao
                    {
                        Margin = new Thickness(1),//cria uma margem no botão
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Tag = new Tuple<int, int>(i, j) //isso serve para guarda a linha e coluna
                    };
                    btn.Click += BotaoGrid_Click;
                    btn.MouseRightButtonUp += BotaoGrid_ClickDireito;

                    botoesGrid[i, j] = btn;//adiciona o botão na matriz
                    gridMatrizVisual.Children.Add(btn);//adiciona na matriz visual
                }
            }

            //atualiza toda a matriz visual com as informações predefinidas na ação
            AtualizarMatrizVisual();

            if (chkGravarBanco.IsChecked == true)
                await IniciarGravacaoAsync(numSimulacaoExistente);

            AtualizarStatus();

            //ativar o botao para executar o passo
            btnAvancarPasso.IsEnabled = true;
            //ativar também o botão Play/Pause
            btnPlayPause.IsEnabled = true;
        }

        //cria a simulacao no banco (ou reaproveita a carregada) e liga o gravador
        private async Task IniciarGravacaoAsync(int? numSimulacaoExistente)
        {
            RepositorioSimulacao? repo = ObterRepositorio();
            if (repo == null || motor == null)
            {
                chkGravarBanco.IsChecked = false;
                return;
            }

            MotorSimulacao motorAtual = motor;
            try
            {
                int numSimulacao = await Task.Run(() =>
                {
                    repo.SincronizarMateriais();
                    return numSimulacaoExistente ?? repo.CriarSimulacao(motorAtual);
                });

                gravador = new GravadorAssincrono(repo, numSimulacao);

                //simulacao nova: grava a interacao 0 (estado inicial). Na carregada ela ja existe no banco
                if (numSimulacaoExistente == null)
                    gravador.Enfileirar(RegistroInteracao.Capturar(motorAtual));

                txtNumSimulacao.Text = numSimulacao.ToString();
            }
            catch (Exception ex)
            {
                gravador = null;
                chkGravarBanco.IsChecked = false;
                MessageBox.Show("Nao foi possivel conectar no banco. A simulacao vai rodar sem gravar.\n\n" + ex.Message,
                                "Banco de dados", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        //le a string de conexao do arquivo conexao.txt (so na primeira vez)
        private RepositorioSimulacao? ObterRepositorio()
        {
            if (repositorio != null) return repositorio;

            string caminho = Path.Combine(AppContext.BaseDirectory, arquivoConexao);
            if (!File.Exists(caminho))
            {
                MessageBox.Show($"Arquivo {arquivoConexao} nao encontrado.\n\nCopie Interface\\conexao.exemplo.txt para " +
                                $"Interface\\{arquivoConexao}, coloque o usuario e a senha do seu MySQL e rode de novo.",
                                "Banco de dados", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            repositorio = new RepositorioSimulacao(File.ReadAllText(caminho).Trim());
            return repositorio;
        }

        private async Task FinalizarGravacaoAsync()
        {
            if (gravador == null) return;
            GravadorAssincrono antigo = gravador;
            gravador = null;
            txtStatus.Text = "Terminando de gravar no banco...";
            await antigo.FinalizarAsync();
        }

        //executa um passo e manda a nova interacao pra fila de gravacao
        private void ExecutarPassoEGravar()
        {
            if (motor == null) return;
            motor.ExecutarPasso();
            gravador?.Enfileirar(RegistroInteracao.Capturar(motor));
        }

        //Botão "Avançar"
        private void btnAvancarPasso_Click(object sender, RoutedEventArgs e)
        {
            if (motor == null) return;

            //roda um passo da simulacao (o valor do passo de tempo ja foi
            //validado e usado na criacao do motor, aqui so avancamos)
            ExecutarPassoEGravar();

            //atualiza a interface com as novas temperaturas
            AtualizarMatrizVisual();
            AtualizarStatus();
        }

        //Botão "Play/Pause": liga e desliga a simulacao automatica
        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (motor == null) return;

            if (timer.IsEnabled)
            {
                timer.Stop();
                btnPlayPause.Content = "Play";
            }
            else
            {
                timer.Start();
                btnPlayPause.Content = "Pause";
            }
        }

        //permite trocar entre sequencial e paralelo no meio da simulacao
        private void chkParalelo_Alterado(object sender, RoutedEventArgs e)
        {
            if (motor != null) motor.UsarParalelismo = chkParalelo.IsChecked == true;
        }

        //roda a cada tick do timer: avanca alguns passos e redesenha
        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (motor == null)
            {
                timer.Stop();
                return;
            }

            for (int k = 0; k < passosPorTick; k++)
            {
                ExecutarPassoEGravar();
            }

            //o metodo devolve a diferenca entre o corpo mais quente e o mais frio
            double diferenca = AtualizarMatrizVisual();
            AtualizarStatus();

            //se a gravacao no banco falhou, para e avisa (senao os dados ficariam incompletos)
            if (gravador?.Erro != null)
            {
                timer.Stop();
                btnPlayPause.Content = "Play";
                MessageBox.Show("Erro ao gravar no banco:\n" + gravador.Erro.Message,
                                "Banco de dados", MessageBoxButton.OK, MessageBoxImage.Error);
                gravador = null;
                chkGravarBanco.IsChecked = false;
                return;
            }

            //se a conta explodiu (passo de tempo grande demais) para e avisa
            if (!double.IsFinite(diferenca))
            {
                timer.Stop();
                btnPlayPause.Content = "Play";
                MessageBox.Show("A simulacao divergiu. Diminua o passo de tempo e crie a malha de novo.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            //diferenca minuscula = equilibrio termico, nao precisa continuar
            if (diferenca < 0.01)
            {
                timer.Stop();
                btnPlayPause.Content = "Play";
            }
        }

        private void AtualizarStatus()
        {
            if (motor == null) return;
            string modo = motor.UsarParalelismo ? "paralelo" : "sequencial";
            string banco = gravador != null
                ? $"Simulação nº {gravador.NumSimulacao} — {gravador.InteracoesGravadas} interações gravadas"
                : "Sem gravação no banco";
            txtStatus.Text = $"Interação {motor.NumeroInteracao} ({motor.TempoSimulado:F1} s), modo {modo}\n{banco}";
        }

        //metodo para atualizar a interface (devolve max - min de temperatura da malha)
        private double AtualizarMatrizVisual()
        {
            if (motor == null || botoesGrid == null) return 0;

            int n = motor.Grid.Tamanho;

            //descobre o menor e o maior valor de temperatura da malha agora,
            //pra escala de cor se ajustar automaticamente (em Kelvin os
            //valores nao ficam entre 0 e 100, entao um range fixo nao funciona)
            double tempMin = double.MaxValue;
            double tempMax = double.MinValue;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double t = motor.Grid.Corpos[i, j].TemperaturaAtual;
                    if (t < tempMin) tempMin = t;
                    if (t > tempMax) tempMax = t;
                }
            }

            //evita divisao por zero se todo mundo estiver na mesma temperatura
            double intervalo = tempMax - tempMin;
            if (intervalo < 0.0001) intervalo = 1;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double temp = motor.Grid.Corpos[i, j].TemperaturaAtual; //esse linah esta puxando apenas a temperatura do corpo

                    //internamente continua em Kelvin, so converte pra Celsius na hora de mostrar (F1 mostra so 1 casa decimal)
                    double tempCelsius = temp - 273.15;
                    Material mat = motor.Grid.Corpos[i, j].Material;

                    //duas linhas: temperatura em cima, material embaixo
                    botoesGrid[i, j].Content = $"{tempCelsius:F1} °C\n{mat.Nome}";

                    //passando o mouse em cima mostra os detalhes completos
                    botoesGrid[i, j].ToolTip = $"Bloco [{i},{j}]\nMaterial: {mat.Nome}\nTemperatura: {tempCelsius:F2} °C";

                    //essas duas linhas sãooque alteram a cor dos blocos
                    //agora a porcentagem e calculada dentro do intervalo real
                    //de temperaturas da malha (min a max), nao um valor fixo
                    double percentual = (temp - tempMin) / intervalo;
                    byte vermelho = (byte)Math.Min(255, Math.Max(0, percentual * 255));
                    byte azul = (byte)(255 - vermelho);

                    //aplicam as cores
                    botoesGrid[i, j].Background = new SolidColorBrush(Color.FromRgb(vermelho, 0, azul));
                    botoesGrid[i, j].Foreground = Brushes.White;
                }
            }

            return tempMax - tempMin;
        }

        //metodo para alterar o material do botão apos criado
        private void BotaoGrid_Click(object sender, RoutedEventArgs e)
        {
            if (motor == null || cmbMaterial.SelectedItem == null) return;

            Button btnClicado = (Button)sender; //recebe o botão que foi clicado
            var pos = (Tuple<int, int>)btnClicado.Tag; //recebe a tupla
            int i = pos.Item1;
            int j = pos.Item2;

            //pega o material selecionado na caixa
            Material materialSelecionado = (Material)cmbMaterial.SelectedItem;

            //atualiza o material
            motor.Grid.Corpos[i, j].Material = materialSelecionado;

            AtualizarMatrizVisual();
        }

        //ao fechar a janela, espera o gravador terminar pra nao perder as ultimas interacoes
        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            timer.Stop();
            gravador?.FinalizarAsync().Wait(TimeSpan.FromSeconds(30));
        }
    }
}

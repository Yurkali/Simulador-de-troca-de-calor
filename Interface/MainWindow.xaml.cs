using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SimuladorTrocaCalor;

namespace Interface
{
    public partial class MainWindow : Window
    {
        private MotorSimulacao motor;
        private Button[,] botoesGrid;

        //timer que roda a simulacao sozinha (botao Play/Pause)
        private DispatcherTimer timer;
        private Button botaoPlay;
        private const int passosPorTick = 20; //quantos passos roda a cada tick antes de redesenhar

        public MainWindow()
        {
            InitializeComponent();

            //a cada 50 ms roda alguns passos e atualiza a tela
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += Timer_Tick;

            //definindo oque sera mostrado na combox
            cmbMaterial.DisplayMemberPath = "Nome";
            cmbMaterial.ItemsSource = Material.MateriaisDisponiveis;
            cmbMaterial.SelectedIndex = 1;
        }

        //Botão de "Criar/Resetar Grid"
        private void btnCriarGrid_Click(object sender, RoutedEventArgs e)
        {
            //validando os campos antes de usar, se algum estiver invalido avisa e cancela
            if (!int.TryParse(txtTamanho.Text, out int tamanho) || tamanho <= 0)
            {
                MessageBox.Show("Tamanho invalido. Digite um numero inteiro maior que zero.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!double.TryParse(txtLado.Text, out double lado) || lado <= 0)
            {
                MessageBox.Show("Lado invalido. Digite um numero maior que zero.",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //o usuario digita em Celsius, mas nao pode ser menor que o zero absoluto
            if (!double.TryParse(txtTempInicial.Text, out double tempInicialCelsius) || tempInicialCelsius < -273.15)
            {
                MessageBox.Show("Temperatura inicial invalida. Digite um valor em Celsius (>= -273,15).",
                                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!double.TryParse(txtPassoTempo.Text, out double passoTempo) || passoTempo <= 0)
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

            //se estava rodando, para antes de trocar o motor
            timer.Stop();
            if (botaoPlay != null) botaoPlay.Content = "Play";

            motor = new MotorSimulacao(gridLogico, passoTempo);

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

                    botoesGrid[i, j] = btn;//adiciona o botão na matriz
                    gridMatrizVisual.Children.Add(btn);//adiciona na matriz visual
                }
            }

            //atualiza toda a matriz visual com as informações predefinidas na ação
            AtualizarMatrizVisual();

            //ativar o botao para executar o passo
            btnAvancarPasso.IsEnabled = true;
            //ativar também o botão Play/Pause
            btnPlayPause.IsEnabled = true;
        }

        //Botão "Avançar"
        private void btnAvancarPasso_Click(object sender, RoutedEventArgs e)
        {
            if (motor == null) return;

            //roda um passo da simulacao (o valor do passo de tempo ja foi
            //validado e usado na criacao do motor, aqui so avancamos)
            motor.ExecutarPasso();

            //atualiza a interface com as novas temperaturas
            AtualizarMatrizVisual();
        }

        //Botão "Play/Pause": liga e desliga a simulacao automatica
        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (motor == null) return;

            botaoPlay = (Button)sender; //guarda o botao pra poder trocar o texto dele depois

            if (timer.IsEnabled)
            {
                timer.Stop();
                botaoPlay.Content = "Play";
            }
            else
            {
                timer.Start();
                botaoPlay.Content = "Pause";
            }
        }

        //roda a cada tick do timer: avanca alguns passos e redesenha
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (motor == null)
            {
                timer.Stop();
                return;
            }

            for (int k = 0; k < passosPorTick; k++)
            {
                motor.ExecutarPasso();
            }

            //o metodo devolve a diferenca entre o corpo mais quente e o mais frio
            double diferenca = AtualizarMatrizVisual();

            //se a conta explodiu (passo de tempo grande demais) para e avisa
            if (!double.IsFinite(diferenca))
            {
                timer.Stop();
                if (botaoPlay != null) botaoPlay.Content = "Play";
                MessageBox.Show("A simulacao divergiu. Diminua o passo de tempo e crie a malha de novo.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            //diferenca minuscula = equilibrio termico, nao precisa continuar
            if (diferenca < 0.01)
            {
                timer.Stop();
                if (botaoPlay != null) botaoPlay.Content = "Play";
            }
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
                    botoesGrid[i, j].Content = $"{tempCelsius:F1} °C";

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
    }
}
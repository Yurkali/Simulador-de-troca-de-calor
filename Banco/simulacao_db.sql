-- =====================================================================
-- Banco do Simulador de Troca de Calor (N2)
--
-- Adaptado do modelo da N1 (Simulacao -> Resultados -> Corpos).
-- As colunas de gravitacao (PosX, PosY, VelX, VelY) foram trocadas pelas
-- informacoes da troca de calor: posicao na malha, material e temperatura.
--
-- Com esses dados da para REMONTAR a simulacao em qualquer interacao:
--   Simulacao  -> tamanho da malha, lado dos corpos e passo de tempo
--   Corpos     -> para cada interacao, o material e a temperatura de cada corpo
--   Material   -> k, c e densidade de cada material usado
-- A interacao 0 guarda o estado inicial (antes do primeiro passo).
--
-- ATENCAO: este script APAGA as tabelas antigas da N1 (e os dados de exemplo).
-- Rodar no MySQL Workbench: File > Open SQL Script > executar (raio).
-- =====================================================================

CREATE DATABASE IF NOT EXISTS simulacao_db;
USE simulacao_db;

-- ordem inversa por causa das chaves estrangeiras
DROP TABLE IF EXISTS Corpos;
DROP TABLE IF EXISTS Resultados;
DROP TABLE IF EXISTS Simulacao;
DROP TABLE IF EXISTS Material;

-- Materiais disponiveis (espelha as classes de Materiais.cs)
CREATE TABLE Material (
    IdMaterial           INT PRIMARY KEY,          -- mesmo Id da classe no C#
    Nome                 VARCHAR(50) NOT NULL,
    Classe               VARCHAR(30) NOT NULL,     -- Metal, Ceramico, Polimero, Natural
    CondutividadeTermica DOUBLE NOT NULL,          -- k  W/(m.K)
    CalorEspecifico      DOUBLE NOT NULL,          -- c  J/(kg.K)
    Densidade            DOUBLE NOT NULL           -- kg/m3
);

-- Uma linha por simulacao executada
CREATE TABLE Simulacao (
    NumSimulacao      INT AUTO_INCREMENT PRIMARY KEY,
    DataSimulacao     DATETIME NOT NULL,
    QtdCorposInicial  INT NOT NULL,                -- N x N
    NumInteracoes     INT NOT NULL DEFAULT 0,      -- ultima interacao gravada
    TempoInteracoes   DOUBLE NOT NULL,             -- passo de tempo (s) de cada interacao
    TamanhoGrid       INT NOT NULL,                -- N da malha N x N
    LadoCorpo         DOUBLE NOT NULL,             -- lado do cubo (m), igual para todos
    ModoProcessamento VARCHAR(20) NOT NULL         -- Sequencial ou Paralelo
);

-- Uma linha por interacao (passo) de cada simulacao
CREATE TABLE Resultados (
    NumSimulacao  INT NOT NULL,
    NumInteracao  INT NOT NULL,
    TempoSimulado DOUBLE NOT NULL,                 -- segundos desde o inicio
    PRIMARY KEY (NumSimulacao, NumInteracao),
    CONSTRAINT fk_res_sim FOREIGN KEY (NumSimulacao)
        REFERENCES Simulacao (NumSimulacao)
        ON DELETE CASCADE
);

-- Estado de cada corpo em cada interacao
CREATE TABLE Corpos (
    NumSimulacao   INT NOT NULL,
    NumInteracao   INT NOT NULL,
    Linha          INT NOT NULL,
    Coluna         INT NOT NULL,
    NomeCorpo      VARCHAR(50) NOT NULL,           -- ex.: C[0,0]
    IdMaterial     INT NOT NULL,
    MassaCorpo     DOUBLE NOT NULL,                -- kg
    DensidadeCorpo DOUBLE NOT NULL,                -- kg/m3
    Temperatura    DOUBLE NOT NULL,                -- Kelvin (o motor trabalha em K)
    CalorSensivel  DOUBLE NOT NULL,                -- J, Q = m.c.T
    PRIMARY KEY (NumSimulacao, NumInteracao, Linha, Coluna),
    CONSTRAINT fk_corpo_res FOREIGN KEY (NumSimulacao, NumInteracao)
        REFERENCES Resultados (NumSimulacao, NumInteracao)
        ON DELETE CASCADE,
    CONSTRAINT fk_corpo_mat FOREIGN KEY (IdMaterial)
        REFERENCES Material (IdMaterial)
);

-- Carga inicial dos materiais (o programa tambem atualiza esta tabela ao conectar)
INSERT INTO Material VALUES
(1, 'Cobre',    'Metal',    385,   385,  8960),
(2, 'Aluminio', 'Metal',    205,   900,  2700),
(3, 'Ferro',    'Metal',    80,    450,  7870),
(4, 'Vidro',    'Ceramico', 1.05,  840,  2500),
(5, 'Madeira',  'Natural',  0.12,  1700, 600),
(6, 'Isopor',   'Polimero', 0.033, 1400, 30),
(7, 'Concreto', 'Ceramico', 1.7,   880,  2400),
(8, 'Aço',      'Metal',    50,    500,  7850);

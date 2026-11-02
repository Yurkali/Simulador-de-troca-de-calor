using System;
using System.Collections.Generic;
using System.Linq;

namespace SimuladorTrocaCalor
{
    // HIERARQUIA DE MATERIAIS
    //
    // Antes cada material era so um "new Material(nome, k, c, densidade)" dentro de uma lista.
    // Agora cada material e uma classe propria, agrupada pela sua classe de material:
    //
    //   Material (abstrata)            -> o que todo material tem: Id, Nome, k, c, densidade
    //   ├── Metal (abstrata)           -> Cobre, Aluminio, Ferro, Aco
    //   ├── Ceramico (abstrata)        -> Vidro, Concreto
    //   ├── Polimero (abstrata)        -> Isopor
    //   └── MaterialNatural (abstrata) -> Madeira
    //
    // O motor so conhece a classe base Material (polimorfismo): ele pede k, c e densidade
    // e cada subclasse responde com os seus valores. Para criar um material novo basta
    // criar uma classe herdando da categoria certa, sem mexer no motor.
    public abstract class Material
    {
        // Id usado como chave na tabela Material do banco (nao pode repetir entre subclasses)
        public abstract int Id { get; }
        public abstract string Nome { get; }

        // Nome da classe de material (Metal, Ceramico...). Cada categoria abstrata define o seu.
        public abstract string Classe { get; }

        public abstract double CondutividadeTermica { get; } // k - W/(m.K)
        public abstract double CalorEspecifico { get; }      // c - J/(kg.K)
        public abstract double Densidade { get; }            // kg/m3

        // usado pelo ComboBox e pelos prints do console
        public override string ToString() => $"{Nome} ({Classe})";

        // Uma instancia de cada material. Como os valores sao so leitura,
        // a mesma instancia pode ser compartilhada por todos os corpos (e por varias threads).
        public static readonly List<Material> MateriaisDisponiveis = new List<Material>
        {
            new Cobre(),
            new Aluminio(),
            new Ferro(),
            new Vidro(),
            new Madeira(),
            new Isopor(),
            new Concreto(),
            new Aco()
        };

        // usado para remontar a simulacao a partir do Id gravado no banco
        public static Material ObterPorId(int id)
        {
            Material? material = MateriaisDisponiveis.FirstOrDefault(m => m.Id == id);
            if (material == null) throw new ArgumentException($"Material com Id {id} nao existe.", nameof(id));
            return material;
        }
    }

    // ---------------- Categorias (classes de material) ----------------

    // metais: alta condutividade, conduzem bem o calor
    public abstract class Metal : Material
    {
        public override string Classe => "Metal";
    }

    // ceramicos: compostos inorganicos nao metalicos (vidro, concreto), condutividade baixa/media
    public abstract class Ceramico : Material
    {
        public override string Classe => "Ceramico";
    }

    // polimeros: cadeias organicas sinteticas, em geral isolantes termicos
    public abstract class Polimero : Material
    {
        public override string Classe => "Polimero";
    }

    // materiais naturais (de origem vegetal/animal), como a madeira
    public abstract class MaterialNatural : Material
    {
        public override string Classe => "Natural";
    }

    // ---------------- Materiais concretos ----------------

    public class Cobre : Metal
    {
        public override int Id => 1;
        public override string Nome => "Cobre";
        public override double CondutividadeTermica => 385;
        public override double CalorEspecifico => 385;
        public override double Densidade => 8960;
    }

    public class Aluminio : Metal
    {
        public override int Id => 2;
        public override string Nome => "Aluminio";
        public override double CondutividadeTermica => 205;
        public override double CalorEspecifico => 900;
        public override double Densidade => 2700;
    }

    public class Ferro : Metal
    {
        public override int Id => 3;
        public override string Nome => "Ferro";
        public override double CondutividadeTermica => 80;
        public override double CalorEspecifico => 450;
        public override double Densidade => 7870;
    }

    public class Vidro : Ceramico
    {
        public override int Id => 4;
        public override string Nome => "Vidro";
        public override double CondutividadeTermica => 1.05;
        public override double CalorEspecifico => 840;
        public override double Densidade => 2500;
    }

    public class Madeira : MaterialNatural
    {
        public override int Id => 5;
        public override string Nome => "Madeira";
        public override double CondutividadeTermica => 0.12;
        public override double CalorEspecifico => 1700;
        public override double Densidade => 600;
    }

    public class Isopor : Polimero
    {
        public override int Id => 6;
        public override string Nome => "Isopor";
        public override double CondutividadeTermica => 0.033;
        public override double CalorEspecifico => 1400;
        public override double Densidade => 30;
    }

    public class Concreto : Ceramico
    {
        public override int Id => 7;
        public override string Nome => "Concreto";
        public override double CondutividadeTermica => 1.7;
        public override double CalorEspecifico => 880;
        public override double Densidade => 2400;
    }

    public class Aco : Metal
    {
        public override int Id => 8;
        public override string Nome => "Aço";
        public override double CondutividadeTermica => 50;
        public override double CalorEspecifico => 500;
        public override double Densidade => 7850;
    }
}

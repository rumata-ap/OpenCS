using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels
{
   public class MaterialCharsVM : ViewModelBase
   {
      MaterialChars chars = new();
      string materialTag = "";

      public MaterialCharsVM()
      {
         
      }

      public MaterialCharsVM(MaterialChars chars, string materialTag = "")
      {
         this.chars = chars;
         this.materialTag = materialTag;
      }

      public MaterialChars Chars 
      { 
         get => chars; 
         set { chars = value; OnPropertyChanged(); } 
      }

      public string Header
      {
         get
         {
             var calcName = TypeCalc switch
             {
                CalcType.C => "C — 1st group, short-term",
                CalcType.CL => "CL — 1st group, long-term",
                CalcType.N => "N — 2nd group, short-term",
                CalcType.NL => "NL — 2nd group, long-term",
                _ => TypeCalc.ToString()
             };
             var tag = string.IsNullOrEmpty(materialTag) ? "?" : materialTag;
             return string.Format(Utilites.Loc.S("MaterialHeader"), tag, calcName);
         }
      }

      public string MaterialTag
      {
         get => materialTag;
         set { materialTag = value; OnPropertyChanged(); OnPropertyChanged(nameof(Header)); }
      }
            
      public string Tag 
      { 
         get => chars.Tag; 
         set { chars.Tag = value; OnPropertyChanged(); } 
      }

      /// <summary>
      /// Класс материала.
      /// </summary>
      public double Class
      {
         get => chars.Class;
         set
         {
            chars.Class = value;
            OnPropertyChanged(nameof(Class));
         }
      }

      /// <summary>
      /// Прочность на сжатие.
      /// </summary>
      public double Fc
      {
         get => chars.Fc;
         set
         {
            chars.Fc = value;
            OnPropertyChanged(nameof(Fc));
         }
      }

      /// <summary>
      /// Прочность на растяжение.
      /// </summary>
      public double Ft
      {
         get => chars.Ft;
         set
         {
            chars.Ft = value;
            OnPropertyChanged(nameof(Ft));
         }
      }

      /// <summary>
      /// Предел текучести.
      /// </summary>
      public double Ry
      {
         get => chars.Ry;
         set
         {
            chars.Ry = value;
            OnPropertyChanged(nameof(Ry));
         }
      }

      /// <summary>
      /// Предел прочности.
      /// </summary>
      public new double Ru
      {
         get => chars.Ru;
         set
         {
            chars.Ru = value;
            OnPropertyChanged(nameof(Ru));
         }
      }

      /// <summary>
      /// Модуль упругости.
      /// </summary>
      public new double E
      {
         get => chars.E;
         set
         {
            chars.E = value;
            OnPropertyChanged(nameof(E));
         }
      }

      /// <summary>
      /// Деформация при достижении fc.
      /// </summary>
      public double Ec0
      {
         get => chars.Ec0;
         set
         {
            chars.Ec0 = value;
            OnPropertyChanged(nameof(Ec0));
         }
      }

      /// <summary>
      /// Деформация при достижении 0.6 fc.
      /// </summary>
      public double Ec1
      {
         get => chars.Ec1;
         set
         {
            chars.Ec1 = value;
            OnPropertyChanged(nameof(Ec1));
         }
      }

      /// <summary>
      /// Максимальная деформация сжатия.
      /// </summary>
      public double Ec2
      {
         get => chars.Ec2;
         set
         {
            chars.Ec2 = value;
            OnPropertyChanged(nameof(Ec2));
         }
      }

      /// <summary>
      /// Деформация при достижении fc для двухлинейной диаграммы.
      /// </summary>
      public double Ec1Red
      {
         get => chars.Ec1Red;
         set
         {
            chars.Ec1Red = value;
            OnPropertyChanged(nameof(Ec1Red));
         }
      }

      /// <summary>
      /// Деформация при достижении ft для двухлинейной диаграммы.
      /// </summary>
      public double Et1Red
      {
         get => chars.Et1Red;
         set
         {
            chars.Et1Red = value;
            OnPropertyChanged(nameof(Et1Red));
         }
      }

      /// <summary>
      /// Деформация при достижении ft.
      /// </summary>
      public double Et0
      {
         get => chars.Et0;
         set
         {
            chars.Et0 = value;
            OnPropertyChanged(nameof(Et0));
         }
      }

      /// <summary>
      /// Деформация при достижении 0.6 ft.
      /// </summary>
      public double Et1
      {
         get => chars.Et1;
         set
         {
            chars.Et1 = value;
            OnPropertyChanged(nameof(Et1));
         }
      }

      /// <summary>
      /// Максимальная деформация при растяжении.
      /// </summary>
      public double Et2
      {
         get => chars.Et2;
         set
         {
            chars.Et2 = value;
            OnPropertyChanged(nameof(Et2));
         }
      }

      /// <summary>
      /// Тип материала.
      /// </summary>
      public MatType Type
      {
         get => chars.Type;
         set
         {
            chars.Type = value;
            OnPropertyChanged(nameof(Type));
         }
      }

      /// <summary>
      /// Тип характеристик.
      /// </summary>
      public CalcType TypeCalc
      {
         get => chars.TypeCalc;
         set
         {
            chars.TypeCalc = value;
            OnPropertyChanged(nameof(TypeCalc));
            OnPropertyChanged(nameof(Header));
         }
      }

      /// <summary>
      /// Влажность среды.
      /// </summary>
      public Dampness Dampness
      {
         get => chars.Dampness;
         set
         {
            chars.Dampness = value;
            OnPropertyChanged(nameof(Dampness));
         }
      }

   }
}

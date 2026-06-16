using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Строка набора усилий (аналог ForceDef в GreenSectionPy).
   /// Для типа "bar": N, Mx, My, Vx, Vy, T.
   /// </summary>
   public class LoadItem
   {
      public int    Id  { get; set; }
      public int    Num { get; set; }

      /// <summary>Метка строки (например "1", "sec 3", "Cm max N").</summary>
      public string Label { get; set; } = "";

      public double N  { get; set; }   // продольная сила, кН
      public double Mx { get; set; }   // изгибающий момент Mx, кН·м
      public double My { get; set; }   // изгибающий момент My, кН·м
      public double Vx { get; set; }   // поперечная сила Vx, кН
      public double Vy { get; set; }   // поперечная сила Vy, кН
      public double T  { get; set; }   // крутящий момент T, кН·м

      /// <summary>Преобразует строку в структуру Load для расчёта CrossSection.</summary>
      public Load ToLoad() => new Load { N = N, Mx = Mx, My = My };

      public override string ToString() =>
         $"{Num:D3}: N={N:G4}  Mx={Mx:G4}  My={My:G4}  Vx={Vx:G4}  Vy={Vy:G4}  T={T:G4}";
   }

   /// <summary>
   /// Строка н��бора усилий для пластин (аналог ShellForces в GreenSectionPy).
   /// Погонные усилия и моменты на единицу ширины.
   /// </summary>
   public class ShellLoadItem
   {
      public int    Id    { get; set; }
      public int    Num   { get; set; }
      public string Label { get; set; } = "";

      public double Nx  { get; set; }   // нормальное погонное усилие по x, кН/м
      public double Ny  { get; set; }   // нормальное погонное усилие по y, кН/м
      public double Nxy { get; set; }   // касательное погонное усилие, кН/м
      public double Mx  { get; set; }   // изгибающий погонный момент Mx, кН·м/м
      public double My  { get; set; }   // изгибающий погонный момент My, кН·м/м
      public double Mxy { get; set; }   // крутящий погонный момент, кН·м/м
      public double Qx  { get; set; }   // поперечная погонная сила Qx, кН/м
      public double Qy  { get; set; }   // поперечная погонная сила Qy, кН/м
   }

   /// <summary>
   /// Именованный набор усилий (аналог ForceSetDef в GreenSectionPy).
   /// Имя набора кодирует вид нагрузки: "G: ...", "L: ...", "Q: ...", "A: ...".
   /// </summary>
   public class ForceSet
   {
      public int     Id          { get; set; }
      public int     Num         { get; set; }
      public string  Tag         { get; set; } = "";
      public string? Description { get; set; }

      /// <summary>Тип набора: "bar" или "shell".</summary>
      public string Kind { get; set; } = "bar";

      public List<LoadItem>      Items      { get; set; } = [];
      public List<ShellLoadItem> ShellItems { get; set; } = [];

      public override string ToString() => $"{Num:D3}#ForceSet : {Tag}";
   }
}

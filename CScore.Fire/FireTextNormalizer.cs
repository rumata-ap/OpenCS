using System.Text;

namespace CScore.Fire;

/// <summary>
/// Нормализация обозначений классов арматуры: регистр, разделители и
/// латинские омоглифы кириллических букв.
/// </summary>
/// <remarks>
/// В реальных данных марка может быть записана и кириллицей («В500»),
/// и латиницей («B500»); без приведения к одному виду распознавание
/// зависело бы от того, как материал попал в базу.
/// </remarks>
public static class FireTextNormalizer
{
   const string Latin = "ABCEHKMOPTXY";
   const string Cyrillic = "АВСЕНКМОРТХУ";

   /// <summary>Верхний регистр, без пробелов и дефисов, латинские омоглифы → кириллица.</summary>
   public static string Normalize(string? value)
   {
      if (string.IsNullOrWhiteSpace(value)) return "";

      var sb = new StringBuilder(value.Length);
      foreach (char raw in value.ToUpperInvariant())
      {
         if (raw is ' ' or '-' or '_' or '\t') continue;
         int idx = Latin.IndexOf(raw);
         sb.Append(idx >= 0 ? Cyrillic[idx] : raw);
      }

      return sb.ToString();
   }
}

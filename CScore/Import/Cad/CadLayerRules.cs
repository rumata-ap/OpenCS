using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Import.Cad;

/// <summary>Роль слоя чертежа при импорте сечений.</summary>
public enum CadLayerRole
{
   /// <summary>Не импортируется.</summary>
   Ignore = 0,
   /// <summary>Бетон: замкнутые контуры и окружности — бетонные области.</summary>
   Concrete = 1,
   /// <summary>Сталь (жёсткая арматура, профиль): контуры — стальные области, в бетоне вырезают отверстие.</summary>
   Steel = 2,
   /// <summary>Продольная арматура: окружности — стержни.</summary>
   Rebar = 3,
   /// <summary>Отверстие (пустота) в области, внутри которой лежит контур.</summary>
   Hole = 4,
   /// <summary>Рамка: всё внутри рамки — одно сечение.</summary>
   Frame = 5,
   /// <summary>Подпись: текст — имя сечения.</summary>
   Label = 6,
   /// <summary>Хомуты и шпильки: линии (с шириной, тонкие, двойным контуром, с крючками) и окружности; тексты — подписи «Ø8 шаг 150».</summary>
   Stirrups = 7,
}

/// <summary>Предзаполнение ролей и классов материалов по именам слоёв.</summary>
public static partial class CadLayerRules
{
   /// <summary>Роль слоя по его имени и составу объектов.</summary>
   public static CadLayerRole Guess(CadLayerInfo layer)
   {
      // Ключевые слова — по исходному имени; классы — по латинизированному (В25 → B25).
      string n = layer.Name.ToLowerInvariant();
      string lat = Latin(layer.Name);
      if (Has(n, "рамк", "frame", "border")) return CadLayerRole.Frame;
      if (Has(n, "отв", "hole", "пуст", "void")) return CadLayerRole.Hole;
      if (Has(n, "хомут", "stirrup", "поп", "шпил")) return CadLayerRole.Stirrups;
      if (Has(n, "арм", "rebar", "reinf") || RebarClass().IsMatch(lat)) return CadLayerRole.Rebar;
      if (Has(n, "стал", "steel", "проф", "prof") || SteelClass().IsMatch(lat)) return CadLayerRole.Steel;
      if (Has(n, "бет", "conc", "жб") || ConcreteClass().IsMatch(lat)) return CadLayerRole.Concrete;
      if (Has(n, "подп", "надп", "text", "текст", "label", "назв", "марк")) return CadLayerRole.Label;
      if (layer.Texts > 0 && layer.Polygons == 0 && layer.Circles == 0 && layer.Lines == 0) return CadLayerRole.Label;
      return CadLayerRole.Ignore;
   }

   /// <summary>Класс материала в имени слоя («B25», «A500», «C245»), латиницей; null — не найден.</summary>
   public static string? GuessClass(string layerName, CadLayerRole role)
   {
      string n = Latin(layerName).ToUpperInvariant();
      var m = role switch
      {
         CadLayerRole.Concrete => ConcreteClass().Match(n),
         CadLayerRole.Rebar or CadLayerRole.Stirrups => RebarClass().Match(n),
         CadLayerRole.Steel => SteelClass().Match(n),
         _ => null,
      };
      return m is { Success: true } ? m.Value.Replace(',', '.') : null;
   }

   static bool Has(string n, params string[] keys) => keys.Any(n.Contains);

   /// <summary>Кириллические буквы, похожие на латинские (В25, А500, С245), — латиницей.</summary>
   public static string Latin(string s)
   {
      var sb = new StringBuilder(s.Length);
      foreach (char c in s)
         sb.Append(c switch
         {
            'А' => 'A', 'а' => 'a', 'В' => 'B', 'в' => 'b', 'С' => 'C', 'с' => 'c', 'Е' => 'E', 'е' => 'e',
            'К' => 'K', 'к' => 'k', 'М' => 'M', 'м' => 'm', 'Н' => 'H', 'н' => 'h', 'О' => 'O', 'о' => 'o',
            'Р' => 'P', 'р' => 'p', 'Т' => 'T', 'т' => 't', 'Х' => 'X', 'х' => 'x',
            _ => c,
         });
      return sb.ToString();
   }

   [GeneratedRegex(@"(?<![A-Za-z0-9])[Bb]\d{1,3}([.,]\d)?(?![0-9])")]
   private static partial Regex ConcreteClass();

   [GeneratedRegex(@"(?<![A-Za-z0-9])[Aa]\d{3}[Cc]?(?![0-9])")]
   private static partial Regex RebarClass();

   [GeneratedRegex(@"(?<![A-Za-z0-9])[Cc]\d{3}(?![0-9])")]
   private static partial Regex SteelClass();
}

using CScore;

namespace CScore.Fire;

/// <summary>Результат разрешения группы класса арматуры.</summary>
/// <param name="Group">Разрешённая группа таблицы 5.6.</param>
/// <param name="Source">Источник: explicit, tag, class или fallback.</param>
/// <param name="IsFallback">true, если группу определить не удалось и взято значение по умолчанию.</param>
/// <param name="RawValue">Исходное обозначение материала — для диагностики.</param>
public readonly record struct FireRebarClassResolution(
   FireRebarClass Group,
   string Source,
   bool IsFallback,
   string? RawValue);

/// <summary>
/// Определение группы класса арматуры по таблице 5.6 СП 468.
/// Порядок: явное поле материала → обозначение → числовой класс → фолбэк.
/// </summary>
public static class FireRebarClassResolver
{
   /// <summary>Разобрать строковое значение поля <see cref="Material.FireRebarClass"/>.</summary>
   public static bool TryParse(string? value, out FireRebarClass group)
   {
      group = FireRebarClass.A240A500;
      if (string.IsNullOrWhiteSpace(value)) return false;

      switch (value.Trim().ToLowerInvariant())
      {
         case "a240_a500": group = FireRebarClass.A240A500; return true;
         case "a600_a1000": group = FireRebarClass.A600A1000; return true;
         case "wire_rope": group = FireRebarClass.WireRope; return true;
         case "a500c_25g2s": group = FireRebarClass.A500C25G2S; return true;
         case "a600c_18g2sf": group = FireRebarClass.A600C18G2SF; return true;
         case "a500c_st3gps": group = FireRebarClass.A500CSt3Gps; return true;
         case "b500c_st3gps": group = FireRebarClass.B500CSt3Gps; return true;
         default: return false;
      }
   }

   /// <summary>Определить группу класса арматуры для материала.</summary>
   public static FireRebarClassResolution Resolve(Material material)
   {
      ArgumentNullException.ThrowIfNull(material);

      if (TryParse(material.FireRebarClass, out var explicitGroup))
         return new FireRebarClassResolution(explicitGroup, "explicit", false, material.Tag);

      string tag = FireTextNormalizer.Normalize(material.Tag);
      if (tag.Contains("ВР") || tag.Contains("К1400") || tag.Contains("К1500") || tag.Contains("В500"))
         return new FireRebarClassResolution(FireRebarClass.WireRope, "tag", false, material.Tag);

      double numericClass = material.MaterialChars.Count > 0 ? material.MaterialChars[0].Class : 0.0;
      switch (numericClass)
      {
         case 240 or 300 or 400 or 500:
            return new FireRebarClassResolution(FireRebarClass.A240A500, "class", false, material.Tag);
         case 600 or 800 or 1000:
            return new FireRebarClassResolution(FireRebarClass.A600A1000, "class", false, material.Tag);
      }

      return new FireRebarClassResolution(FireRebarClass.A240A500, "fallback", true, material.Tag);
   }
}

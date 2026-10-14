using CScore;
using CScore.Fire;
using CScore.Fire.Entities;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Предупреждение о шаге тепловой сетки вне рекомендуемого диапазона п. 6.2 СП 468
/// для результата огневой задачи. Тепловой расчёт актуален (хеш совпал), значит шаг
/// огневого сечения — тот, на котором он выполнен.
/// </summary>
public static class FireMeshWarning
{
   /// <summary>Текст предупреждения; null, если шаг в рекомендуемом диапазоне.</summary>
   public static string? Text(CrossSection section, FireSectionDef def)
   {
      var check = FireMeshStepValidator.Check(section, def.MeshStepM);
      return check.OutOfRecommendedRange
         ? string.Format(Loc.S("FireSection_MeshStepOutOfRange"), def.MeshStepM)
         : null;
   }
}

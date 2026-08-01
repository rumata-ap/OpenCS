using System.Collections.Generic;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;

namespace OpenCS.Services
{
   /// <summary>Данные страницы результатов FEM-анализа: сама страница + доступ к VM-модели,
   /// созданной GUI-фабрикой (VM содержит WPF/платформенные типы и не может жить в Core.UI).</summary>
   public sealed record FemAnalysisResultPage(IContentPage Page, IFemAnalysisResultHandle Handle);

   /// <summary>Платформо-независимый доступ к модели результата FEM-анализа.</summary>
   public interface IFemAnalysisResultHandle
   {
      /// <summary>Возвращает координаты, перемещения и реакции узла из результата.</summary>
      bool TryGetNodeResult(string tag, out (double X, double Y, double Z) point,
          out FemNodeDisplacement? displacement, out FemNodeReaction? reaction);

      /// <summary>Диагностические предупреждения нелинейного расчёта.</summary>
      IReadOnlyList<string> Diagnostics { get; }

      /// <summary>Признак наличия артефактов нелинейного расчёта (шаги на диске).</summary>
      bool HasArtifacts { get; }

      /// <summary>Каталог артефактов нелинейного расчёта.</summary>
      string ArtifactDirectory { get; }
   }
}

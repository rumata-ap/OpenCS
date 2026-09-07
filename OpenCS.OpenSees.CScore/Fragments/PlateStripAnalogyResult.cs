using System.Collections.Generic;
using CScore.Fem;

namespace OpenCS.OpenSees.CScore.Fragments
{
    /// <summary>Результат сквозной сверки полосы плиты: shell-прогон против beam-аналогии.</summary>
    public sealed class PlateStripAnalogyResult
    {
        /// <summary>Идентификатор полосы.</summary>
        public string StripId { get; set; } = "";

        /// <summary>Диагностики построения сетки.</summary>
        public IReadOnlyList<string> MeshDiagnostics { get; set; } = [];

        /// <summary>Диагностики модели, границ и прогона.</summary>
        public IReadOnlyList<string> BoundaryDiagnostics { get; set; } = [];

        /// <summary>Диагностики доменной части (сэмплер, решётка источников, Ньютон).</summary>
        public IReadOnlyList<FemValidationDiagnostic> DomainDiagnostics { get; set; } = [];

        /// <summary>Дошёл ли shell-прогон до полной нагрузки последней стадии.</summary>
        public bool IsConverged { get; set; }

        /// <summary>Сошлась ли нелинейная beam-аналогия.</summary>
        public bool BeamIsCalculable { get; set; }

        /// <summary>Доли пролёта, на которых снята эпюра.</summary>
        public IReadOnlyList<double> StationFractions { get; set; } = [];

        /// <summary>Эпюра [N, My, Mz] из shell-прогона.</summary>
        public IReadOnlyList<double[]> ShellResultants { get; set; } = [];

        /// <summary>Эпюра [N, My, Mz] нелинейной beam-аналогии.</summary>
        public IReadOnlyList<double[]> BeamResultants { get; set; } = [];

        /// <summary>Максимальный прогиб shell-модели (по узлам полосы), м.</summary>
        public double ShellMaxDeflectionM { get; set; }

        /// <summary>Максимальный прогиб beam-аналогии, м.</summary>
        public double BeamMaxDeflectionM { get; set; }

        /// <summary>Наибольшее относительное расхождение по My между shell и beam.</summary>
        public double MaxRelativeMomentMismatch { get; set; } = double.NaN;

        /// <summary>Относительное расхождение по прогибу.</summary>
        public double RelativeDeflectionMismatch { get; set; } = double.NaN;
    }
}

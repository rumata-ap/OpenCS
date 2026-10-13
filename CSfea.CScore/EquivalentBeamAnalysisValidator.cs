using CScore.Fem;
using CScore.PlateStrip;

namespace CSfea.CScoreBridge;

/// <summary>Предрасчётная проверка возможности использовать эквивалентную балку в 3D.</summary>
public static class EquivalentBeamAnalysisValidator
{
    public static IReadOnlyList<FemValidationDiagnostic> Validate(
        EquivalentSection section, bool torsionActive)
    {
        ArgumentNullException.ThrowIfNull(section);
        var diagnostics = new List<FemValidationDiagnostic>();
        if (!section.IsCalculable)
            diagnostics.Add(new(
                "equivalent_section_invalid_result",
                "Нельзя передать в стержневой МКЭ нерассчитанное эквивалентное сечение."));
        if (torsionActive && !(section.TorsionalStiffness > 0.0))
            diagnostics.Add(new(
                "equivalent_section_torsion_unsupported",
                "У эквивалентного сечения нет крутильной жёсткости: сечение рассчитано по " +
                "формулировке Эйлера–Бернулли, нужна формулировка Тимошенко (срез 8b)."));
        return diagnostics;
    }
}

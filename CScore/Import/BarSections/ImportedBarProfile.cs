namespace CScore.Import;

/// <summary>Материал стержня по данным схемы-источника.</summary>
public enum ImportedBarMaterial { Unknown, Concrete, Steel }

/// <summary>Форма сечения стержня по данным схемы-источника.</summary>
public enum ImportedBarShape { Rectangle }

/// <summary>
/// Профиль жёсткости стержня, нейтральный к программе-источнику (ЛИРА, SCAD …). Оси сечения OpenCS:
/// x — вдоль местной оси Y1 стержня, y — вдоль Z1.
/// </summary>
/// <param name="StiffnessNum">Номер жёсткости в схеме-источнике.</param>
/// <param name="Material">Материал стержня.</param>
/// <param name="Shape">Форма сечения.</param>
/// <param name="WidthM">Ширина B (вдоль Y1), м.</param>
/// <param name="HeightM">Высота H (вдоль Z1), м.</param>
/// <param name="SourceLabel">Исходная подпись жёсткости (имя в программе-источнике).</param>
public sealed record ImportedBarProfile(int StiffnessNum, ImportedBarMaterial Material,
    ImportedBarShape Shape, double WidthM, double HeightM, string SourceLabel);

/// <summary>
/// Профили стержней по жёсткостям схемы-источника: «Брус» ЛИРЫ и <c>S0</c> SCAD — прямоугольное ЖБ-сечение;
/// прочие формы пока не поддерживаются.
/// </summary>
public static class ImportedBarProfiles
{
    /// <summary>Профиль жёсткости КЭ либо причина, по которой его нет.</summary>
    /// <param name="stiffnesses">Жёсткости схемы по номеру.</param>
    /// <param name="stiffnessNum">Номер жёсткости КЭ; null — у КЭ его нет.</param>
    /// <param name="scad">Схема из SCAD (иначе — ЛИРА); влияет на разбор и подсказки в причинах.</param>
    public static (ImportedBarProfile? Profile, string? Reason) Resolve(
        IReadOnlyDictionary<int, LiraStiffnessRecord> stiffnesses, int? stiffnessNum, bool scad)
    {
        if (stiffnessNum is not int num)
            return (null, scad
                ? "у КЭ нет номера жёсткости"
                : "у КЭ нет номера жёсткости (меню схемы «Обновить жёсткости элементов из ЛИРЫ (API)»)");
        if (!stiffnesses.TryGetValue(num, out var stiffness))
            return (null, scad
                ? $"жёсткости {num} нет среди жёсткостей схемы"
                : $"жёсткости {num} нет среди жёсткостей схемы (меню схемы «Обновить жёсткости элементов из ЛИРЫ (API)»)");

        var rect = scad ? ScadStiffnessParams.BarRect(stiffness) : LiraStiffnessParams.BarRect(stiffness);
        if (rect == null)
            return (null, $"жёсткость {num} «{stiffness.Name}»: форма сечения не поддерживается "
                          + (scad ? "(только брус S0)" : "(только «Брус»)"));
        return (new ImportedBarProfile(num, ImportedBarMaterial.Concrete, ImportedBarShape.Rectangle,
            rect.WidthM, rect.HeightM, stiffness.Name), null);
    }
}

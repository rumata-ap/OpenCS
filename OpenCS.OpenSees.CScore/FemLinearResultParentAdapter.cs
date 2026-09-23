using System.Globalization;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Submodel;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore;

/// <summary>Итог адаптации сохранённого результата: либо результат, либо блокирующая диагностика.</summary>
public sealed record ParentResultAdapterOutcome(IParentLinearResult? Result, FemValidationDiagnostic? Error);

/// <summary>
/// Превращает сохранённый <see cref="CalcResult"/> линейного расчёта OpenSees (<c>TaskKind = "fem_linear"</c>,
/// <c>DataJson</c> — сериализованный <see cref="FemLinearResult"/>) в <see cref="IParentLinearResult"/>.
/// Целые теги OpenSees переводятся в канонические строковые.
/// </summary>
public static class FemLinearResultParentAdapter
{
    public const string LinearTaskKind = "fem_linear";

    public static ParentResultAdapterOutcome FromCalcResult(CalcResult calcResult)
    {
        ArgumentNullException.ThrowIfNull(calcResult);
        if (calcResult.TaskKind != LinearTaskKind)
            return Fail(BoundaryScenarioDiagnostics.ParentResultNotLinear,
                $"Результат #{calcResult.Id} вида '{calcResult.TaskKind}' не является линейным расчётом OpenSees.");
        if (calcResult.Status != "ok")
            return Fail(BoundaryScenarioDiagnostics.ParentResultInvalid,
                $"Линейный результат #{calcResult.Id} имеет статус '{calcResult.Status}'.");

        FemLinearResult? parsed;
        try { parsed = JsonSerializer.Deserialize<FemLinearResult>(calcResult.DataJson); }
        catch (JsonException ex)
        {
            return Fail(BoundaryScenarioDiagnostics.ParentResultInvalid,
                $"Линейный результат #{calcResult.Id} повреждён: {ex.Message}");
        }
        if (parsed is null || parsed.ElementForces.Count == 0)
            return Fail(BoundaryScenarioDiagnostics.ParentResultInvalid,
                $"Линейный результат #{calcResult.Id} не содержит концевых усилий стержней.");

        return new ParentResultAdapterOutcome(FromLinearResult(parsed), null);
    }

    public static IParentLinearResult FromLinearResult(FemLinearResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Build(result.Displacements, result.Reactions, result.ElementForces, isLinear: true);
    }

    /// <summary>Перевод OpenSees-результатов (перемещения, реакции, концевые усилия) в словарный
    /// <see cref="IParentLinearResult"/> с каноническими строковыми тегами — общий для линейного и
    /// нелинейного (по шагам) адаптеров.</summary>
    internal static DictionaryParentLinearResult Build(IEnumerable<FemNodeDisplacement> displacementRows,
        IEnumerable<FemNodeReaction> reactionRows, IEnumerable<FemElementEndForces> forceRows, bool isLinear)
    {
        var displacements = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        foreach (var d in displacementRows)
            displacements[Tag(d.NodeTag)] = new Dof6(d.Ux, d.Uy, d.Uz, d.Rx, d.Ry, d.Rz);
        var reactions = new Dictionary<string, Dof6>(StringComparer.Ordinal);
        foreach (var r in reactionRows)
            reactions[Tag(r.NodeTag)] = new Dof6(r.Rx, r.Ry, r.Rz, r.Mx, r.My, r.Mz);
        var endForces = new Dictionary<string, BeamEndForces>(StringComparer.Ordinal);
        foreach (var f in forceRows)
            endForces[Tag(f.ElemTag)] = new BeamEndForces(
                new Dof6(f.Ni, f.Qyi, f.Qzi, f.Mxi, f.Myi, f.Mzi),
                new Dof6(f.Nj, f.Qyj, f.Qzj, f.Mxj, f.Myj, f.Mzj));
        return new DictionaryParentLinearResult(isLinear, displacements, reactions, endForces);
    }

    static string Tag(int tag) => tag.ToString(CultureInfo.InvariantCulture);

    static ParentResultAdapterOutcome Fail(string code, string message) =>
        new(null, new FemValidationDiagnostic(code, message, true, []));
}

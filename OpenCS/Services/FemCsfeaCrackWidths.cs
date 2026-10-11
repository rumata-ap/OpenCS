using CScore;
using CScore.Fem;
using CSfea.Core;
using CSfea.CScoreBridge.Structural;

namespace OpenCS.Services;

/// <summary>
/// ЖБ-пластина расчётной модели для ширины раскрытия трещин: сечение, материалы, угол оси x выдачи усилий в осях
/// сечения (град) и оси сечения в глобальных осях (x — в плоскости КЭ, z — нормаль КЭ CSfea).
/// </summary>
public sealed record FemCsfeaCrackElement(int Id, PlateSection Section, Material Concrete, Material Rebar,
    double ForceAngleDeg, double[] AxisX, double[] AxisZ);

/// <summary>
/// Ширина раскрытия трещин пластины по граням сечения (низ — грань −z, верх — +z), мм: продолжительное a_crc,1 и
/// непродолжительное a_crc — наибольшие по направлениям; угол линии трещины наибольшего непродолжительного раскрытия
/// к оси x сечения, град (−90…90].
/// </summary>
public readonly record struct FemCsfeaCrackWidth(double LongBottom, double ShortBottom, double AngleBottom,
    double LongTop, double ShortTop, double AngleTop);

/// <summary>
/// Ширина раскрытия трещин пластин по усилиям шага секущего расчёта: упрощённая проверка по направлениям (Капра — Мори,
/// пп. 8.2.15–8.2.18 СП 63) — та же, что в нормативной проверке пластин по КЭ. Длительная часть усилий — доля от полных:
/// a_crc,1 = a(длит., φ1 = 1,4), a_crc = a(полн., φ1 = 1,0) − a(длит., φ1 = 1,0) + a_crc,1 (п. 8.2.7). Характеристики
/// материалов — вида расчёта N (п. 6.1.26), как в нормативной проверке.
/// </summary>
public static class FemCsfeaCrackWidths
{
    const string Kind = "shell_simpl_capri_sls";

    /// <summary>ЖБ-пластины адаптированной модели; пластины без сечения CScore или без материалов в проекте не входят.</summary>
    public static List<FemCsfeaCrackElement> Elements(FemRcModelResult adapted, IEnumerable<Material> materials)
    {
        ArgumentNullException.ThrowIfNull(adapted);
        var byId = new Dictionary<int, Material>();
        foreach (var m in materials) byId.TryAdd(m.Id, m);
        var nodes = adapted.Model.Nodes.ToDictionary(n => n.Id);
        var result = new List<FemCsfeaCrackElement>();
        foreach (var shell in adapted.Model.Shells)
        {
            if (shell.Section.Plate is not { } plate) continue;
            if (!byId.TryGetValue(plate.ConcreteMaterialId, out var concrete) || !byId.TryGetValue(plate.RebarMaterialId, out var rebar)) continue;
            if (concrete.GetChars(CalcType.N) == null || rebar.GetChars(CalcType.N) == null) continue;
            var coords = shell.NodeIds.Select(id => nodes[id]).Select(n => new[] { n.X, n.Y, n.Z }).ToArray();
            var frame = ShellGeometry.LocalFrame(coords);
            double[] z = [frame[2, 0], frame[2, 1], frame[2, 2]];
            // Ось x сечения — заданная, спроецированная на плоскость КЭ; нет — ось x КЭ CSfea.
            double[] x = [frame[0, 0], frame[0, 1], frame[0, 2]];
            if (shell.SectionAxisX is { } a)
            {
                double dot = a[0] * z[0] + a[1] * z[1] + a[2] * z[2];
                double[] p = [a[0] - dot * z[0], a[1] - dot * z[1], a[2] - dot * z[2]];
                double len = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                if (len > 1e-9) x = [p[0] / len, p[1] / len, p[2] / len];
            }
            result.Add(new FemCsfeaCrackElement(shell.Id, plate, concrete, rebar,
                adapted.ShellForceAngles.GetValueOrDefault(shell.Id), x, z));
        }
        return result;
    }

    /// <summary>
    /// Ширина раскрытия одной пластины по усилиям в осях выдачи (СИ: Nx, Ny, Nxy, Н/м; Mx, My, Mxy, Н·м/м);
    /// <paramref name="longShare"/> — доля длительной части усилий, 0…1.
    /// </summary>
    public static FemCsfeaCrackWidth Compute(FemCsfeaCrackElement e, ReadOnlySpan<double> forces, double longShare)
    {
        var item = new ShellLoadItem
        {
            Nx = forces[0] / 1e3, Ny = forces[1] / 1e3, Nxy = forces[2] / 1e3,
            Mx = forces[3] / 1e3, My = forces[4] / 1e3, Mxy = forces[5] / 1e3,
        };
        // Усилия в осях выдачи → оси сечения (армирования), как в проверке по КЭ.
        if (e.ForceAngleDeg != 0) item = ShellForceTransform.Rotate(item, e.ForceAngleDeg);

        List<ShellSimplDirectionResult> Dirs(double k, double phi1) => ShellSimplSolver.Solve(
            new ShellSimplSolver.SolveParams(k * item.Nx, k * item.Ny, k * item.Nxy, k * item.Mx, k * item.My, k * item.Mxy,
                Kind, Phi1: phi1), e.Section, e.Concrete, e.Rebar, CalcType.N).CapriDirs ?? [];

        var long14 = Dirs(longShare, 1.4);
        bool allLong = longShare >= 1.0;
        var full10 = allLong ? long14 : Dirs(1.0, 1.0);
        var long10 = allLong ? long14 : Dirs(longShare, 1.0);

        // [низ, верх]
        var aLong = new double[2];
        var aShort = new double[2];
        var angle = new double[2];
        for (int i = 0; i < full10.Count && i < long14.Count && i < long10.Count; i++)
        {
            if (full10[i].Strip.NoRebar) continue;
            double l = long14[i].Strip.Acrc_mm;
            double s = allLong ? l : full10[i].Strip.Acrc_mm - long10[i].Strip.Acrc_mm + l;
            if (!double.IsFinite(l) || !double.IsFinite(s)) continue;
            int face = full10[i].Top ? 1 : 0;
            aLong[face] = Math.Max(aLong[face], l);
            if (s > aShort[face])
            {
                aShort[face] = s;
                double line = full10[i].Alpha_deg + 90;
                while (line > 90) line -= 180;
                while (line <= -90) line += 180;
                angle[face] = line;
            }
        }
        return new FemCsfeaCrackWidth(aLong[0], aShort[0], angle[0], aLong[1], aShort[1], angle[1]);
    }

    /// <summary>Ширина раскрытия всех ЖБ-пластин шага (параллельно); пластины, которых нет в полях, пропускаются.</summary>
    public static Dictionary<int, FemCsfeaCrackWidth> Compute(IReadOnlyList<FemCsfeaCrackElement> elements,
        RcSecantStepFields fields, double longShare, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(fields);
        var index = new Dictionary<int, int>(fields.ShellIds.Length);
        for (int i = 0; i < fields.ShellIds.Length; i++) index.TryAdd(fields.ShellIds[i], i);
        var values = new FemCsfeaCrackWidth?[elements.Count];
        Parallel.For(0, elements.Count, new ParallelOptions { CancellationToken = ct }, k =>
        {
            if (!index.TryGetValue(elements[k].Id, out int i)) return;
            var f = fields.ShellForces.AsSpan(i * RcSecantStepFields.ShellForceComponents, RcSecantStepFields.ShellForceComponents);
            try { values[k] = Compute(elements[k], f, longShare); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException) { }
        });
        var result = new Dictionary<int, FemCsfeaCrackWidth>(elements.Count);
        for (int k = 0; k < elements.Count; k++)
            if (values[k] is { } v) result[elements[k].Id] = v;
        return result;
    }
}

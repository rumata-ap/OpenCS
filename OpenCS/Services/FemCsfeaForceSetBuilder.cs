using System.Globalization;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.Structural;
using OpenCS.ViewModels;

namespace OpenCS.Services;

/// <summary>
/// «Шаг → набор усилий»: поля шага расчёта CSfea (<see cref="RcSecantStepFields"/>) — в наборы усилий схемы для
/// проверок по КЭ. Пластины — строка на КЭ (центр, усилия закона сечения в осях выдачи — как у импортированных наборов;
/// кН/м, кН·м/м), стержни — две строки на КЭ (концы i и j, сечения 1 и 2; кН, кН·м — как наборы стержней OpenSees).
/// Номер КЭ строки — тег КЭ сетки.
/// </summary>
public static class FemCsfeaForceSetBuilder
{
    /// <summary>Наборы шага: пластин (если есть пластины) и стержней (если есть стержни); номера — после существующих.</summary>
    public static List<ForceSet> Build(FemSchema schema, string analysisTag, FemCsfeaStepSummary step, string stageTag,
        RcSecantStepFields fields, IReadOnlyCollection<ForceSet> existing)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(fields);
        int num = existing.Count == 0 ? 1 : existing.Max(f => f.Num) + 1;
        string baseTag = string.Format(CultureInfo.CurrentCulture, "CSfea {0}, шаг {1} ({2}, λ = {3:0.###})", analysisTag, step.N,
            stageTag, step.LoadFactor);
        string description = string.Format(CultureInfo.CurrentCulture,
            "Расчёт CSfea «{0}» схемы «{1}»: шаг {2}, стадия «{3}», λ = {4:0.####}.", analysisTag, schema.Tag, step.N, stageTag,
            step.LoadFactor);
        var sets = new List<ForceSet>();

        if (fields.ShellIds.Length > 0)
        {
            var rows = new List<ShellLoadItem>(fields.ShellIds.Length);
            for (int e = 0; e < fields.ShellIds.Length; e++)
            {
                int o = e * RcSecantStepFields.ShellForceComponents;
                var f = fields.ShellForces;
                if (!Enumerable.Range(o, RcSecantStepFields.ShellForceComponents).All(i => double.IsFinite(f[i]))) continue;
                rows.Add(new ShellLoadItem
                {
                    Num = rows.Count + 1, Label = fields.ShellIds[e].ToString(CultureInfo.InvariantCulture),
                    Nx = f[o] / 1e3, Ny = f[o + 1] / 1e3, Nxy = f[o + 2] / 1e3,
                    Mx = f[o + 3] / 1e3, My = f[o + 4] / 1e3, Mxy = f[o + 5] / 1e3,
                    Qx = f[o + 6] / 1e3, Qy = f[o + 7] / 1e3,
                    SourceElementNum = fields.ShellIds[e],
                });
            }
            sets.Add(new ForceSet
            {
                Num = num++, Tag = baseTag + " — пластины", Description = description, Kind = "shell", SourceType = "fea",
                SourceSchemaId = schema.Id, ShellItems = rows,
            });
        }

        if (fields.BeamIds.Length > 0)
        {
            var rows = new List<LoadItem>(2 * fields.BeamIds.Length);
            for (int e = 0; e < fields.BeamIds.Length; e++)
            {
                int o = e * RcSecantStepFields.BeamForceComponents;
                var f = fields.BeamForces;
                if (!Enumerable.Range(o, RcSecantStepFields.BeamForceComponents).All(i => double.IsFinite(f[i]))) continue;
                int tag = fields.BeamIds[e];
                var pair = FemForceEndpointConverter.Convert(new FemElementEndForces(tag, f[o], f[o + 1], f[o + 2], f[o + 3],
                    f[o + 4], f[o + 5], f[o + 6], f[o + 7], f[o + 8], f[o + 9], f[o + 10], f[o + 11]),
                    FemForceEndpointSignPolicy.OpenSeesDefault);
                foreach (var (values, section) in new[] { (pair.Start, 1), (pair.End, 2) })
                {
                    var item = FemForceEndpointConverter.ToLoadItem(values, rows.Count + 1,
                        string.Create(CultureInfo.InvariantCulture, $"{tag}-{section}"));
                    item.SourceElementNum = tag;
                    item.SourceSectionNum = section;
                    rows.Add(item);
                }
            }
            sets.Add(new ForceSet
            {
                Num = num, Tag = baseTag + " — стержни", Description = description, Kind = "bar", SourceType = "fea",
                SourceSchemaId = schema.Id, Items = rows,
            });
        }
        return sets;
    }
}

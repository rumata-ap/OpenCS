using System.Globalization;
using CScore;
using CScore.Fem;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.Structural;

namespace OpenCS.Services;

/// <summary>
/// «Шаг → набор усилий»: поля шага расчёта CSfea (<see cref="RcSecantStepFields"/>) — в наборы усилий схемы для
/// проверок по КЭ. Пластины — строка на КЭ (центр, усилия закона сечения в осях выдачи — как у импортированных наборов;
/// кН/м, кН·м/м), стержни — две строки на КЭ (концы i и j, сечения 1 и 2; кН, кН·м — как наборы стержней OpenSees;
/// при коротационном расчёте — в осях деформированного КЭ, т. е. в осях сечения, что и нужно проверкам).
/// Номер КЭ строки — тег КЭ сетки. С охватом (<see cref="FemCsfeaForceSetScope"/>) — только строки КЭ охвата, у охвата
/// одного КонЭ наборы привязаны к нему.
/// </summary>
public static class FemCsfeaForceSetBuilder
{
    /// <summary>Наборы шага: пластин (если есть пластины) и стержней (если есть стержни); номера — после существующих.</summary>
    /// <param name="secondOrder">Результат второго порядка (<see cref="FemCsfeaResultSummary.SecondOrder"/>) — <see cref="ForceSet.SecondOrder"/>.</param>
    public static List<ForceSet> Build(FemSchema schema, string analysisTag, FemCsfeaStepSummary step, string stageTag,
        RcSecantStepFields fields, IReadOnlyCollection<ForceSet> existing, FemCsfeaForceSetScope? scope = null, bool secondOrder = false)
    {
        ArgumentNullException.ThrowIfNull(step);
        string baseTag = string.Format(CultureInfo.CurrentCulture, "CSfea {0}, шаг {1} ({2}, λ = {3:0.###})", analysisTag, step.N,
            stageTag, step.LoadFactor);
        string description = string.Format(CultureInfo.CurrentCulture,
            "Расчёт CSfea «{0}» схемы «{1}»: шаг {2}, стадия «{3}», λ = {4:0.####}.", analysisTag, schema.Tag, step.N, stageTag,
            step.LoadFactor);
        return BuildNamed(schema, baseTag, description, fields, existing, scope, secondOrder);
    }

    /// <summary>
    /// Наборы полей с заданным именем (к нему добавляется « — пластины»/« — стержни» и пометка охвата) и описанием;
    /// вид без строк в охвате — без набора.
    /// </summary>
    public static List<ForceSet> BuildNamed(FemSchema schema, string baseTag, string description, RcSecantStepFields fields,
        IReadOnlyCollection<ForceSet> existing, FemCsfeaForceSetScope? scope = null, bool secondOrder = false)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fields);
        if (scope != null) description += " " + scope.Description;
        string suffix = scope?.TagSuffix ?? "";
        int num = existing.Count == 0 ? 1 : existing.Max(f => f.Num) + 1;
        var sets = new List<ForceSet>();

        if (fields.ShellIds.Length > 0)
        {
            var rows = new List<ShellLoadItem>(fields.ShellIds.Length);
            AppendShellRows(rows, fields, scope?.Elements, null);
            if (scope == null || rows.Count > 0)
                sets.Add(new ForceSet
                {
                    Num = num++, Tag = baseTag + " — пластины" + suffix, Description = description, Kind = "shell",
                    SourceType = "fea", SourceSchemaId = schema.Id, ShellItems = rows, SecondOrder = secondOrder,
                });
        }

        if (fields.BeamIds.Length > 0)
        {
            var rows = new List<LoadItem>(2 * fields.BeamIds.Length);
            AppendBeamRows(rows, fields, scope?.Elements, null);
            if (scope == null || rows.Count > 0)
                sets.Add(new ForceSet
                {
                    Num = num, Tag = baseTag + " — стержни" + suffix, Description = description, Kind = "bar",
                    SourceType = "fea", SourceSchemaId = schema.Id, Items = rows, SecondOrder = secondOrder,
                });
        }
        if (scope != null) foreach (var set in sets) scope.Bind(set);
        return sets;
    }

    /// <summary>Метка строки: номер КЭ (стержень — «тег-сечение»), с вариантом пакета — «… · вариант».</summary>
    static string RowLabel(string element, string? variant) => variant == null ? element : $"{element} · {variant}";

    /// <summary>
    /// Строки пластин полей (кН/м, кН·м/м) в конец <paramref name="rows"/> с продолжением нумерации: КЭ вне
    /// <paramref name="elements"/> (null — все) и с нечисловыми усилиями пропускаются.
    /// </summary>
    internal static void AppendShellRows(List<ShellLoadItem> rows, RcSecantStepFields fields, IReadOnlySet<int>? elements,
        string? variant)
    {
        var f = fields.ShellForces;
        for (int e = 0; e < fields.ShellIds.Length; e++)
        {
            int o = e * RcSecantStepFields.ShellForceComponents;
            if (elements != null && !elements.Contains(fields.ShellIds[e])) continue;
            if (!Enumerable.Range(o, RcSecantStepFields.ShellForceComponents).All(i => double.IsFinite(f[i]))) continue;
            rows.Add(new ShellLoadItem
            {
                Num = rows.Count + 1, Label = RowLabel(fields.ShellIds[e].ToString(CultureInfo.InvariantCulture), variant),
                Nx = f[o] / 1e3, Ny = f[o + 1] / 1e3, Nxy = f[o + 2] / 1e3,
                Mx = f[o + 3] / 1e3, My = f[o + 4] / 1e3, Mxy = f[o + 5] / 1e3,
                Qx = f[o + 6] / 1e3, Qy = f[o + 7] / 1e3,
                SourceElementNum = fields.ShellIds[e],
            });
        }
    }

    /// <summary>Строки стержней полей (две на КЭ, кН, кН·м) — как <see cref="AppendShellRows"/>.</summary>
    internal static void AppendBeamRows(List<LoadItem> rows, RcSecantStepFields fields, IReadOnlySet<int>? elements,
        string? variant)
    {
        var f = fields.BeamForces;
        for (int e = 0; e < fields.BeamIds.Length; e++)
        {
            int o = e * RcSecantStepFields.BeamForceComponents;
            if (elements != null && !elements.Contains(fields.BeamIds[e])) continue;
            if (!Enumerable.Range(o, RcSecantStepFields.BeamForceComponents).All(i => double.IsFinite(f[i]))) continue;
            int tag = fields.BeamIds[e];
            var pair = FemForceEndpointConverter.Convert(new FemElementEndForces(tag, f[o], f[o + 1], f[o + 2], f[o + 3],
                f[o + 4], f[o + 5], f[o + 6], f[o + 7], f[o + 8], f[o + 9], f[o + 10], f[o + 11]),
                FemForceEndpointSignPolicy.OpenSeesDefault);
            foreach (var (values, section) in new[] { (pair.Start, 1), (pair.End, 2) })
            {
                var item = FemForceEndpointConverter.ToLoadItem(values, rows.Count + 1,
                    RowLabel(string.Create(CultureInfo.InvariantCulture, $"{tag}-{section}"), variant));
                item.SourceElementNum = tag;
                item.SourceSectionNum = section;
                rows.Add(item);
            }
        }
    }
}

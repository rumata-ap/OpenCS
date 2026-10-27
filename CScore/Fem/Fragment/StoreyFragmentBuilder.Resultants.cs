using System.Globalization;
using CScore.Fem.Loads;
using CScore.Planar;

namespace CScore.Fem.Fragment;

public static partial class StoreyFragmentBuilder
{
    /// <summary>Допустимое относительное расхождение равнодействующих родителя и фрагмента по загружению (спека разд. 4).</summary>
    public const double ResultantTolerance = 0.005;

    /// <summary>
    /// Проверка равнодействующих по загружениям: родитель (точные интегралы по полосе, накоплены при переносе) против
    /// фрагмента — нагрузки КонЭ-стержней и узлов аналитически, нагрузки на КЭ и поля — согласованными узловыми силами
    /// на пробной сетке (<see cref="FragmentTrialMesh"/>). Собственный вес не сравнивается.
    /// </summary>
    static void CheckResultants(LoadCtx l)
    {
        var draft = l.C.Draft;
        if (draft.LoadCases.Count == 0) return;
        var frag = new Dictionary<(int LoadCase, string Category), Resultant>();
        Resultant Sum(int lc, string category)
        {
            if (!frag.TryGetValue((lc, category), out var s)) frag[(lc, category)] = s = new Resultant();
            return s;
        }

        var parts = l.Bars.ToDictionary(p => p.Tag, StringComparer.Ordinal);
        foreach (var (tag, load) in draft.MemberLoads)
        {
            var p = parts[tag];
            bool local = load.CoordinateSystem == "local";
            PlanarVector3 G(PlanarVector3 v) => local ? p.ToGlobal(v) : v;
            var q0 = G(new PlanarVector3(load.QxStart, load.QyStart, load.QzStart));
            var q1 = G(new PlanarVector3(load.QxEnd, load.QyEnd, load.QzEnd));
            var s = Sum(load.LoadCaseId, l.CategoryOf[load]);
            if (load.DistributionType == "point")
                s.Add(p.A + p.Axis * load.StartOffsetM - l.Origin, q0, G(new PlanarVector3(load.Mx, load.My, load.Mz)));
            else
                s.AddLine(p.A + p.Axis * load.StartOffsetM - l.Origin, p.A + p.Axis * (p.Length - load.EndOffsetM) - l.Origin,
                    q0, load.DistributionType == "uniform" ? q0 : q1);
        }
        var nodes = draft.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        foreach (var (tag, load) in draft.NodeLoads)
            Sum(load.LoadCaseId, l.CategoryOf[load]).Add(P(nodes[tag]) - l.Origin,
                new PlanarVector3(load.Fx, load.Fy, load.Fz), new PlanarVector3(load.Mx, load.My, load.Mz));

        var elementLoads = draft.ElementLoads.Where(x => x.LoadKind != FemElementLoadKinds.SelfWeight).ToList();
        if (elementLoads.Count > 0 || draft.FieldLoads.Count > 0)
        {
            var (meshNodes, meshElements) = FragmentTrialMesh.Build(draft);
            var mesh = new FemLoadMeshContext(meshNodes, meshElements);
            var diagnostics = new List<FemValidationDiagnostic>();
            void Nodalize(FemElementLoad load, string category)
            {
                var forces = new Dictionary<string, double[]>(StringComparer.Ordinal);
                FemElementLoadNodalizer.Accumulate(load, mesh, 1, forces, diagnostics);
                var s = Sum(load.LoadCaseId, category);
                foreach (var (tag, f) in forces)
                {
                    var n = mesh.NodesByTag[tag];
                    s.Add(new PlanarVector3(n.X, n.Y, n.Z) - l.Origin, new PlanarVector3(f[0], f[1], f[2]), new PlanarVector3(f[3], f[4], f[5]));
                }
            }
            foreach (var load in elementLoads) Nodalize(load, l.CategoryOf[load]);
            var mapper = new ParentMeshMapper(draft.RefElements, l.C.Tol);
            foreach (var field in draft.FieldLoads)
                foreach (var load in FragmentFieldLoadResolver.Resolve([field], mapper, draft.RefElements, mesh, diagnostics, l.C.Tol))
                    Nodalize(load, l.CategoryOf[field]);
        }

        foreach (var lc in draft.LoadCases)
        {
            var categories = l.ParentSums.Keys.Concat(frag.Keys).Where(k => k.LoadCase == lc.Id).Select(k => k.Category)
                .Distinct().Order(StringComparer.Ordinal).ToList();
            if (categories.Count == 0) continue;
            Resultant Total(Dictionary<(int LoadCase, string Category), Resultant> sums)
            {
                var t = new Resultant();
                foreach (var category in categories)
                    if (sums.TryGetValue((lc.Id, category), out var s)) t.Add(s);
                return t;
            }
            var parent = Total(l.ParentSums);
            var fragment = Total(frag);
            double scaleF = parent.F.Length, scaleM = parent.M.Length + parent.F.Length * l.Reach;
            double dF = (fragment.F - parent.F).Length, dM = (fragment.M - parent.M).Length;
            bool mismatch = scaleF > 0
                ? dF > ResultantTolerance * scaleF || dM > ResultantTolerance * scaleM
                : dF > 1e-6 || dM > 1e-6;
            if (!mismatch) continue;
            var byCategory = categories.Select(category =>
            {
                var p = l.ParentSums.GetValueOrDefault((lc.Id, category)) ?? new Resultant();
                var f = frag.GetValueOrDefault((lc.Id, category)) ?? new Resultant();
                return $"{category}: ΔF {Pct((f.F - p.F).Length, scaleF)}, ΔM {Pct((f.M - p.M).Length, scaleM)}";
            });
            l.C.Warn("fragment_load_resultant_mismatch",
                $"Загружение «{lc.Tag}»: равнодействующая фрагмента отличается от родителя — ΔF {Pct(dF, scaleF)}, " +
                $"ΔM {Pct(dM, scaleM)} ({string.Join("; ", byCategory)}). Возможно, сетка фрагмента грубее опорной.",
                lc.Tag);
        }
    }

    static string Pct(double d, double scale) => scale > 0
        ? (d / scale * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %"
        : d.ToString("0.###", CultureInfo.InvariantCulture) + " Н";
}

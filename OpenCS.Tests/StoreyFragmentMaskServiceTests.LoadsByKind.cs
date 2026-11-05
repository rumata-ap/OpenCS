using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Нагрузки полосы родителя по виду (спека 2026-11-05 «нагрузки КЭ на гранях фрагмента по виду»): КЭ, пересекающий
/// грань призмы или плоскость полосы, даёт в Ω_f ту часть нагрузки, которую переносит построитель, — не долю КЭ.
/// </summary>
public sealed partial class StoreyFragmentMaskServiceTests
{
    static FemRcModelInput WithLoads(FemRcModelInput i, IEnumerable<FemElementLoad> element, IEnumerable<FemMemberLoad> member) => new()
    {
        MeshNodes = i.MeshNodes, MeshElements = i.MeshElements, Nodes = i.Nodes, Members = i.Members, Groups = i.Groups,
        Supports = i.Supports, Springs = i.Springs, RigidBodies = i.RigidBodies, LoadCases = i.LoadCases, NodeLoads = i.NodeLoads,
        MemberLoads = [.. i.MemberLoads, .. member], ElementLoads = [.. i.ElementLoads, .. element], MeshNodeLoads = i.MeshNodeLoads,
        KinematicLoads = i.KinematicLoads, MeshNodeKinematicLoads = i.MeshNodeKinematicLoads, Properties = i.Properties,
        PlateSection = i.PlateSection, ShellModelSection = i.ShellModelSection, BeamSection = i.BeamSection, Calc = i.Calc,
        RegionAxisX = i.RegionAxisX, BeamShear = i.BeamShear, Stages = [],
    };

    static FemElementLoad ElementLoad(int lc, string elem, string kind, string axis, double[] values)
    {
        var l = new FemElementLoad { LoadCaseId = lc, LoadKind = kind, Axis = axis, CoordinateSystem = "global" };
        l.SetTargetTags([elem]);
        l.SetValues(values);
        return l;
    }

    /// <summary>
    /// Призма (−2,5; −2,5) с полуразмерами 0,9 (x, y ∈ [−3,4; −1,6]), полоса z 1,5…4,5. КЭ плиты 1 — x, y ∈ [−3; −1,5]
    /// поперёк граней x = y = −1,6; КЭ колонны C1 511 — z 1…2 поперёк z₁ = 1,5. Разность нагрузок полосы с добавкой и без.
    /// </summary>
    [Theory]
    [InlineData("shell-point-out", 0.0, 0.0, 0.0)]
    [InlineData("shell-point-in", 0.0, 0.0, -10_000.0)]
    [InlineData("bar-point-out", 0.0, 0.0, 0.0)]
    [InlineData("bar-point-in", 1000.0, 0.0, 0.0)]
    [InlineData("bar-trapezoid", 1250.0, 0.0, 0.0)]
    [InlineData("member-offset", 2700.0, 0.0, 0.0)]
    [InlineData("member-point-on-node", 500.0, 0.0, 0.0)]
    public void BandLoads_ByKind_MatchTransferRule(string what, double fx, double fy, double fz)
    {
        var prism = FragmentPrism.Create(-2.5, -2.5, 1, 0, 0.9, 0.9);
        var p = Create(Band with { Prism = prism });
        using var db = p.Db;
        int lc = db.GetFemLoadCases(p.Schema.Id)[0].Id;
        var input = FemCsfeaInputBuilder.Build(db, p.Schema.Id, new FemCsfeaSetup { Stages = [], GrossBars = true });
        var adapted = StoreyFragmentMaskService.AdaptAllCases(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));

        var nodes = input.MeshNodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var shell = FemElementGeometry.Of(input.MeshElements.Single(e => e.ElemTag == "1"), nodes)!;
        double[] Local(double x, double y)
        {
            var (ex, ey, _) = shell.ShellFrame();
            var d = new PlanarVector3(x, y, 3) - shell.Points[shell.Contour[0]];
            return [-10_000, d.Dot(ex), d.Dot(ey)];
        }
        var c1 = input.Members.Single(m => m.ElemTag == "C1");
        var element = new List<FemElementLoad>();
        var member = new List<FemMemberLoad>();
        switch (what)
        {
            case "shell-point-out": element.Add(ElementLoad(lc, "1", "point", "z", Local(-1.55, -2.8))); break;
            case "shell-point-in": element.Add(ElementLoad(lc, "1", "point", "z", Local(-2.8, -2.8))); break;
            case "bar-point-out": element.Add(ElementLoad(lc, "511", "point", "x", [1000, 0.2])); break;
            case "bar-point-in": element.Add(ElementLoad(lc, "511", "point", "x", [1000, 0.8])); break;
            case "bar-trapezoid": element.Add(ElementLoad(lc, "511", "trapezoid", "x", [1000, 0, 3000, 1])); break;
            case "member-offset":
                member.Add(new FemMemberLoad
                {
                    LoadCaseId = lc, MemberId = c1.Id, CoordinateSystem = "global", DistributionType = "uniform",
                    StartOffsetM = 1.8, QxStart = 1000, QxEnd = 1000,
                });
                break;
            case "member-point-on-node":
                member.Add(new FemMemberLoad
                {
                    LoadCaseId = lc, MemberId = c1.Id, CoordinateSystem = "global", DistributionType = "point",
                    StartOffsetM = 2.0, QxStart = 500, QxEnd = 500,
                });
                break;
        }
        var loaded = WithLoads(input, element, member);
        var bounds = new FragmentBounds(Band.Z1, Band.Z2, prism, 1e-3);

        var before = FragmentBandLoads.Compute(input, FragmentParentLoadParts.Compute(input, adapted), bounds, PlanarVector3.Zero)[lc].F;
        var adaptedLoaded = StoreyFragmentMaskService.AdaptAllCases(loaded);
        Assert.False(adaptedLoaded.HasErrors, string.Join("; ", adaptedLoaded.Report));
        var after = FragmentBandLoads.Compute(loaded, FragmentParentLoadParts.Compute(loaded, adaptedLoaded), bounds, PlanarVector3.Zero)[lc].F;

        var d = after - before;
        double tol = 1e-6 * Math.Max(1, Math.Abs(fx) + Math.Abs(fy) + Math.Abs(fz));
        Assert.True(Math.Abs(d.X - fx) <= tol && Math.Abs(d.Y - fy) <= tol && Math.Abs(d.Z - fz) <= tol,
            $"{what}: ΔF = ({d.X:0.###}; {d.Y:0.###}; {d.Z:0.###}), ожидалось ({fx}; {fy}; {fz})");
    }
}

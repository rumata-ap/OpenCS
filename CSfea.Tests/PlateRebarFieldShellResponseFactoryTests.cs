using CScore;
using CSfea.CScoreBridge;
using CScore.PlateRebar;

namespace CSfea.Tests;

/// <summary>Тесты моста PlateRebarField → per-element IShellSectionResponse (CSfea).</summary>
public static class PlateRebarFieldShellResponseFactoryTests
{
    public static void RunAll()
    {
        TestHarness.Section("PlateRebarField → CSfea: дедуп per-element откликов и разный Mx");
        RunDedupAndDifferentiatedResponse();
    }

    static void RunDedupAndDifferentiatedResponse()
    {
        double e = 30_000, h = 0.2; // МПа, м
        var concrete = LinearDiagram(e);
        var section = new PlateSection { H = h, NLayers = 8, TensionConcrete = true };

        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon =
            [
                new() { U = 5, V = 0 }, new() { U = 7, V = 0 },
                new() { U = 7, V = 2 }, new() { U = 5, V = 2 },
            ],
            Layout = new PlateRebarLayer { Asx = 0.002, Zsx = 0.09 },
        };
        var field = new PlateRebarField([], [zone]);
        var materials = new PlateSectionMaterials
        {
            ConcreteDiagram = concrete, RebarDiagram = concrete, ConcreteE_MPa = e,
        };

        var centroids = new (int ElementId, double U, double V)[] { (1, 0.5, 0.5), (2, 6, 1) };
        var set = PlateRebarFieldShellResponseFactory.MapMesh(section, field, materials, centroids);

        TestHarness.Check("2 элемента с разным армированием → 2 уникальных отклика",
            set.UniqueResponses.Count == 2, $"count={set.UniqueResponses.Count}");

        var responses = set.ToPerElementArray([1, 2]);
        double kx = 1e-3;
        var forcesBaseline = responses[0].Forces([0, 0, 0], [kx, 0, 0], [0, 0]);
        var forcesReinforced = responses[1].Forces([0, 0, 0], [kx, 0, 0], [0, 0]);

        TestHarness.Check("Mx с доп. арматурой больше Mx без неё (тот же κx)",
            forcesReinforced.M[0] > forcesBaseline.M[0],
            $"MxBaseline={forcesBaseline.M[0]:e3}, MxReinforced={forcesReinforced.M[0]:e3}");
    }

    static Diagramm LinearDiagram(double e_MPa)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = e_MPa, Ry = 600, Ru = 600, Ft = 600, Fc = -600,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = e_MPa, Type = MatType.ReSteelF, Tag = "lin" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m.GetDiagramms(DiagrammType.L2)![CalcType.C];
    }
}

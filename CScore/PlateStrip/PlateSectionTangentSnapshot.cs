using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CScore.Fem;

namespace CScore.PlateStrip;

/// <summary>Результат построения замороженного источника PlateSection.</summary>
public sealed record PlateSectionTangentSnapshotBuildResult(
    bool IsCalculable,
    PlateSectionTangentSnapshot? Source,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>
/// Линейный источник, полученный заморозкой касательной PlateSection в нулевом состоянии.
/// Ненулевые усилия в нулевой точке не превращаются в постоянный force offset.
/// </summary>
public sealed class PlateSectionTangentSnapshot : IPlateSectionResponse
{
    readonly ConstantLinearPlateSectionResponse _linear;

    PlateSectionTangentSnapshot(ConstantLinearPlateSectionResponse linear, string fingerprint,
                                double probeStep, double relativeTolerance, double absoluteTolerance)
    {
        _linear = linear;
        Fingerprint = fingerprint;
        ProbeStep = probeStep;
        RelativeTolerance = relativeTolerance;
        AbsoluteTolerance = absoluteTolerance;
        ReferenceState = ShellStrainState.Zero;
        Snapshot = linear.Tangent(ReferenceState);
    }

    /// <summary>Вид источника.</summary>
    public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.PlateSectionTangentSnapshot;

    /// <summary>Состояние, в котором снята касательная.</summary>
    public ShellStrainState ReferenceState { get; }

    /// <summary>Снятая касательная PlateSection.</summary>
    public PlateShellTangentResult Snapshot { get; }

    public double ProbeStep { get; }
    public double RelativeTolerance { get; }
    public double AbsoluteTolerance { get; }
    public string Fingerprint { get; }

    public PlateResultants Forces(ShellStrainState state) => _linear.Forces(state);
    public PlateShellTangentResult Tangent(ShellStrainState state) => _linear.Tangent(state);

    /// <summary>
    /// Снять и проверить касательную PlateSection в нулевом состоянии.
    /// </summary>
    public static PlateSectionTangentSnapshotBuildResult Create(
        PlateSection section,
        Diagramm concreteDiagram,
        Diagramm rebarDiagram,
        IReadOnlyList<Diagramm?>? layerDiagrams = null,
        double probeStep = 1e-7,
        double relativeTolerance = 1e-4,
        double absoluteTolerance = 1e-8)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(concreteDiagram);
        ArgumentNullException.ThrowIfNull(rebarDiagram);

        var diagnostics = new List<FemValidationDiagnostic>();
        if (!section.TensionConcrete)
        {
            diagnostics.Add(new(
                "equivalent_section_source_requires_tension",
                "Для линейного снимка PlateSection требуется TensionConcrete=true."));
            return new(false, null, diagnostics);
        }
        if (!(probeStep > 0.0) || !double.IsFinite(probeStep) ||
            !(relativeTolerance >= 0.0) || !double.IsFinite(relativeTolerance) ||
            !(absoluteTolerance >= 0.0) || !double.IsFinite(absoluteTolerance))
        {
            diagnostics.Add(new(
                "equivalent_section_invalid_probe",
                "Шаг и допуски проверки линейного источника должны быть конечными и допустимыми."));
            return new(false, null, diagnostics);
        }

        var zero = ShellStrainState.Zero;
        var baseTangent = section.ComputeTangent(
            zero, concreteDiagram, rebarDiagram, layerDiagrams,
            tensionOverride: true);

        var baseResultants = new PlateResultants(
            baseTangent.Nx, baseTangent.Ny, baseTangent.Nxy,
            baseTangent.Mx, baseTangent.My, baseTangent.Mxy);
        if (HasNonZero(baseResultants, absoluteTolerance))
            diagnostics.Add(new(
                "equivalent_section_state_dependent_source",
                "PlateSection имеет ненулевые результирующие усилия в нулевом состоянии; force offset не применяется.",
                false));

        var probeStates = new[]
        {
            new ShellStrainState(probeStep, 0, 0, 0, 0, 0),
            new ShellStrainState(-probeStep, 0, 0, 0, 0, 0),
            new ShellStrainState(0, probeStep, 0, 0, 0, 0),
            new ShellStrainState(0, -probeStep, 0, 0, 0, 0),
            new ShellStrainState(0, 0, probeStep, 0, 0, 0),
            new ShellStrainState(0, 0, -probeStep, 0, 0, 0),
            new ShellStrainState(0, 0, 0, probeStep, 0, 0),
            new ShellStrainState(0, 0, 0, -probeStep, 0, 0),
            new ShellStrainState(0, 0, 0, 0, probeStep, 0),
            new ShellStrainState(0, 0, 0, 0, -probeStep, 0),
            new ShellStrainState(0, 0, 0, 0, 0, probeStep),
            new ShellStrainState(0, 0, 0, 0, 0, -probeStep)
        };
        for (int i = 0; i < probeStates.Length; i++)
        {
            var probe = section.ComputeTangent(
                probeStates[i], concreteDiagram, rebarDiagram, layerDiagrams,
                tensionOverride: true);
            if (!CloseTangent(baseTangent, probe, relativeTolerance, absoluteTolerance))
            {
                diagnostics.Add(new(
                    "equivalent_section_state_dependent_source",
                    "Касательная PlateSection меняется на малой пробе около нулевого состояния.",
                    false));
                break;
            }
        }

        string fingerprint = BuildFingerprint(section, concreteDiagram, rebarDiagram, layerDiagrams, baseTangent);
        var linear = new ConstantLinearPlateSectionResponse(
            baseTangent.A, baseTangent.B, baseTangent.D, baseTangent.As, fingerprint);
        var source = new PlateSectionTangentSnapshot(
            linear, fingerprint, probeStep, relativeTolerance, absoluteTolerance);
        return new(true, source, diagnostics);
    }

    static bool HasNonZero(PlateResultants resultants, double tolerance) =>
        Math.Abs(resultants.Nx) > tolerance || Math.Abs(resultants.Ny) > tolerance ||
        Math.Abs(resultants.Nxy) > tolerance || Math.Abs(resultants.Mx) > tolerance ||
        Math.Abs(resultants.My) > tolerance || Math.Abs(resultants.Mxy) > tolerance;

    static bool CloseTangent(PlateShellTangentResult a, PlateShellTangentResult b,
                             double relativeTolerance, double absoluteTolerance)
    {
        return CloseBlock(a.A, b.A, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.B, b.B, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.D, b.D, relativeTolerance, absoluteTolerance) &&
               CloseBlock(a.As, b.As, relativeTolerance, absoluteTolerance);
    }

    static bool CloseBlock(double[,] a, double[,] b, double relativeTolerance, double absoluteTolerance)
    {
        for (int i = 0; i < a.GetLength(0); i++)
        for (int j = 0; j < a.GetLength(1); j++)
        {
            double scale = Math.Max(Math.Abs(a[i, j]), Math.Abs(b[i, j]));
            if (Math.Abs(a[i, j] - b[i, j]) > absoluteTolerance + relativeTolerance * scale)
                return false;
        }
        return true;
    }

    static string BuildFingerprint(PlateSection section, Diagramm concrete, Diagramm rebar,
                                   IReadOnlyList<Diagramm?>? layerDiagrams, PlateShellTangentResult tangent)
    {
        var parts = new List<string>
        {
            $"section:{section.Id}:{section.Tag}:{section.H.ToString("G17", CultureInfo.InvariantCulture)}",
            $"layers:{section.NLayers}:{section.TensionConcrete}:{section.SofteningModel}:{section.PlateModel}",
            $"materials:{section.ConcreteMaterialId}:{section.RebarMaterialId}",
            $"diagrams:{concrete.Id}:{rebar.Id}"
        };
        if (layerDiagrams != null)
            parts.Add("layer-diagrams:" + string.Join(",", layerDiagrams.Select(d => d?.Id ?? 0)));
        AddMatrix(parts, tangent.A, "A");
        AddMatrix(parts, tangent.B, "B");
        AddMatrix(parts, tangent.D, "D");
        AddMatrix(parts, tangent.As, "As");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
    }

    static void AddMatrix(List<string> parts, double[,] matrix, string name)
    {
        for (int i = 0; i < matrix.GetLength(0); i++)
        for (int j = 0; j < matrix.GetLength(1); j++)
            parts.Add($"{name}:{i}:{j}:{matrix[i, j].ToString("G17", CultureInfo.InvariantCulture)}");
    }
}

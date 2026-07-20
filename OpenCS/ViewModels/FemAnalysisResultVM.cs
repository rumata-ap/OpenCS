using System.Text.Json;
using System.Windows.Media.Media3D;
using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Structural;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>ViewModel результатной вкладки линейного OpenSees-расчёта FEM-схемы.</summary>
public class FemAnalysisResultVM : ViewModelBase
{
    /// <summary>Статус расчёта (ok/not_converged/error/cancelled).</summary>
    public string Status { get; }
    /// <summary>Каталог артефактов запуска, если есть.</summary>
    public string? ArtifactDirectory { get; }
    /// <summary>Есть ли каталог артефактов.</summary>
    public bool HasArtifacts => !string.IsNullOrEmpty(ArtifactDirectory);
    /// <summary>Диагностические сообщения и ошибки валидации.</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    public IReadOnlyList<FemNodeDisplacement> Displacements { get; }
    public IReadOnlyList<FemNodeReaction> Reactions { get; }
    public IReadOnlyList<FemElementEndForces> ElementForces { get; }

    readonly Dictionary<int, Point3D> _originalByTag = [];
    readonly Dictionary<int, Vector3D> _dispByTag = [];
    readonly List<(int I, int J)> _elementPairs = [];

    /// <summary>Геометрия mesh-элемента с локальным базисом для эпюр.</summary>
    readonly record struct ElementGeom(int Tag, Point3D Pi, Point3D Pj, Vector3D Ey, Vector3D Ez);
    readonly List<ElementGeom> _elementGeoms = [];
    readonly Dictionary<int, FemElementEndForces> _forcesByElem = [];

    /// <summary>Точки линий исходной (недеформированной) схемы, парами.</summary>
    public Point3DCollection OriginalLines { get; } = [];
    /// <summary>Точки линий деформированной схемы, парами (масштаб DeformScale).</summary>
    public Point3DCollection DeformedLines { get; private set; } = [];

    /// <summary>Есть ли стержневая геометрия для 3D-отображения.</summary>
    public bool HasGeometry => _elementPairs.Count > 0;

    double _deformScale = 1.0;
    /// <summary>Масштаб визуализации перемещений.</summary>
    public double DeformScale
    {
        get => _deformScale;
        set { if (value <= 0 || value == _deformScale) return; _deformScale = value; RebuildDeformed(); OnPropertyChanged(); }
    }

    /// <summary>Компоненты усилий для выбора эпюры.</summary>
    public IReadOnlyList<FemForceComponent> ForceComponents { get; } =
        [FemForceComponent.N, FemForceComponent.Qy, FemForceComponent.Qz,
         FemForceComponent.Mx, FemForceComponent.My, FemForceComponent.Mz];

    FemForceComponent _selectedForceComponent = FemForceComponent.My;
    /// <summary>Выбранная компонента усилия для 3D-эпюры.</summary>
    public FemForceComponent SelectedForceComponent
    {
        get => _selectedForceComponent;
        set { if (value == _selectedForceComponent) return; _selectedForceComponent = value; RebuildForceDiagram(); OnPropertyChanged(); }
    }

    double _forceScale = 1.0;
    /// <summary>Масштаб 3D-эпюры усилий.</summary>
    public double ForceScale
    {
        get => _forceScale;
        set { if (value <= 0 || value == _forceScale) return; _forceScale = value; RebuildForceDiagram(); OnPropertyChanged(); }
    }

    /// <summary>Геометрия ленты выбранной эпюры.</summary>
    public MeshGeometry3D? ForceDiagramMesh { get; private set; }

    public FemAnalysisResultVM(CalcResult result, DatabaseService db, FemSchema schema)
    {
        Status = result.Status;

        var parsed = ParseResult(result.DataJson);
        Displacements = parsed?.Displacements ?? [];
        Reactions = parsed?.Reactions ?? [];
        ElementForces = parsed?.ElementForces ?? [];
        ArtifactDirectory = parsed?.ArtifactDirectory;
        Diagnostics = CollectDiagnostics(result.DataJson, parsed);

        // Геометрия из mesh-снимка схемы
        foreach (var n in db.GetFemMeshNodes(schema.Id))
            if (int.TryParse(n.NodeTag, out int tag))
                _originalByTag[tag] = new Point3D(n.X, n.Y, n.Z);

        foreach (var e in db.GetFemMeshElements(schema.Id))
        {
            var ends = JsonSerializer.Deserialize<int[]>(e.NodeIdsJson) ?? [];
            if (ends.Length != 2 || !_originalByTag.ContainsKey(ends[0]) || !_originalByTag.ContainsKey(ends[1]))
                continue;
            _elementPairs.Add((ends[0], ends[1]));
            if (int.TryParse(e.ElemTag, out int etag))
            {
                var pi = _originalByTag[ends[0]];
                var pj = _originalByTag[ends[1]];
                var (ey, ez) = LocalFrame(pi, pj);
                _elementGeoms.Add(new ElementGeom(etag, pi, pj, ey, ez));
            }
        }

        foreach (var f in ElementForces)
            _forcesByElem[f.ElemTag] = f;

        foreach (var d in Displacements)
            _dispByTag[d.NodeTag] = new Vector3D(d.Ux, d.Uy, d.Uz);

        foreach (var (i, j) in _elementPairs)
        {
            OriginalLines.Add(_originalByTag[i]);
            OriginalLines.Add(_originalByTag[j]);
        }

        _deformScale = SuggestScale();
        RebuildDeformed();

        _forceScale = SuggestForceScale();
        RebuildForceDiagram();
    }

    static (Vector3D Ey, Vector3D Ez) LocalFrame(Point3D pi, Point3D pj)
    {
        var ex = pj - pi;
        if (ex.Length < 1e-9) return (new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));
        ex.Normalize();
        var vecxz = System.Math.Abs(ex.Z) > 1 - 1e-6 ? new Vector3D(1, 0, 0) : new Vector3D(0, 0, 1);
        var ey = Vector3D.CrossProduct(vecxz, ex);
        if (ey.Length < 1e-9) ey = Vector3D.CrossProduct(new Vector3D(0, 1, 0), ex);
        ey.Normalize();
        var ez = Vector3D.CrossProduct(ex, ey);
        ez.Normalize();
        return (ey, ez);
    }

    /// <summary>Значения выбранной компоненты на концах i и j (внутреннее усилие, непрерывное по узлу).</summary>
    (double Vi, double Vj) ComponentValues(FemElementEndForces f) => _selectedForceComponent switch
    {
        FemForceComponent.N  => (f.Ni,  -f.Nj),
        FemForceComponent.Qy => (f.Qyi, -f.Qyj),
        FemForceComponent.Qz => (f.Qzi, -f.Qzj),
        FemForceComponent.Mx => (f.Mxi, -f.Mxj),
        FemForceComponent.My => (f.Myi, -f.Myj),
        FemForceComponent.Mz => (f.Mzi, -f.Mzj),
        _ => (0, 0)
    };

    Vector3D OffsetAxis(ElementGeom g) => _selectedForceComponent switch
    {
        FemForceComponent.Qy or FemForceComponent.Mz => g.Ey,
        _ => g.Ez
    };

    void RebuildForceDiagram()
    {
        var segs = new List<(Point3D, Point3D, Vector3D, Vector3D)>();
        foreach (var g in _elementGeoms)
        {
            if (!_forcesByElem.TryGetValue(g.Tag, out var f)) continue;
            var (vi, vj) = ComponentValues(f);
            var axis = OffsetAxis(g);
            segs.Add((g.Pi, g.Pj, axis * (vi * _forceScale), axis * (vj * _forceScale)));
        }
        ForceDiagramMesh = FemForceDiagramFactory.BuildRibbons(segs);
        OnPropertyChanged(nameof(ForceDiagramMesh));
    }

    /// <summary>Масштаб эпюры: макс. |усилие| по всем компонентам → ≈10% габарита.</summary>
    double SuggestForceScale()
    {
        if (_elementGeoms.Count == 0 || _forcesByElem.Count == 0) return 1.0;
        double maxVal = 0;
        foreach (var f in _forcesByElem.Values)
            foreach (var v in new[] { f.Ni, f.Qyi, f.Qzi, f.Mxi, f.Myi, f.Mzi, f.Nj, f.Qyj, f.Qzj, f.Mxj, f.Myj, f.Mzj })
                maxVal = System.Math.Max(maxVal, System.Math.Abs(v));
        if (maxVal <= 1e-12) return 1.0;

        var xs = _originalByTag.Values;
        double dx = xs.Max(p => p.X) - xs.Min(p => p.X);
        double dy = xs.Max(p => p.Y) - xs.Min(p => p.Y);
        double dz = xs.Max(p => p.Z) - xs.Min(p => p.Z);
        double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (diag <= 1e-9) return 1.0;
        return 0.1 * diag / maxVal;
    }

    void RebuildDeformed()
    {
        var pts = new Point3DCollection();
        foreach (var (i, j) in _elementPairs)
        {
            pts.Add(Deformed(i));
            pts.Add(Deformed(j));
        }
        DeformedLines = pts;
        OnPropertyChanged(nameof(DeformedLines));
    }

    Point3D Deformed(int tag)
    {
        var p = _originalByTag[tag];
        if (_dispByTag.TryGetValue(tag, out var d))
            return p + d * _deformScale;
        return p;
    }

    /// <summary>Подбирает масштаб так, чтобы макс. перемещение было ≈5% габарита схемы.</summary>
    double SuggestScale()
    {
        if (_originalByTag.Count == 0 || _dispByTag.Count == 0) return 1.0;
        double maxDisp = _dispByTag.Values.Select(v => v.Length).DefaultIfEmpty(0).Max();
        if (maxDisp <= 1e-12) return 1.0;

        var xs = _originalByTag.Values;
        double dx = xs.Max(p => p.X) - xs.Min(p => p.X);
        double dy = xs.Max(p => p.Y) - xs.Min(p => p.Y);
        double dz = xs.Max(p => p.Z) - xs.Min(p => p.Z);
        double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (diag <= 1e-9) return 1.0;
        return 0.05 * diag / maxDisp;
    }

    static FemLinearResult? ParseResult(string dataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (!doc.RootElement.TryGetProperty("Displacements", out _)) return null;
            return JsonSerializer.Deserialize<FemLinearResult>(dataJson);
        }
        catch (JsonException) { return null; }
    }

    static IReadOnlyList<string> CollectDiagnostics(string dataJson, FemLinearResult? parsed)
    {
        var list = new List<string>();
        if (parsed?.Diagnostics is { Count: > 0 }) list.AddRange(parsed.Diagnostics);
        try
        {
            using var doc = JsonDocument.Parse(dataJson);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                foreach (var e in errors.EnumerateArray())
                    if (e.GetString() is { } s) list.Add(s);
            else if (doc.RootElement.TryGetProperty("error", out var err) && err.GetString() is { } m && list.Count == 0)
                list.Add(m);
        }
        catch (JsonException) { }
        return list;
    }
}

/// <summary>Компонента усилия стержня для отображения эпюры.</summary>
public enum FemForceComponent { N, Qy, Qz, Mx, My, Mz }

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CScore;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Generation;
using OpenCS.Gmsh.Mapping;
using OpenCS.Gmsh.Parsing;
using OpenCS.Gmsh.Runtime;

namespace OpenCS.Gmsh;

/// <summary>Строит сетку одного PlanarRegion внешним Gmsh в формате MSH 2.2 ASCII.</summary>
public sealed class GmshPlanarMesher : IPlanarMesher
{
    public const string GeneratorVersion = "gmsh-planar-v4-fem-driven-constraints";
    const int OuterPhysicalGroup = 1001;
    const int HolePhysicalGroupBase = 1002;
    const int SurfacePhysicalGroup = 2001;
    readonly GmshPlanarMesherOptions _options;

    public GmshPlanarMesher(GmshPlanarMesherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = PlanarRegionValidator.Validate(request.Region).ToList();
        diagnostics.AddRange(request.EffectivePreflightDiagnostics);
        if (request.ConstraintObjects is not null)
            diagnostics.AddRange(PlanarConstraintValidator.Validate(request.Region, request.EffectiveConstraintObjects));
        try { request.Settings.Validate(); }
        catch (InvalidOperationException ex) { diagnostics.Add(new("planar_mesh_settings_invalid", ex.Message)); }
        if (_options.Timeout <= TimeSpan.Zero)
            diagnostics.Add(new("planar_mesh_timeout_invalid", "Тайм-аут запуска Gmsh должен быть положительным."));

        var directory = Path.Combine(_options.ArtifactRoot, $"gmsh-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var provenance = new PlanarMeshProvenance("", GeneratorVersion) { ArtifactDirectory = directory };
        GmshExecutableInfo? executable = null;

        try
        {
            if (diagnostics.Any(d => d.IsError)) return Failed(request, diagnostics, provenance);

            executable = new GmshExecutableResolver(_options.BundledExecutablePath).Resolve(_options.ExecutablePath);
            provenance = provenance with { ExecutablePath = executable.Path, GmshVersion = await GmshProcessRunner.ReadVersionAsync(executable.Path, _options.Timeout, cancellationToken) };

            var geoPath = Path.Combine(directory, "model.geo");
            var mshPath = Path.Combine(directory, "model.msh");
            await File.WriteAllTextAsync(geoPath, GmshPlanarGeoBuilder.Build(request.Region, request.Settings, request.EffectiveConstraintObjects), Encoding.UTF8, cancellationToken);
            var result = await GmshProcessRunner.RunAsync(
                executable.Path,
                directory,
                [geoPath, "-2", "-format", "msh41", "-order", "1", "-o", mshPath],
                _options.Timeout,
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), result.Output, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), result.Error, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(mshPath))
            {
                var error = string.IsNullOrWhiteSpace(result.Error) ? "" : $" {result.Error.Trim()}";
                diagnostics.Add(new("gmsh_process_failed", $"Gmsh завершился с кодом {result.ExitCode}.{error}"));
                return Failed(request, diagnostics, provenance);
            }

            var msh = GmshMsh41Reader.Read(await File.ReadAllTextAsync(mshPath, cancellationToken));
            var parsed = ParseMsh41(msh, request.Region.Frame, request.Region, request.EffectiveConstraintObjects);
            diagnostics.AddRange(parsed.Diagnostics);
            var snapshot = new PlanarMeshSnapshot
            {
                RegionId = request.Region.Id,
                InputFingerprint = PlanarMeshFingerprint.Compute(request.Region, request.Settings, provenance, request.ConstraintSourceFingerprint),
                IsCalculable = false,
                Settings = request.Settings,
                Provenance = provenance,
                Diagnostics = diagnostics,
                Nodes = parsed.Nodes,
                Elements = parsed.Elements,
                BoundaryMappings = parsed.BoundaryMappings,
                EntityProvenance = parsed.EntityProvenance,
                ConstraintMappings = parsed.ConstraintMappings,
                MeshFormatVersion = "msh41"
            };
            diagnostics.AddRange(PlanarMeshSnapshotValidator.Validate(snapshot));
            return new PlanarMeshSnapshot
            {
                RegionId = snapshot.RegionId,
                InputFingerprint = snapshot.InputFingerprint,
                IsCalculable = !diagnostics.Any(d => d.IsError),
                Settings = snapshot.Settings,
                Provenance = snapshot.Provenance,
                Diagnostics = diagnostics,
                Nodes = snapshot.Nodes,
                Elements = snapshot.Elements,
                BoundaryMappings = snapshot.BoundaryMappings,
                MeshFormatVersion = snapshot.MeshFormatVersion,
                EntityProvenance = snapshot.EntityProvenance,
                ConstraintMappings = snapshot.ConstraintMappings
            };
        }
        catch (GmshProcessTimeoutException ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), ex.Output, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), ex.Error, cancellationToken);
            diagnostics.Add(new("gmsh_timeout", $"Gmsh не завершился за {_options.Timeout.TotalSeconds:G17} с."));
            return Failed(request, diagnostics, provenance);
        }
        catch (FileNotFoundException ex)
        {
            diagnostics.Add(new("gmsh_executable_not_found", ex.Message));
            return Failed(request, diagnostics, provenance);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException)
        {
            diagnostics.Add(new("gmsh_mesh_read_failed", ex.Message));
            return Failed(request, diagnostics, provenance);
        }
        finally
        {
            var manifest = new
            {
                generatorVersion = GeneratorVersion,
                gmshVersion = provenance.GmshVersion,
                executablePath = provenance.ExecutablePath,
                executableSource = executable?.Source,
                inputFingerprint = TryComputeFingerprint(request, provenance),
                settings = request.Settings,
                geoFile = "model.geo",
                mshFile = File.Exists(Path.Combine(directory, "model.msh")) ? "model.msh" : null,
                stdoutFile = "stdout.log",
                stderrFile = "stderr.log"
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                Encoding.UTF8,
                CancellationToken.None);
        }
    }

    static PlanarMeshSnapshot Failed(PlanarMeshingRequest request, IReadOnlyList<FemValidationDiagnostic> diagnostics, PlanarMeshProvenance provenance) => new()
    {
        RegionId = request.Region.Id,
         InputFingerprint = TryComputeFingerprint(request, provenance) ?? request.Region.GeometryFingerprint,
        IsCalculable = false,
        Settings = request.Settings,
        Provenance = provenance,
        Diagnostics = diagnostics
    };

    static string? TryComputeFingerprint(PlanarMeshingRequest request, PlanarMeshProvenance provenance)
    {
        try { return PlanarMeshFingerprint.Compute(request.Region, request.Settings, provenance, request.ConstraintSourceFingerprint); }
        catch (InvalidOperationException) { return null; }
    }

    static string BuildGeo(PlanarRegion region, PlanarMeshSettings settings) =>
        GmshPlanarGeoBuilder.Build(region, settings);

    static (IReadOnlyList<PlanarMeshNode> Nodes, IReadOnlyList<PlanarMeshElement> Elements,
        IReadOnlyList<PlanarMeshBoundaryMapping> BoundaryMappings,
        IReadOnlyList<PlanarMeshEntityProvenance> EntityProvenance,
         IReadOnlyList<PlanarConstraintMeshMapping> ConstraintMappings,
         IReadOnlyList<FemValidationDiagnostic> Diagnostics)
         ParseMsh41(
             GmshMsh41Document document,
             Frame3D frame,
             PlanarRegion region,
             IReadOnlyList<PlanarConstraintObject> constraintObjects)
    {
        var rawNodes = document.Nodes.ToDictionary(node => node.RawId);
        var ids = rawNodes.Keys.OrderBy(id => id).ToArray();
        var indices = ids.Select((id, dense) => (id, dense)).ToDictionary(pair => pair.id, pair => pair.dense);
        var nodes = ids.Select((id, dense) =>
        {
            var node = rawNodes[id];
            var global = frame.Origin + frame.LocalX * node.X + frame.LocalY * node.Y;
            return new PlanarMeshNode(dense, node.X, node.Y, global.X, global.Y, global.Z);
        }).ToArray();

        var diagnostics = document.Diagnostics.ToList();
        var elements = new List<PlanarMeshElement>();
        var boundaryEdges = new Dictionary<PlanarBoundaryKey, List<(int A, int B)>>();
        var expectedLines = BoundaryLineMap(region);
        foreach (var item in document.Elements)
        {
            if (item.ElementType == 1)
            {
                if (item.RawNodeIds.Count != 2) throw new InvalidDataException("Некорректная связность граничного элемента MSH 4.1.");
                if (!expectedLines.TryGetValue((int)item.EntityTag, out var key)) continue;
                if (item.PhysicalGroup != PhysicalGroup(key))
                    diagnostics.Add(new("gmsh_boundary_physical_group_mismatch", $"Граничный entity {item.EntityTag} имеет physical group {item.PhysicalGroup}, ожидался {PhysicalGroup(key)}."));
                var a = DenseIndex41(item.RawNodeIds[0], indices);
                var b = DenseIndex41(item.RawNodeIds[1], indices);
                if (!boundaryEdges.TryGetValue(key, out var edgeList)) boundaryEdges[key] = edgeList = [];
                edgeList.Add((a, b));
                continue;
            }

            if (item.ElementType is not (2 or 3)) continue;
            var expectedCount = item.ElementType == 2 ? 3 : 4;
            if (item.RawNodeIds.Count != expectedCount) throw new InvalidDataException("Некорректная связность shell-элемента MSH 4.1.");
            var connectivity = item.RawNodeIds.Select(raw => DenseIndex41(raw, indices)).ToArray();
            if (SignedArea(connectivity.Select(index => nodes[index]).ToArray()) < 0) Array.Reverse(connectivity);
            elements.Add(new(elements.Count, expectedCount == 3 ? PlanarMeshElementKind.Triangle3 : PlanarMeshElementKind.Quadrangle4, connectivity));
        }

        if (elements.Count == 0)
            diagnostics.Add(new("gmsh_mesh_empty", "Gmsh не вернул ни одного T3 или Q4 элемента."));
        var mappings = BuildBoundaryMappings(expectedLines, boundaryEdges, nodes, region, diagnostics);
         var constraintResult = PlanarConstraintMeshMapper.Map(region, document, nodes, elements, constraintObjects);
        diagnostics.AddRange(constraintResult.Diagnostics.Where(diagnostic => !document.Diagnostics.Contains(diagnostic)));
        var entityProvenance = HostEntityProvenance(document)
            .Concat(constraintResult.Mappings.SelectMany(mapping => mapping.EntityProvenance))
            .Distinct()
            .ToArray();
        return (nodes, elements, mappings, entityProvenance,
            constraintResult.Mappings, diagnostics);
    }

    static IReadOnlyList<PlanarMeshEntityProvenance> HostEntityProvenance(GmshMsh41Document document) =>
        document.Elements
            .Where(element => element.ElementType is (1 or 2 or 3) && element.PhysicalName.StartsWith("host:", StringComparison.Ordinal))
            .Select(element => new PlanarMeshEntityProvenance(
                element.PhysicalName,
                element.EntityDimension,
                checked((int)element.EntityTag),
                element.PhysicalGroup,
                element.PhysicalName))
            .Distinct()
            .ToArray();

    static int DenseIndex41(long rawId, IReadOnlyDictionary<long, int> indices) =>
        indices.TryGetValue(rawId, out var index) ? index : throw new InvalidDataException($"Элемент MSH ссылается на неизвестный узел {rawId}.");

    static (IReadOnlyList<PlanarMeshNode> Nodes, IReadOnlyList<PlanarMeshElement> Elements,
        IReadOnlyList<PlanarMeshBoundaryMapping> BoundaryMappings, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
        ParseMsh22(string[] lines, Frame3D frame, PlanarRegion region)
    {
        var nodeStart = Array.IndexOf(lines, "$Nodes");
        var elementStart = Array.IndexOf(lines, "$Elements");
        if (nodeStart < 0 || elementStart < 0)
            throw new InvalidDataException("MSH 2.2 не содержит Nodes или Elements.");

        var rawNodes = new Dictionary<int, (double U, double V)>();
        var nodeCount = ParseCount(lines, nodeStart);
        for (var i = 0; i < nodeCount; i++)
        {
            var values = Split(lines[nodeStart + 2 + i]);
            if (values.Length < 3) throw new InvalidDataException("Некорректная строка узла MSH.");
            rawNodes.Add(
                ParseInt(values[0]),
                (ParseDouble(values[1]), ParseDouble(values[2])));
        }

        var ids = rawNodes.Keys.OrderBy(id => id).ToArray();
        var indices = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var nodes = ids.Select((id, index) =>
        {
            var p = rawNodes[id];
            var global = frame.Origin + frame.LocalX * p.U + frame.LocalY * p.V;
            return new PlanarMeshNode(index, p.U, p.V, global.X, global.Y, global.Z);
        }).ToArray();

        var diagnostics = new List<FemValidationDiagnostic>();
        var elements = new List<PlanarMeshElement>();
        var boundaryEdges = new Dictionary<PlanarBoundaryKey, List<(int A, int B)>>();
        var expectedLines = BoundaryLineMap(region);
        var elementCount = ParseCount(lines, elementStart);
        for (var i = 0; i < elementCount; i++)
        {
            var values = Split(lines[elementStart + 2 + i]).Select(ParseInt).ToArray();
            if (values.Length < 3) throw new InvalidDataException("Некорректная строка элемента MSH.");
            var elementType = values[1];
            var tagCount = values[2];
            var connectivityStart = 3 + tagCount;
            if (connectivityStart > values.Length) throw new InvalidDataException("Некорректные теги элемента MSH.");
            var physical = tagCount > 0 ? values[3] : 0;
            var entity = tagCount > 1 ? values[4] : 0;

            if (elementType == 1)
            {
                if (values.Length - connectivityStart != 2) throw new InvalidDataException("Некорректная связность граничного элемента MSH.");
                var a = DenseIndex(values[connectivityStart], indices);
                var b = DenseIndex(values[connectivityStart + 1], indices);
                if (expectedLines.TryGetValue(entity, out var key))
                {
                    var expectedPhysical = PhysicalGroup(key);
                    if (physical != expectedPhysical)
                        diagnostics.Add(new("gmsh_boundary_physical_group_mismatch", $"Граничный элемент линии {entity} имеет physical group {physical}, ожидался {expectedPhysical}."));
                    if (!boundaryEdges.TryGetValue(key, out var edgeList))
                        boundaryEdges[key] = edgeList = [];
                    edgeList.Add((a, b));
                }
                continue;
            }

            if (elementType is 2 or 3)
            {
                var count = elementType == 2 ? 3 : 4;
                if (values.Length - connectivityStart != count) throw new InvalidDataException("Некорректная связность shell-элемента MSH.");
                var connectivity = values.Skip(connectivityStart).Select(id => DenseIndex(id, indices)).ToArray();
                var area = SignedArea(connectivity.Select(index => nodes[index]).ToArray());
                if (area < 0) Array.Reverse(connectivity);
                elements.Add(new(elements.Count, count == 3 ? PlanarMeshElementKind.Triangle3 : PlanarMeshElementKind.Quadrangle4, connectivity));
                continue;
            }

            if (elementType != 15)
                diagnostics.Add(new("gmsh_unsupported_element", $"Элемент MSH типа {elementType} не поддерживается для PlanarRegion."));
        }

        if (elements.Count == 0)
            diagnostics.Add(new("gmsh_mesh_empty", "Gmsh не вернул ни одного T3 или Q4 элемента."));
        var mappings = BuildBoundaryMappings(expectedLines, boundaryEdges, nodes, region, diagnostics);
        return (nodes, elements, mappings, diagnostics);
    }

    static IReadOnlyList<PlanarMeshBoundaryMapping> BuildBoundaryMappings(
        IReadOnlyDictionary<int, PlanarBoundaryKey> expectedLines,
        IReadOnlyDictionary<PlanarBoundaryKey, List<(int A, int B)>> boundaryEdges,
        IReadOnlyList<PlanarMeshNode> nodes,
        PlanarRegion region,
        ICollection<FemValidationDiagnostic> diagnostics)
    {
        var result = new List<PlanarMeshBoundaryMapping>();
        var endpoints = BoundaryEndpointMap(region);
        foreach (var key in expectedLines.Values.Distinct())
        {
            if (!boundaryEdges.TryGetValue(key, out var edges) || edges.Count == 0)
            {
                diagnostics.Add(new("gmsh_boundary_mapping_missing", $"Для границы {key.Loop}:{key.HoleIndex}:{key.StartVertex}-{key.EndVertex} не найдены рёбра сетки."));
                continue;
            }

            var start = FindNearestNode(nodes, endpoints[key].StartU, endpoints[key].StartV);
            var end = FindNearestNode(nodes, endpoints[key].EndU, endpoints[key].EndV);
            if (start < 0 || end < 0)
            {
                diagnostics.Add(new("gmsh_boundary_endpoint_missing", $"Концы границы {key.Loop}:{key.HoleIndex}:{key.StartVertex}-{key.EndVertex} не представлены узлами сетки."));
                continue;
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (var (a, b) in edges)
            {
                if (!adjacency.TryGetValue(a, out var aList)) adjacency[a] = aList = [];
                if (!adjacency.TryGetValue(b, out var bList)) adjacency[b] = bList = [];
                aList.Add(b);
                bList.Add(a);
            }

            var path = new List<int> { start };
            var previous = -1;
            var current = start;
            while (current != end)
            {
                if (!adjacency.TryGetValue(current, out var neighbours)) break;
                var next = neighbours.Where(index => index != previous).Distinct().ToArray();
                if (next.Length != 1 || path.Contains(next[0])) break;
                previous = current;
                current = next[0];
                path.Add(current);
            }
            if (current != end || path.Count != edges.Count + 1)
            {
                diagnostics.Add(new("gmsh_boundary_chain_invalid", $"Граница {key.Loop}:{key.HoleIndex}:{key.StartVertex}-{key.EndVertex} не образует однозначную цепочку рёбер."));
                continue;
            }
            result.Add(new() { Key = key, NodeIndices = path });
        }
        return result;
    }

    static IReadOnlyDictionary<PlanarBoundaryKey, (double StartU, double StartV, double EndU, double EndV)> BoundaryEndpointMap(PlanarRegion region)
    {
        var result = new Dictionary<PlanarBoundaryKey, (double, double, double, double)>();
        var holeIndex = 0;
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            var loop = contour.Type == ContourType.Hull ? BoundaryLoop.Outer : BoundaryLoop.Hole;
            var currentHole = loop == BoundaryLoop.Hole ? holeIndex++ : 0;
            for (var i = 0; i < x.Length; i++)
            {
                var key = new PlanarBoundaryKey(loop, currentHole, i, (i + 1) % x.Length);
                result[key] = (x[i], y[i], x[(i + 1) % x.Length], y[(i + 1) % x.Length]);
            }
        }
        return result;
    }

    static int FindNearestNode(IReadOnlyList<PlanarMeshNode> nodes, double u, double v)
    {
        var best = -1;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < nodes.Count; i++)
        {
            var du = nodes[i].U - u;
            var dv = nodes[i].V - v;
            var distance = du * du + dv * dv;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return bestDistance <= 1e-14 ? best : -1;
    }

    static IReadOnlyDictionary<int, PlanarBoundaryKey> BoundaryLineMap(PlanarRegion region)
    {
        var result = new Dictionary<int, PlanarBoundaryKey>();
        var line = 1;
        var holeIndex = 0;
        foreach (var contour in region.Contours)
        {
            var (x, y) = PlanarRegionTopologyValidator.ToOpenLoop(contour.X, contour.Y);
            var loop = contour.Type == ContourType.Hull ? BoundaryLoop.Outer : BoundaryLoop.Hole;
            var currentHole = loop == BoundaryLoop.Hole ? holeIndex++ : 0;
            for (var i = 0; i < x.Length; i++)
                result[line + i] = new(loop, currentHole, i, (i + 1) % x.Length);
            line += x.Length;
        }
        return result;
    }

    static int PhysicalGroup(PlanarBoundaryKey key) => key.Loop == BoundaryLoop.Outer ? OuterPhysicalGroup : HolePhysicalGroupBase + key.HoleIndex;

    static int DenseIndex(int rawId, IReadOnlyDictionary<int, int> indices) =>
        indices.TryGetValue(rawId, out var index) ? index : throw new InvalidDataException($"Элемент MSH ссылается на неизвестный узел {rawId}.");

    static string[] Split(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static int ParseCount(string[] lines, int sectionStart) => int.Parse(lines[sectionStart + 1], CultureInfo.InvariantCulture);
    static int ParseInt(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    static double SignedArea(IReadOnlyList<PlanarMeshNode> nodes) => Enumerable.Range(0, nodes.Count).Sum(i => nodes[i].U * nodes[(i + 1) % nodes.Count].V - nodes[(i + 1) % nodes.Count].U * nodes[i].V) / 2;
}

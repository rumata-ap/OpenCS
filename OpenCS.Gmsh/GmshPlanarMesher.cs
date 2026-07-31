using System.Diagnostics;
using System.Globalization;
using System.Text;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Gmsh.Runtime;

namespace OpenCS.Gmsh;

/// <summary>Строит сетку одного PlanarRegion внешним Gmsh в формате MSH 2.2 ASCII.</summary>
public sealed class GmshPlanarMesher : IPlanarMesher
{
    const string GeneratorVersion = "gmsh-planar-v1";
    readonly GmshPlanarMesherOptions _options;

    public GmshPlanarMesher(GmshPlanarMesherOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PlanarMeshSnapshot> BuildAsync(PlanarMeshingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = PlanarRegionValidator.Validate(request.Region).ToList();
        try { request.Settings.Validate(); }
        catch (InvalidOperationException ex) { diagnostics.Add(new FemValidationDiagnostic("planar_mesh_settings_invalid", ex.Message)); }
        if (diagnostics.Any(d => d.IsError)) return Failed(request, diagnostics, new("", GeneratorVersion));

        var provenance = new PlanarMeshProvenance("4.15.2", GeneratorVersion);
        string directory = Path.Combine(_options.ArtifactRoot, $"gmsh-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string geoPath = Path.Combine(directory, "model.geo");
        string mshPath = Path.Combine(directory, "model.msh");
        try
        {
            var executable = new GmshExecutableResolver(_options.BundledExecutablePath).Resolve(_options.ExecutablePath);
            await File.WriteAllTextAsync(geoPath, BuildGeo(request.Region, request.Settings), Encoding.UTF8, cancellationToken);
            var result = await RunAsync(executable.Path, directory, [geoPath, "-2", "-format", "msh22", "-order", "1", "-o", mshPath], cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "stdout.log"), result.Output, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "stderr.log"), result.Error, cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(mshPath))
            {
                diagnostics.Add(new("gmsh_process_failed", $"Gmsh завершился с кодом {result.ExitCode}."));
                return Failed(request, diagnostics, provenance);
            }

            var (nodes, elements) = ParseMsh22(await File.ReadAllLinesAsync(mshPath, cancellationToken), request.Region.Frame);
            var snapshot = new PlanarMeshSnapshot
            {
                RegionId = request.Region.Id,
                InputFingerprint = PlanarMeshFingerprint.Compute(request.Region, request.Settings, provenance),
                IsCalculable = true,
                Settings = request.Settings,
                Provenance = provenance,
                Diagnostics = diagnostics,
                Nodes = nodes,
                Elements = elements
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
                BoundaryMappings = snapshot.BoundaryMappings
            };
        }
        catch (FileNotFoundException ex)
        {
            diagnostics.Add(new("gmsh_executable_not_found", ex.Message));
            return Failed(request, diagnostics, provenance);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OperationCanceledException)
        {
            diagnostics.Add(new("gmsh_mesh_read_failed", ex.Message));
            return Failed(request, diagnostics, provenance);
        }
    }

    static PlanarMeshSnapshot Failed(PlanarMeshingRequest request, IReadOnlyList<FemValidationDiagnostic> diagnostics, PlanarMeshProvenance provenance) => new()
    {
        RegionId = request.Region.Id,
        InputFingerprint = request.Region.GeometryFingerprint,
        IsCalculable = false,
        Settings = request.Settings,
        Provenance = provenance,
        Diagnostics = diagnostics
    };

    static async Task<(int ExitCode, string Output, string Error)> RunAsync(string executable, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить Gmsh.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }

    static string BuildGeo(PlanarRegion region, PlanarMeshSettings settings)
    {
        var result = new StringBuilder();
        result.AppendLine("Mesh.ElementOrder = 1;");
        result.AppendLine($"Mesh.Algorithm = {settings.Algorithm};");
        result.AppendLine($"Mesh.CharacteristicLengthMax = {settings.MaxElementSizeM.ToString("G17", CultureInfo.InvariantCulture)};");
        if (settings.ElementMode is PlanarMeshElementMode.Quads or PlanarMeshElementMode.Mixed)
            result.AppendLine("Mesh.RecombineAll = 1;");
        var loops = new List<int>();
        var point = 1;
        var line = 1;
        foreach (var contour in region.Contours)
        {
            var n = contour.X.Count - 1;
            var points = Enumerable.Range(point, n).ToArray();
            for (var i = 0; i < n; i++)
                result.AppendLine($"Point({points[i]}) = {{{contour.X[i].ToString("G17", CultureInfo.InvariantCulture)}, {contour.Y[i].ToString("G17", CultureInfo.InvariantCulture)}, 0, {settings.MaxElementSizeM.ToString("G17", CultureInfo.InvariantCulture)}}};");
            var lines = Enumerable.Range(line, n).ToArray();
            for (var i = 0; i < n; i++) result.AppendLine($"Line({lines[i]}) = {{{points[i]}, {points[(i + 1) % n]}}};");
            var loop = loops.Count + 1;
            result.AppendLine($"Curve Loop({loop}) = {{{string.Join(", ", lines)}}};");
            loops.Add(loop);
            point += n;
            line += n;
        }
        result.AppendLine($"Plane Surface(1) = {{{string.Join(", ", loops)}}};");
        return result.ToString();
    }

    static (IReadOnlyList<PlanarMeshNode> Nodes, IReadOnlyList<PlanarMeshElement> Elements) ParseMsh22(string[] lines, Frame3D frame)
    {
        var nodeStart = Array.IndexOf(lines, "$Nodes");
        var elementStart = Array.IndexOf(lines, "$Elements");
        if (nodeStart < 0 || elementStart < 0) throw new InvalidDataException("MSH 2.2 не содержит Nodes или Elements.");
        var rawNodes = new Dictionary<int, (double U, double V)>();
        var nodeCount = int.Parse(lines[nodeStart + 1], CultureInfo.InvariantCulture);
        for (var i = 0; i < nodeCount; i++)
        {
            var values = lines[nodeStart + 2 + i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rawNodes.Add(int.Parse(values[0], CultureInfo.InvariantCulture), (double.Parse(values[1], CultureInfo.InvariantCulture), double.Parse(values[2], CultureInfo.InvariantCulture)));
        }
        var ids = rawNodes.Keys.OrderBy(id => id).ToArray();
        var indices = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var nodes = ids.Select((id, index) =>
        {
            var p = rawNodes[id];
            var global = frame.Origin + frame.LocalX * p.U + frame.LocalY * p.V;
            return new PlanarMeshNode(index, p.U, p.V, global.X, global.Y, global.Z);
        }).ToArray();
        var elements = new List<PlanarMeshElement>();
        var elementCount = int.Parse(lines[elementStart + 1], CultureInfo.InvariantCulture);
        for (var i = 0; i < elementCount; i++)
        {
            var values = lines[elementStart + 2 + i].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (values[1] is not (2 or 3)) continue;
            var count = values[1] == 2 ? 3 : 4;
            var tags = values[2];
            var connectivity = values.Skip(3 + tags).Select(id => indices[id]).ToArray();
            if (connectivity.Length != count) throw new InvalidDataException("Некорректная связность shell-элемента MSH.");
            var area = SignedArea(connectivity.Select(index => nodes[index]).ToArray());
            if (area < 0) Array.Reverse(connectivity);
            elements.Add(new PlanarMeshElement(elements.Count, count == 3 ? PlanarMeshElementKind.Triangle3 : PlanarMeshElementKind.Quadrangle4, connectivity));
        }
        return (nodes, elements);
    }

    static double SignedArea(IReadOnlyList<PlanarMeshNode> nodes) => Enumerable.Range(0, nodes.Count).Sum(i => nodes[i].U * nodes[(i + 1) % nodes.Count].V - nodes[(i + 1) % nodes.Count].U * nodes[i].V) / 2;
}

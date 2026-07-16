using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;

namespace OpenCS.OpenSees.Services;

/// <summary>
/// Строит поверхность N–Mx–My по граничным и дополнительным опорным срезам,
/// а затем проверяет относительно неё исходные точки ForceSet.
/// </summary>
public sealed class SectionSpatialInteractionService
{
    private const double AxialToleranceFactor = 1e-9;
    private const double GeometryTolerance = 1e-9;
    private readonly ISpatialSectionAnalysisExecutor _executor;

    /// <summary>Создаёт оркестратор с исполнителем одного пространственного прогона.</summary>
    public SectionSpatialInteractionService(ISpatialSectionAnalysisExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>Строит поверхность или вырожденный плоский полярный срез.</summary>
    public async Task<SectionSpatialInteractionResult> RunAsync(
        OpenSeesSectionModel model,
        SectionSpatialInteractionRequest request,
        OpenSeesRunRequest processRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(processRequest);

        model.Validate();
        request.Validate();

        double[] candidates = request.AxialForcesN.OrderBy(value => value).ToArray();
        IReadOnlyList<double> angles = request.GenerateAnglesDegrees();
        Dictionary<double, SectionSpatialInteractionSlice> cache = [];
        List<string> diagnostics = [];

        async Task<SectionSpatialInteractionSlice> RunSliceAsync(double axialForceN, string role)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cache.TryGetValue(axialForceN, out SectionSpatialInteractionSlice? cached))
            {
                cached = await RunSliceCoreAsync(
                    model,
                    request,
                    processRequest,
                    axialForceN,
                    angles,
                    cancellationToken);
                cache.Add(axialForceN, cached);
            }

            if (cached.Role == role)
                return cached;

            return new SectionSpatialInteractionSlice
            {
                AxialForceN = cached.AxialForceN,
                Role = role,
                IsComplete = cached.IsComplete,
                Status = cached.Status,
                Points = cached.Points,
                Diagnostics = cached.Diagnostics
            };
        }

        double minimumCandidate = candidates[0];
        double maximumCandidate = candidates[^1];
        if (NearlyEqual(minimumCandidate, maximumCandidate))
        {
            SectionSpatialInteractionSlice plane = await RunSliceAsync(minimumCandidate, "boundary");
            diagnostics.AddRange(plane.Diagnostics);
            List<SectionSpatialInteractionDemandCheck> checks = plane.IsComplete
                ? CheckPlaneDemands(request.DemandPoints, plane)
                : CreateIndeterminateChecks(request.DemandPoints, "not_converged");

            return CreateResult(
                status: GetAggregateStatus([plane]),
                geometryKind: "plane",
                slices: [plane],
                diagnostics,
                checks,
                effectiveMinimumAxialForceN: minimumCandidate,
                effectiveMaximumAxialForceN: maximumCandidate);
        }

        SectionSpatialInteractionSlice? lower = await FindBoundaryAsync(
            candidates,
            ascending: true,
            RunSliceAsync,
            diagnostics,
            cancellationToken);
        SectionSpatialInteractionSlice? upper = await FindBoundaryAsync(
            candidates,
            ascending: false,
            RunSliceAsync,
            diagnostics,
            cancellationToken);

        if (lower is null || upper is null || NearlyEqual(lower.AxialForceN, upper.AxialForceN) ||
            lower.AxialForceN > upper.AxialForceN)
        {
            diagnostics.Add("Не удалось найти две различные сходящиеся граничные величины N.");
            return CreateResult(
                status: "not_converged",
                geometryKind: "surface",
                slices: [],
                diagnostics,
                CreateIndeterminateChecks(request.DemandPoints, "no_surface"),
                null,
                null);
        }

        List<double> supportForces = BuildSupportForces(
            lower.AxialForceN,
            upper.AxialForceN,
            request.AdditionalAxialSlices);
        List<SectionSpatialInteractionSlice> slices = [];
        foreach (double axialForceN in supportForces)
        {
            string role = NearlyEqual(axialForceN, lower.AxialForceN) ||
                          NearlyEqual(axialForceN, upper.AxialForceN)
                ? "boundary"
                : "support";
            SectionSpatialInteractionSlice slice = await RunSliceAsync(axialForceN, role);
            slices.Add(slice);
            diagnostics.AddRange(slice.Diagnostics);
        }

        bool completeSurface = slices.All(slice => slice.IsComplete);
        List<SectionSpatialInteractionDemandCheck> demandChecks = completeSurface
            ? CheckSurfaceDemands(request.DemandPoints, slices)
            : CreateIndeterminateChecks(request.DemandPoints, "not_converged");

        return CreateResult(
            status: GetAggregateStatus(slices),
            geometryKind: "surface",
            slices,
            diagnostics,
            demandChecks,
            lower.AxialForceN,
            upper.AxialForceN);
    }

    private async Task<SectionSpatialInteractionSlice?> FindBoundaryAsync(
        IReadOnlyList<double> candidates,
        bool ascending,
        Func<double, string, Task<SectionSpatialInteractionSlice>> runSlice,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        IEnumerable<double> ordered = ascending ? candidates : candidates.Reverse();
        foreach (double candidate in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SectionSpatialInteractionSlice slice = await runSlice(candidate, "boundary");
            if (slice.IsComplete)
                return slice;

            diagnostics.Add($"Граничный срез N={candidate:G6} не сошёлся полностью; выполнен поиск ближе к диапазону.");
        }

        return null;
    }

    private async Task<SectionSpatialInteractionSlice> RunSliceCoreAsync(
        OpenSeesSectionModel model,
        SectionSpatialInteractionRequest request,
        OpenSeesRunRequest processRequest,
        double axialForceN,
        IReadOnlyList<double> angles,
        CancellationToken cancellationToken)
    {
        List<SectionSpatialInteractionPoint> points = [];
        List<string> diagnostics = [];

        foreach (double angle in angles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SpatialSectionAnalysisResult analysis = await _executor.RunAsync(
                    model,
                    new SpatialSectionAnalysisRequest
                    {
                        AxialForceN = axialForceN,
                        AngleDegrees = angle,
                        MaxCurvature = request.MaxCurvature,
                        Increments = request.Increments,
                        Convention = request.Convention
                    },
                    processRequest,
                    cancellationToken);
                SectionSpatialInteractionPoint point = CreatePoint(axialForceN, angle, analysis);
                points.Add(point);
                diagnostics.AddRange(point.Diagnostics.Select(message => FormatDiagnostic(axialForceN, angle, message)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                string message = exception.Message;
                points.Add(new SectionSpatialInteractionPoint
                {
                    AxialForceN = axialForceN,
                    AngleDegrees = angle,
                    Status = "error",
                    Diagnostics = [message]
                });
                diagnostics.Add(FormatDiagnostic(axialForceN, angle, message));
            }
        }

        string status = GetAggregateStatus(points.Select(point => point.Status));
        return new SectionSpatialInteractionSlice
        {
            AxialForceN = axialForceN,
            Status = status,
            IsComplete = points.Count == angles.Count && points.All(IsConvergedPoint),
            Points = points,
            Diagnostics = diagnostics
        };
    }

    private static SectionSpatialInteractionPoint CreatePoint(
        double axialForceN,
        double angle,
        SpatialSectionAnalysisResult analysis)
    {
        SpatialSectionHistoryRow? terminal = analysis.Rows.LastOrDefault(row => row.Converged);
        return new SectionSpatialInteractionPoint
        {
            AxialForceN = axialForceN,
            AngleDegrees = angle,
            MomentMxNm = terminal?.MomentMxNm,
            MomentMyNm = terminal?.MomentMyNm,
            CurvatureMx = terminal?.CurvatureMx,
            CurvatureMy = terminal?.CurvatureMy,
            TerminalRow = terminal,
            HistoryRows = analysis.Rows.ToArray(),
            Status = analysis.Status,
            Diagnostics = analysis.Diagnostics,
            ArtifactDirectory = analysis.ArtifactDirectory
        };
    }

    private static List<double> BuildSupportForces(double minimum, double maximum, int additionalCount)
    {
        List<double> result = [minimum];
        for (int index = 1; index <= additionalCount; index++)
            result.Add(minimum + (maximum - minimum) * index / (additionalCount + 1));
        result.Add(maximum);
        return result;
    }

    private static List<SectionSpatialInteractionDemandCheck> CheckPlaneDemands(
        IReadOnlyList<SpatialInteractionDemandPoint> demands,
        SectionSpatialInteractionSlice slice)
    {
        List<(double X, double Y)> polygon = GetPolygon(slice);
        return demands.Select(demand => CreateDemandCheck(demand, polygon)).ToList();
    }

    private static List<SectionSpatialInteractionDemandCheck> CheckSurfaceDemands(
        IReadOnlyList<SpatialInteractionDemandPoint> demands,
        IReadOnlyList<SectionSpatialInteractionSlice> slices)
    {
        List<SectionSpatialInteractionDemandCheck> checks = [];
        foreach (SpatialInteractionDemandPoint demand in demands)
        {
            if (demand.AxialForceN < slices[0].AxialForceN &&
                !NearlyEqual(demand.AxialForceN, slices[0].AxialForceN) ||
                demand.AxialForceN > slices[^1].AxialForceN &&
                !NearlyEqual(demand.AxialForceN, slices[^1].AxialForceN))
            {
                checks.Add(new SectionSpatialInteractionDemandCheck
                {
                    Num = demand.Num,
                    Label = demand.Label,
                    AxialForceN = demand.AxialForceN,
                    MomentMxNm = demand.MomentMxNm,
                    MomentMyNm = demand.MomentMyNm,
                    Status = "outside_axial_range",
                    Diagnostic = "Продольная сила точки находится вне проверенного диапазона поверхности."
                });
                continue;
            }

            SectionSpatialInteractionSlice lower = slices.Last(slice =>
                slice.AxialForceN <= demand.AxialForceN || NearlyEqual(slice.AxialForceN, demand.AxialForceN));
            SectionSpatialInteractionSlice upper = slices.First(slice =>
                slice.AxialForceN >= demand.AxialForceN || NearlyEqual(slice.AxialForceN, demand.AxialForceN));
            List<(double X, double Y)> polygon = InterpolatePolygon(lower, upper, demand.AxialForceN);
            checks.Add(CreateDemandCheck(demand, polygon));
        }

        return checks;
    }

    private static List<(double X, double Y)> InterpolatePolygon(
        SectionSpatialInteractionSlice lower,
        SectionSpatialInteractionSlice upper,
        double axialForceN)
    {
        List<(double X, double Y)> low = GetPolygon(lower);
        List<(double X, double Y)> high = GetPolygon(upper);
        if (NearlyEqual(lower.AxialForceN, upper.AxialForceN) || low.Count != high.Count)
            return low;

        double factor = (axialForceN - lower.AxialForceN) /
            (upper.AxialForceN - lower.AxialForceN);
        return low.Zip(high, (left, right) =>
            (left.X + (right.X - left.X) * factor,
             left.Y + (right.Y - left.Y) * factor)).ToList();
    }

    private static List<(double X, double Y)> GetPolygon(SectionSpatialInteractionSlice slice) =>
        slice.Points
            .Where(point => point.MomentMxNm.HasValue && point.MomentMyNm.HasValue)
            .Select(point => (point.MomentMxNm!.Value, point.MomentMyNm!.Value))
            .ToList();

    private static SectionSpatialInteractionDemandCheck CreateDemandCheck(
        SpatialInteractionDemandPoint demand,
        IReadOnlyList<(double X, double Y)> polygon)
    {
        bool inside = IsInsidePolygon(polygon, demand.MomentMxNm, demand.MomentMyNm);
        double? utilization = TryCalculateUtilization(polygon, demand.MomentMxNm, demand.MomentMyNm);
        return new SectionSpatialInteractionDemandCheck
        {
            Num = demand.Num,
            Label = demand.Label,
            AxialForceN = demand.AxialForceN,
            MomentMxNm = demand.MomentMxNm,
            MomentMyNm = demand.MomentMyNm,
            IsInside = inside,
            Utilization = utilization,
            Status = inside ? "inside" : "outside",
            Diagnostic = inside
                ? "Точка находится внутри полярного среза."
                : "Точка находится вне полярного среза."
        };
    }

    private static bool IsInsidePolygon(
        IReadOnlyList<(double X, double Y)> polygon,
        double x,
        double y)
    {
        if (polygon.Count < 3)
            return false;

        bool inside = false;
        for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
        {
            (double currentX, double currentY) = polygon[index];
            (double previousX, double previousY) = polygon[previous];
            if (IsPointOnSegment(x, y, previousX, previousY, currentX, currentY))
                return true;

            bool crosses = (currentY > y) != (previousY > y);
            if (crosses && x < (previousX - currentX) * (y - currentY) /
                (previousY - currentY) + currentX)
                inside = !inside;
        }

        return inside;
    }

    private static double? TryCalculateUtilization(
        IReadOnlyList<(double X, double Y)> polygon,
        double x,
        double y)
    {
        double demandRadius = Math.Sqrt(x * x + y * y);
        if (demandRadius <= GeometryTolerance)
            return 0;

        double directionX = x / demandRadius;
        double directionY = y / demandRadius;
        double? capacityRadius = null;
        for (int index = 0; index < polygon.Count; index++)
        {
            (double x1, double y1) = polygon[index];
            (double x2, double y2) = polygon[(index + 1) % polygon.Count];
            double cross = directionX * (y2 - y1) - directionY * (x2 - x1);
            if (Math.Abs(cross) <= GeometryTolerance)
                continue;

            double t = (x1 * y2 - y1 * x2) / cross;
            double u = (x1 * directionY - y1 * directionX) / cross;
            if (t >= -GeometryTolerance && u >= -GeometryTolerance && u <= 1 + GeometryTolerance)
                capacityRadius = capacityRadius.HasValue
                    ? Math.Min(capacityRadius.Value, Math.Max(0, t))
                    : Math.Max(0, t);
        }

        return capacityRadius is double capacity && capacity > GeometryTolerance
            ? demandRadius / capacity
            : null;
    }

    private static bool IsPointOnSegment(
        double x,
        double y,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        double cross = (x - x1) * (y2 - y1) - (y - y1) * (x2 - x1);
        if (Math.Abs(cross) > GeometryTolerance)
            return false;
        return x >= Math.Min(x1, x2) - GeometryTolerance &&
               x <= Math.Max(x1, x2) + GeometryTolerance &&
               y >= Math.Min(y1, y2) - GeometryTolerance &&
               y <= Math.Max(y1, y2) + GeometryTolerance;
    }

    private static List<SectionSpatialInteractionDemandCheck> CreateIndeterminateChecks(
        IReadOnlyList<SpatialInteractionDemandPoint> demands,
        string status) =>
        demands.Select(demand => new SectionSpatialInteractionDemandCheck
        {
            Num = demand.Num,
            Label = demand.Label,
            AxialForceN = demand.AxialForceN,
            MomentMxNm = demand.MomentMxNm,
            MomentMyNm = demand.MomentMyNm,
            Status = status,
            Diagnostic = status == "no_surface"
                ? "Поверхность несущей способности не построена."
                : "Проверка невозможна: не все опорные срезы сошлись."
        }).ToList();

    private static SectionSpatialInteractionResult CreateResult(
        string status,
        string geometryKind,
        IReadOnlyList<SectionSpatialInteractionSlice> slices,
        IReadOnlyList<string> diagnostics,
        IReadOnlyList<SectionSpatialInteractionDemandCheck> demandChecks,
        double? effectiveMinimumAxialForceN,
        double? effectiveMaximumAxialForceN) => new()
        {
            Status = status,
            VerificationStatus = GetVerificationStatus(demandChecks),
            GeometryKind = geometryKind,
            EffectiveMinimumAxialForceN = effectiveMinimumAxialForceN,
            EffectiveMaximumAxialForceN = effectiveMaximumAxialForceN,
            Slices = slices,
            Points = slices.SelectMany(slice => slice.Points).ToArray(),
            DemandChecks = demandChecks,
            Diagnostics = diagnostics
        };

    private static string GetVerificationStatus(IReadOnlyList<SectionSpatialInteractionDemandCheck> checks)
    {
        if (checks.Count == 0)
            return "not_available";
        if (checks.Any(check => check.Status is "outside" or "outside_axial_range"))
            return "not_ok";
        return checks.All(check => check.Status == "inside") ? "ok" : "indeterminate";
    }

    private static bool IsConvergedPoint(SectionSpatialInteractionPoint point) =>
        point.Status == "ok" && point.MomentMxNm.HasValue && point.MomentMyNm.HasValue;

    private static string GetAggregateStatus(IEnumerable<SectionSpatialInteractionSlice> slices) =>
        GetAggregateStatus(slices.Select(slice => slice.Status));

    private static string GetAggregateStatus(IEnumerable<string> statuses) =>
        statuses.Any(status => status == "error")
            ? "error"
            : statuses.Any(status => status != "ok")
                ? "not_converged"
                : "ok";

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= AxialToleranceFactor * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));

    private static string FormatDiagnostic(double axialForce, double angle, string diagnostic) =>
        $"N={axialForce}, angle={angle}°: {diagnostic}";
}

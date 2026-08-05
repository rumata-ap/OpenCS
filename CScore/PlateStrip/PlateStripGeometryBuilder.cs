using CScore.Fem;
using CScore.Planar;

namespace CScore.PlateStrip;

/// <summary>Результат построения геометрии полосы: либо валидный PlateStripBeamAnalogy, либо
/// диагностики без результата. Паттерн идентичен PlanarConstraintDeriver.Derive.</summary>
public sealed record PlateStripBuildResult(
    bool IsCalculable,
    PlateStripBeamAnalogy? Analogy,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Строит геометрию полосы плиты между двумя явно заданными опорами (Срез 1
/// стержневой аналогии полосы плиты, WidthPolicy = ExplicitWidth). Чистая функция: не
/// мутирует PlanarRegion, не обращается к БД/OpenSees. Auto-derivation опор из
/// PlanarConnection/RigidTransferDomain — задача следующих срезов. См.
/// docs/superpowers/specs/2026-08-05-plate-strip-beam-analogy-slice1-design.md.</summary>
public static class PlateStripGeometryBuilder
{
    const double MinAxisLengthM = 1e-6;

    public static PlateStripBuildResult Build(
        string id, PlanarRegion region, SupportLocus start, SupportLocus end, double explicitWidthM)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (region.Hull is null)
            throw new ArgumentException("У региона отсутствует Hull.", nameof(region));

        if (!start.Frame.Origin.IsFinite || !end.Frame.Origin.IsFinite || !double.IsFinite(explicitWidthM))
            return Fail("plate_strip_invalid_input",
                "Координаты опор или ширина полосы не являются конечными числами.");

        var startUv = ProjectToRegionPlane(region.Frame, start.Frame.Origin);
        var endUv = ProjectToRegionPlane(region.Frame, end.Frame.Origin);

        double axisU = endUv.U - startUv.U;
        double axisV = endUv.V - startUv.V;
        double length = Math.Sqrt(axisU * axisU + axisV * axisV);
        if (!(length >= MinAxisLengthM))
            return Fail("plate_strip_degenerate_axis",
                "Опоры совпадают или практически совпадают в плоскости региона — ось полосы вырождена.");
        if (!(explicitWidthM > 0))
            return Fail("plate_strip_invalid_width", "ExplicitWidthM должна быть положительной.");

        double axisDirU = axisU / length, axisDirV = axisV / length;
        double perpU = -axisDirV, perpV = axisDirU;

        if (!SegmentIntersectsHull(region.Hull.X, region.Hull.Y, startUv, endUv))
            return Fail("plate_strip_outside_region", "Ось полосы (CenterLine) не пересекает Hull региона.");

        var hullLocal = ToStripLocal(region.Hull.X, region.Hull.Y, startUv, axisDirU, axisDirV, perpU, perpV);
        var hullParts = ClipToStrip(hullLocal, length, explicitWidthM);

        // hullParts.Count == 0 здесь означает, что CenterLine касается границы Hull только
        // тангенциально (в вершине/вдоль ребра), без реальной площади пересечения —
        // SegmentIntersectsHull выше это пропускает (пересечение отрезка с контуром — не то
        // же самое, что положительная площадь клиппированного полигона). Тот же код
        // диагностики: с инженерной точки зрения это тоже "нет пригодного материала полосы".
        // Умышленно не покрыт отдельным тестом (см. plate_strip_degenerate_polygon в Global
        // Constraints/спеке) — надёжный тест этой ветки потребовал бы точного совпадения с
        // границей на грани double-точности, к тому же PointInPolygon/SegmentsIntersect не
        // гарантируют консистентного поведения ровно на границе.
        if (hullParts.Count == 0)
            return Fail("plate_strip_outside_region", "Ось полосы не пересекает Hull региона.");
        if (hullParts.Count > 1)
            return Fail("plate_strip_non_contiguous",
                "Клиппинг полосы по Hull региона даёт несвязный результат — разбиение на span fragments не входит в этот срез.");

        foreach (var hole in region.Holes)
        {
            var holeLocal = ToStripLocal(hole.X, hole.Y, startUv, axisDirU, axisDirV, perpU, perpV);
            holeLocal.Reverse(); // Holes — CW по конвенции региона; переворачиваем в CCW (как GridSplit.Slice)
            var holeParts = ClipToStrip(holeLocal, length, explicitWidthM);
            if (holeParts.Count > 0)
                return Fail("plate_strip_crosses_hole", "Полоса пересекает отверстие региона.");
        }

        var candidate = hullParts[0];
        var polygon = candidate.Select(p => ToRegionUv(p, startUv, axisDirU, axisDirV, perpU, perpV)).ToList();
        var stripFrame = BuildStripFrame(region.Frame, startUv, axisDirU, axisDirV);

        var geometry = new PlateStripGeometry
        {
            CenterLine = [startUv, endUv],
            LeftBoundary =
            [
                ToRegionUv((0, explicitWidthM / 2), startUv, axisDirU, axisDirV, perpU, perpV),
                ToRegionUv((length, explicitWidthM / 2), startUv, axisDirU, axisDirV, perpU, perpV)
            ],
            RightBoundary =
            [
                ToRegionUv((0, -explicitWidthM / 2), startUv, axisDirU, axisDirV, perpU, perpV),
                ToRegionUv((length, -explicitWidthM / 2), startUv, axisDirU, axisDirV, perpU, perpV)
            ],
            Polygon = polygon,
            LengthM = length
        };

        var analogy = new PlateStripBeamAnalogy
        {
            Id = id,
            SourceRegionId = region.Id,
            StartSupportLocus = start,
            EndSupportLocus = end,
            StripFrame = stripFrame,
            Geometry = geometry,
            ExplicitWidthM = explicitWidthM,
            Fingerprint = PlateStripFingerprint.Compute(region, start, end, explicitWidthM)
        };

        return new PlateStripBuildResult(true, analogy, []);
    }

    static PlanarPoint2D ProjectToRegionPlane(Frame3D frame, PlanarVector3 point)
    {
        var d = point - frame.Origin;
        return new PlanarPoint2D(d.Dot(frame.LocalX), d.Dot(frame.LocalY));
    }

    static List<(double X, double Y)> ToStripLocal(
        IList<double> x, IList<double> y, PlanarPoint2D originUv,
        double axisDirU, double axisDirV, double perpU, double perpV)
    {
        var (ox, oy) = PlanarRegionTopologyValidator.ToOpenLoop(x, y);
        var result = new List<(double X, double Y)>(ox.Length);
        for (int i = 0; i < ox.Length; i++)
        {
            double du = ox[i] - originUv.U;
            double dv = oy[i] - originUv.V;
            result.Add((du * axisDirU + dv * axisDirV, du * perpU + dv * perpV));
        }
        return result;
    }

    static PlanarPoint2D ToRegionUv(
        (double S, double W) p, PlanarPoint2D originUv, double axisDirU, double axisDirV, double perpU, double perpV) =>
        new(originUv.U + p.S * axisDirU + p.W * perpU, originUv.V + p.S * axisDirV + p.W * perpV);

    static Frame3D BuildStripFrame(Frame3D regionFrame, PlanarPoint2D startUv, double axisDirU, double axisDirV)
    {
        var origin = regionFrame.Origin + regionFrame.LocalX * startUv.U + regionFrame.LocalY * startUv.V;
        var localX = (regionFrame.LocalX * axisDirU + regionFrame.LocalY * axisDirV).Normalize();
        var localZ = regionFrame.LocalZ;
        var localY = localZ.Cross(localX);
        var frame = new Frame3D(origin, localX, localY, localZ);
        frame.Validate();
        return frame;
    }

    /// <summary>Полный клиппинг-пайплайн GridSplit.Slice (ClipByRect → RemoveSpikes →
    /// SplitWoundPolygon) для прямоугольного коридора полосы [0,length]x[-widthM/2,widthM/2]
    /// в strip-local координатах — общий для Hull (Task 9) и каждого Hole (Task 10), без
    /// сокращённого/частичного повторения пайплайна.</summary>
    static List<List<(double X, double Y)>> ClipToStrip(
        List<(double X, double Y)> stripLocalPoints, double length, double widthM)
    {
        var clip = GridSplit.ClipByRect(stripLocalPoints, 0, length, -widthM / 2, widthM / 2);
        var spikeless = GridSplit.RemoveSpikes(clip);
        return GridSplit.SplitWoundPolygon(spikeless, 0, length, -widthM / 2, widthM / 2);
    }

    static PlateStripBuildResult Fail(string code, string message) =>
        new(false, null, [new FemValidationDiagnostic(code, message)]);

    static bool SegmentIntersectsHull(IList<double> hullX, IList<double> hullY, PlanarPoint2D a, PlanarPoint2D b)
    {
        var (hx, hy) = PlanarRegionTopologyValidator.ToOpenLoop(hullX, hullY);
        var poly = new double[hx.Length][];
        for (int i = 0; i < hx.Length; i++) poly[i] = [hx[i], hy[i]];

        if (CSTriangulation.GeometryUtils.PointInPolygon(a.U, a.V, poly)) return true;
        if (CSTriangulation.GeometryUtils.PointInPolygon(b.U, b.V, poly)) return true;

        int n = hx.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (CSTriangulation.GeometryUtils.SegmentsIntersect(a.U, a.V, b.U, b.V, hx[i], hy[i], hx[j], hy[j]))
                return true;
        }
        return false;
    }
}

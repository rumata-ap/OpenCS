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
    public static PlateStripBuildResult Build(
        string id, PlanarRegion region, SupportLocus start, SupportLocus end, double explicitWidthM)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (region.Hull is null)
            throw new ArgumentException("У региона отсутствует Hull.", nameof(region));

        var startUv = ProjectToRegionPlane(region.Frame, start.Frame.Origin);
        var endUv = ProjectToRegionPlane(region.Frame, end.Frame.Origin);

        double axisU = endUv.U - startUv.U;
        double axisV = endUv.V - startUv.V;
        double length = Math.Sqrt(axisU * axisU + axisV * axisV);

        double axisDirU = axisU / length, axisDirV = axisV / length;
        double perpU = -axisDirV, perpV = axisDirU;

        var hullLocal = ToStripLocal(region.Hull.X, region.Hull.Y, startUv, axisDirU, axisDirV, perpU, perpV);
        var hullParts = ClipToStrip(hullLocal, length, explicitWidthM);

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
}

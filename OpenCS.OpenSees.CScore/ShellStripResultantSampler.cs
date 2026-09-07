using System;
using System.Collections.Generic;
using System.Linq;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateStrip;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.CScore
{
    /// <summary>
    /// Эпюра [N, My, Mz] полосы, полученная интегрированием <b>реальных</b> shell resultants
    /// прогона по сечениям полосы (Срез 7).
    ///
    /// Первый потребитель реальных ShellSectionResultants в цепочке полосы: Срез 3a проверял
    /// только алгебраическую самосогласованность редукции, Срез 3b гомогенизировал RVE — ни тот,
    /// ни другой результатов настоящего прогона не читал.
    ///
    /// Связь плитных усилий с балочными задаётся тем же оператором StripKinematicEmbedding,
    /// которым пользуются EquivalentSectionCalculator и StripResultantIntegrator:
    /// <code>
    /// N  = ∫ Nx dv,   My = ∫ Mx dv,   Mz = -∫ v·Nx dv
    /// </code>
    ///
    /// <b>Отбор элементов и веса — одна операция.</b> Линия сечения станции пересекается с
    /// полигоном каждого элемента, вес равен длине отрезка внутри него, поэтому сумма весов
    /// равна ширине полосы по построению. Отбор «по центроидам в полосе шириной в один элемент»
    /// не применяется: он не даёт сумму весов, равную ширине, и требует размера элемента,
    /// которого в PlanarMeshSnapshot нет.
    /// </summary>
    public static class ShellStripResultantSampler
    {
        public static double[][] Sample(
            IReadOnlyList<ShellSectionResultants> resultants,
            IReadOnlyDictionary<int, int> elementTagToIndex,
            PlanarMeshSnapshot snapshot,
            Frame3D regionFrame,
            PlateStripBeamAnalogy analogy,
            IReadOnlyList<double> stationFractions,
            out IReadOnlyList<FemValidationDiagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(resultants);
            ArgumentNullException.ThrowIfNull(elementTagToIndex);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(analogy);
            ArgumentNullException.ThrowIfNull(stationFractions);

            var found = new List<FemValidationDiagnostic>();
            diagnostics = found;

            double lengthM = analogy.Geometry.LengthM;
            double halfWidth = analogy.ExplicitWidthM / 2.0;
            if (!(lengthM > 0.0) || !(halfWidth > 0.0))
                throw new ArgumentException("Полоса должна иметь положительные длину и ширину.", nameof(analogy));

            // Усреднение по точкам интегрирования элемента, как в
            // ShellMeshPatchPlateSectionResponse: у Q4 их четыре, у T3 — до трёх.
            var averaged = new Dictionary<int, ShellSectionResultants>();
            foreach (var group in resultants.Where(r => r != null).GroupBy(r => r.ElementTag))
            {
                int count = group.Count();
                averaged[group.Key] = new ShellSectionResultants(
                    group.Key, 0,
                    group.Average(r => r.Nx), group.Average(r => r.Ny), group.Average(r => r.Nxy),
                    group.Average(r => r.Mx), group.Average(r => r.My), group.Average(r => r.Mxy),
                    group.Average(r => r.Qx), group.Average(r => r.Qy));
            }

            // Угол от осей оболочки (совпадают с осями региона у PlanarMeshSnapshotShellModelAdapter)
            // к осям полосы, вокруг нормали региона.
            double angle = ShellResultantRotation.AngleBetween(
                regionFrame.LocalX.X, regionFrame.LocalX.Y, regionFrame.LocalX.Z,
                analogy.StripFrame.LocalX.X, analogy.StripFrame.LocalX.Y, analogy.StripFrame.LocalX.Z,
                regionFrame.LocalZ.X, regionFrame.LocalZ.Y, regionFrame.LocalZ.Z);

            var elementPolygons = BuildStripPolygons(snapshot, regionFrame, analogy);

            var result = new double[stationFractions.Count][];
            for (int s = 0; s < stationFractions.Count; s++)
            {
                double x = stationFractions[s] * lengthM;
                var station = new double[3];
                double totalWeight = 0.0;

                // Станция, попавшая на общее ребро соседних столбцов элементов, иначе была бы
                // учтена дважды. Первый проход берёт элемент по полуоткрытому правилу
                // [xmin, xmax); если так не нашлось ни одного (станция на дальнем конце сетки),
                // второй проход разрешает правую границу.
                bool includeRightEdge = !elementPolygons.Any(
                    e => TryCutAt(e.Polygon, x, halfWidth, false, out _, out _));

                foreach (var (elementIndex, polygon) in elementPolygons)
                {
                    if (!TryCutAt(polygon, x, halfWidth, includeRightEdge, out double vLow, out double vHigh))
                        continue;

                    int tag = elementTagToIndex
                        .Where(pair => pair.Value == elementIndex)
                        .Select(pair => (int?)pair.Key)
                        .FirstOrDefault() ?? -1;
                    if (tag < 0 || !averaged.TryGetValue(tag, out var raw)) continue;

                    var rotated = ShellResultantRotation.ToKilonewton(
                        ShellResultantRotation.Rotate(raw, angle));

                    double weight = vHigh - vLow;
                    double vMid = 0.5 * (vLow + vHigh);
                    totalWeight += weight;

                    station[0] += weight * rotated.Nx;
                    station[1] += weight * rotated.Mx;
                    station[2] -= weight * vMid * rotated.Nx;
                }

                if (totalWeight <= 0.0)
                    found.Add(new("plate_strip_shell_sampler_empty_station",
                        $"Станция {s + 1} (доля {stationFractions[s]:G4}) не пересекла ни одного элемента сетки."));
                result[s] = station;
            }

            foreach (int tag in averaged.Keys)
                if (!elementTagToIndex.ContainsKey(tag))
                {
                    found.Add(new("plate_strip_shell_sampler_unknown_element",
                        $"Элемент с тегом {tag} отсутствует в переданном отображении тегов сетки."));
                    break;
                }

            return result;
        }

        /// <summary>Полигоны элементов в координатах полосы: (x вдоль оси, v поперёк).</summary>
        static List<(int ElementIndex, List<(double X, double V)> Polygon)> BuildStripPolygons(
            PlanarMeshSnapshot snapshot, Frame3D regionFrame, PlateStripBeamAnalogy analogy)
        {
            var byIndex = snapshot.Nodes.ToDictionary(n => n.Index);
            var polygons = new List<(int, List<(double, double)>)>(snapshot.Elements.Count);

            foreach (var element in snapshot.Elements)
            {
                var polygon = new List<(double, double)>(element.NodeIndices.Count);
                bool complete = true;
                foreach (int nodeIndex in element.NodeIndices)
                {
                    if (!byIndex.TryGetValue(nodeIndex, out var node)) { complete = false; break; }
                    var global = PlanarBoundaryFrameConverter.ToGlobalPoint(
                        regionFrame, new PlanarVector3(node.U, node.V, 0.0));
                    var strip = PlanarBoundaryFrameConverter.ToLocalPoint(analogy.StripFrame, global);
                    polygon.Add((strip.X, strip.Y));
                }
                if (complete && polygon.Count >= 3)
                    polygons.Add((element.Index, polygon));
            }
            return polygons;
        }

        /// <summary>Отрезок сечения x = const внутри полигона, обрезанный коридором полосы.</summary>
        static bool TryCutAt(
            List<(double X, double V)> polygon, double x, double halfWidth, bool includeRightEdge,
            out double vLow, out double vHigh)
        {
            vLow = vHigh = 0.0;

            // Полуоткрытый по x пролёт элемента: иначе станция на общем ребре двух соседних
            // элементов даёт двойной вклад, и сумма весов оказывается вдвое больше ширины.
            double elementMin = polygon.Min(p => p.X), elementMax = polygon.Max(p => p.X);
            if (x < elementMin - 1e-12) return false;
            if (includeRightEdge ? x > elementMax + 1e-12 : x >= elementMax - 1e-12) return false;

            var crossings = new List<double>(4);

            for (int i = 0; i < polygon.Count; i++)
            {
                var (x1, v1) = polygon[i];
                var (x2, v2) = polygon[(i + 1) % polygon.Count];
                if (x1 == x2)
                {
                    if (Math.Abs(x1 - x) < 1e-12) { crossings.Add(v1); crossings.Add(v2); }
                    continue;
                }
                double lo = Math.Min(x1, x2), hi = Math.Max(x1, x2);
                if (x < lo - 1e-12 || x > hi + 1e-12) continue;
                double t = (x - x1) / (x2 - x1);
                crossings.Add(v1 + t * (v2 - v1));
            }

            if (crossings.Count < 2) return false;
            double low = Math.Max(crossings.Min(), -halfWidth);
            double high = Math.Min(crossings.Max(), halfWidth);
            if (high - low <= 1e-12) return false;

            vLow = low;
            vHigh = high;
            return true;
        }
    }
}

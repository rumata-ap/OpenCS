using System;
using System.Collections.Generic;
using System.Linq;
using CScore.Fem;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Автоматически вырезает вертикальный фрагмент стены из элементов конструктивной схемы FemSchema.
    /// </summary>
    public class FemSchemaWallExtractor
    {
        public VerticalPlanarFragment ExtractWallFragment(FemSchema schema, FemMember member, IReadOnlyList<FemNode> nodes, string fragmentName)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (member == null) throw new ArgumentNullException(nameof(member));
            if (nodes == null || nodes.Count < 3)
                throw new ArgumentException("Для вырезания стены требуется минимум 3 узла.", nameof(nodes));

            // Локальный 3D-базис стены через PlanarVector3
            PlanarVector3 p0 = new PlanarVector3(nodes[0].X, nodes[0].Y, nodes[0].Z);
            PlanarVector3 p1 = new PlanarVector3(nodes[1].X, nodes[1].Y, nodes[1].Z);
            PlanarVector3 p2 = new PlanarVector3(nodes[2].X, nodes[2].Y, nodes[2].Z);

            PlanarVector3 dirU = (p1 - p0).Normalize();
            PlanarVector3 normal = (dirU.Cross(p2 - p0)).Normalize();
            PlanarVector3 dirV = (normal.Cross(dirU)).Normalize();

            Frame3D frame = new Frame3D(p0, dirU, dirV, normal);

            // Перевод 3D-узлов в локальные координаты (u, v)
            List<double> xCoords = new List<double>();
            List<double> yCoords = new List<double>();

            foreach (var node in nodes)
            {
                var delta = new PlanarVector3(node.X, node.Y, node.Z) - frame.Origin;
                xCoords.Add(delta.Dot(frame.LocalX));
                yCoords.Add(delta.Dot(frame.LocalY));
            }

            var sourceContour = new Contour
            {
                Id = member.Id,
                Tag = member.ElemTag,
                X = xCoords,
                Y = yCoords
            };

            var region = PlanarRegion.CreateFromContour(sourceContour, frame: frame, tag: fragmentName);
            region.Id = member.Id;

            var bottomCut = BuildEdgeCut(
                region, PlanarCutInterfaceKind.BottomCut, $"wall_{member.Id}_bottom",
                selectMin: true, normal: new PlanarVector3(0, -1, 0), frame: frame);
            var topCut = BuildEdgeCut(
                region, PlanarCutInterfaceKind.TopCut, $"wall_{member.Id}_top",
                selectMin: false, normal: new PlanarVector3(0, 1, 0), frame: frame);

            return new VerticalPlanarFragment
            {
                FragmentId = member.Id,
                Name = fragmentName,
                Region = region,
                BottomCut = bottomCut,
                TopCut = topCut,
                StageConfig = FragmentStageConfig.CreateDefault1Stage()
            };
        }

        /// <summary>Строит cut interface на реальном ребре Hull региона (минимальная либо
        /// максимальная V-координата) через PlanarBoundaryKey — не через embedded-кривую,
        /// совпадающую с уже существующей границей.</summary>
        static PlanarCutInterface BuildEdgeCut(
            PlanarRegion region, PlanarCutInterfaceKind kind, string id,
            bool selectMin, PlanarVector3 normal, Frame3D frame)
        {
            Contour hull = region.Hull ?? throw new InvalidOperationException(
                "PlanarRegion.CreateFromContour должен был создать Hull.");
            // Hull.X/Y замкнуты (последняя точка дублирует первую) — рабочий диапазон [0, Count-2].
            int openCount = hull.Y.Count - 1;
            double target = selectMin ? hull.Y.Take(openCount).Min() : hull.Y.Take(openCount).Max();
            var matches = Enumerable.Range(0, openCount)
                .Where(i => Math.Abs(hull.Y[i] - target) < 1e-6)
                .ToList();
            if (matches.Count != 2)
                throw new InvalidOperationException(
                    $"Ожидалось ровно 2 вершины на {(selectMin ? "нижнем" : "верхнем")} ребре стены, найдено {matches.Count}. " +
                    "FemSchemaWallExtractor поддерживает только прямоугольные стеновые панели.");

            int start = matches[0];
            int end = matches[1];
            var points = new List<PlanarPoint2D>
            {
                new PlanarPoint2D(hull.X[start], hull.Y[start]),
                new PlanarPoint2D(hull.X[end], hull.Y[end])
            };

            return new PlanarCutInterface
            {
                Id = id,
                Kind = kind,
                Geometry = new PlanarConstraintGeometry(PlanarConstraintGeometryKind.Curve, points),
                NormalFromFragmentToOmittedSide = normal,
                Frame = frame,
                BoundaryKey = new PlanarBoundaryKey(BoundaryLoop.Outer, 0, start, end)
            };
        }
    }
}

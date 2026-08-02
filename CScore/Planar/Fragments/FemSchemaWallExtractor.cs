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
            List<PlanarPoint2D> pts2D = new List<PlanarPoint2D>();

            foreach (var node in nodes)
            {
                var delta = new PlanarVector3(node.X, node.Y, node.Z) - frame.Origin;
                double u = delta.Dot(frame.LocalX);
                double v = delta.Dot(frame.LocalY);
                xCoords.Add(u);
                yCoords.Add(v);
                pts2D.Add(new PlanarPoint2D(u, v));
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

            double minY = yCoords.Min();
            double maxY = yCoords.Max();

            var bottomPts = pts2D.Where(p => Math.Abs(p.V - minY) < 1e-4).OrderBy(p => p.U).ToList();
            var topPts = pts2D.Where(p => Math.Abs(p.V - maxY) < 1e-4).OrderBy(p => p.U).ToList();

            var bottomCut = new PlanarCutInterface
            {
                Id = $"wall_{member.Id}_bottom",
                Kind = PlanarCutInterfaceKind.BottomCut,
                Geometry = new PlanarConstraintGeometry(
                    PlanarConstraintGeometryKind.Curve,
                    bottomPts.Count >= 2 ? bottomPts : new List<PlanarPoint2D> { pts2D[0], pts2D[1] }),
                NormalFromFragmentToOmittedSide = new PlanarVector3(0, -1, 0),
                Frame = frame
            };

            var topCut = new PlanarCutInterface
            {
                Id = $"wall_{member.Id}_top",
                Kind = PlanarCutInterfaceKind.TopCut,
                Geometry = new PlanarConstraintGeometry(
                    PlanarConstraintGeometryKind.Curve,
                    topPts.Count >= 2 ? topPts : new List<PlanarPoint2D> { pts2D[2], pts2D[3 % pts2D.Count] }),
                NormalFromFragmentToOmittedSide = new PlanarVector3(0, 1, 0),
                Frame = frame
            };

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
    }
}

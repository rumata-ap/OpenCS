using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CScore.Fem;

namespace CScore.Planar.Fragments
{
    /// <summary>Доменный агрегат многоэтажной фибровой/балочной колонны, проходящей через
    /// N заранее выбранных перекрытий (Levels). Балочные сегменты колонны (Segments) собираются
    /// позиционно между соседними уровнями; автоматического поиска геометрии нет.</summary>
    public class MultiStoryColumnFragment
    {
        /// <summary>Идентификатор фрагмента.</summary>
        public int FragmentId { get; set; }
        /// <summary>Имя фрагмента.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Уровни (перекрытия) снизу вверх, минимум 2.</summary>
        public List<ColumnFloorLevel> Levels { get; set; } = new List<ColumnFloorLevel>();
        /// <summary>Балочные сегменты колонны между соседними уровнями:
        /// Segments.Count == Levels.Count - 1, Segments[i] соединяет Levels[i] и Levels[i+1].</summary>
        public List<ColumnSegment> Segments { get; set; } = new List<ColumnSegment>();
        /// <summary>Заделка низа самого нижнего уровня.</summary>
        public ColumnBaseFixity BaseSupport { get; set; } = ColumnBaseFixity.None;
        /// <summary>Тип geomTransf нелинейных балочных элементов колонны — единый на всю
        /// составную модель (ShellOpenSeesModel.NonlinearBeamGeomTransfKind).</summary>
        public string GeomTransfKind { get; set; } = "PDelta";
        /// <summary>Формулировка нелинейного балочного элемента — единая на всю составную
        /// модель (ShellOpenSeesModel.NonlinearBeamElementFormulation).</summary>
        public string ElementFormulation { get; set; } = "forceBeamColumn";
        /// <summary>Конфигурация стадий нелинейного нагружения фрагмента.</summary>
        public FragmentStageConfig StageConfig { get; set; } = FragmentStageConfig.CreateDefault1Stage();
        /// <summary>Template-наборы boundary actions, ключ — Id внешней границы любого уровня
        /// (объединение всех Levels[i].Boundaries).</summary>
        public Dictionary<string, PlanarBoundaryActionSet> BoundaryTemplates { get; set; } =
            new Dictionary<string, PlanarBoundaryActionSet>();

        /// <summary>Проверяет инварианты агрегата. Blocking diagnostics имеют стабильные коды
        /// multistory_column_* и содержательное сообщение.</summary>
        public IReadOnlyList<FemValidationDiagnostic> Validate()
        {
            var diagnostics = new List<FemValidationDiagnostic>();

            if (Levels is null || Levels.Count < 2)
            {
                diagnostics.Add(new("multistory_column_level_count_invalid",
                    "Фрагмент должен содержать минимум 2 уровня (Levels)."));
                return diagnostics;
            }

            if (Segments is null || Segments.Count != Levels.Count - 1)
                diagnostics.Add(new("multistory_column_segment_sequence_invalid",
                    $"Segments.Count должен быть равен Levels.Count - 1 " +
                    $"({Levels.Count - 1}), фактически {Segments?.Count ?? 0}."));

            var levelIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var level in Levels)
            {
                if (level is null || string.IsNullOrWhiteSpace(level.Id))
                {
                    diagnostics.Add(new("multistory_column_duplicate_id", "Уровень без Id."));
                    continue;
                }
                if (!levelIds.Add(level.Id))
                    diagnostics.Add(new("multistory_column_duplicate_id",
                        $"Id уровня '{level.Id}' повторяется."));

                var hull = level.PlateRegion?.Hull;
                if (hull is null)
                {
                    diagnostics.Add(new("multistory_column_anchor_outside_hull",
                        $"Уровень '{level.Id}': PlateRegion.Hull не задан."));
                    continue;
                }
                bool insideHull = WktHelper.PointInPolygon(
                    hull.X, hull.Y, level.ColumnAnchorLocalXY.U, level.ColumnAnchorLocalXY.V);
                if (!insideHull)
                {
                    diagnostics.Add(new("multistory_column_anchor_outside_hull",
                        $"Уровень '{level.Id}': anchor-точка ({level.ColumnAnchorLocalXY.U}, " +
                        $"{level.ColumnAnchorLocalXY.V}) лежит вне Hull региона."));
                    continue;
                }
                foreach (var hole in level.PlateRegion!.Holes)
                    if (WktHelper.PointInPolygon(
                        hole.X, hole.Y, level.ColumnAnchorLocalXY.U, level.ColumnAnchorLocalXY.V))
                        diagnostics.Add(new("multistory_column_anchor_inside_hole",
                            $"Уровень '{level.Id}': anchor-точка попадает в отверстие региона."));
            }

            var segmentIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var segment in Segments ?? new List<ColumnSegment>())
            {
                if (segment is null || string.IsNullOrWhiteSpace(segment.Id))
                {
                    diagnostics.Add(new("multistory_column_duplicate_id", "Сегмент без Id."));
                    continue;
                }
                if (!segmentIds.Add(segment.Id))
                    diagnostics.Add(new("multistory_column_duplicate_id",
                        $"Id сегмента '{segment.Id}' повторяется."));
            }

            var boundaryIds = new HashSet<string>(System.StringComparer.Ordinal);
            var allBoundaries = Levels.SelectMany(level => level.Boundaries ?? new List<FloorJunctionBoundary>()).ToList();
            foreach (var boundary in allBoundaries)
            {
                if (boundary is null || string.IsNullOrWhiteSpace(boundary.Id))
                {
                    diagnostics.Add(new("multistory_column_duplicate_id", "Boundary без Id."));
                    continue;
                }
                if (!boundaryIds.Add(boundary.Id))
                    diagnostics.Add(new("multistory_column_duplicate_id",
                        $"Id границы '{boundary.Id}' повторяется."));
            }

            var templateKeys = BoundaryTemplates?.Keys ?? Enumerable.Empty<string>();
            foreach (var boundary in allBoundaries)
            {
                if (boundary is null || string.IsNullOrWhiteSpace(boundary.Id)) continue;
                if (!templateKeys.Contains(boundary.Id))
                    diagnostics.Add(new("multistory_column_boundary_template_missing",
                        $"Для boundary '{boundary.Id}' не задан template в BoundaryTemplates."));
            }
            foreach (var key in templateKeys.OrderBy(k => k, System.StringComparer.Ordinal))
                if (!boundaryIds.Contains(key))
                    diagnostics.Add(new("multistory_column_boundary_template_missing",
                        $"Template '{key}' не соответствует ни одному boundary."));

            return diagnostics;
        }

        /// <summary>Хэш-отпечаток актуальности параметров агрегата: fragment id; для каждого
        /// уровня — GeometryFingerprint региона, PlateSection, ColumnAnchorLocalXY, внешние
        /// границы и mesh settings; для каждого сегмента — CrossSection identity, GJ, Vecxz;
        /// BaseSupport; GeomTransfKind/ElementFormulation; StageConfig; все boundary-action
        /// templates. Хранится только в памяти — schema migration в этот срез не входит.</summary>
        public string GetFingerprint(IReadOnlyDictionary<string, PlanarMeshSettings> levelMeshSettings)
        {
            var values = new List<string> { "multistory-column-v1", FragmentId.ToString(CultureInfo.InvariantCulture) };

            foreach (var level in Levels ?? new List<ColumnFloorLevel>())
            {
                values.Add(level.Id);
                values.Add(level.PlateRegion?.GeometryFingerprint ?? "null");
                values.Add(SectionFingerprint(level.PlateSection));
                values.Add($"{Fmt(level.ColumnAnchorLocalXY.U)}:{Fmt(level.ColumnAnchorLocalXY.V)}");
                values.Add(string.Join(";", (level.Boundaries ?? new List<FloorJunctionBoundary>())
                    .OrderBy(b => b.Id, StringComparer.Ordinal)
                    .Select(b => $"{b.Id}:{b.RegionId}:{CutFingerprint(b.Cut)}")));
                values.Add(levelMeshSettings.TryGetValue(level.Id, out var settings)
                    ? MeshSettingsFingerprint(settings) : "null");
            }

            foreach (var segment in Segments ?? new List<ColumnSegment>())
            {
                values.Add(segment.Id);
                values.Add($"{segment.Section?.Id.ToString(CultureInfo.InvariantCulture) ?? "null"}:" +
                    $"{segment.Section?.Tag ?? "null"}:{segment.Section?.Areas.Count ?? 0}");
                values.Add(Fmt(segment.GJ));
                values.Add($"{Fmt(segment.Vecxz.X)}:{Fmt(segment.Vecxz.Y)}:{Fmt(segment.Vecxz.Z)}");
                values.Add(segment.NumIntegrationPoints.ToString(CultureInfo.InvariantCulture));
            }

            values.Add(BaseSupport.ToString());
            values.Add(GeomTransfKind);
            values.Add(ElementFormulation);
            values.Add(string.Join(";", (StageConfig?.Stages ?? new List<FragmentStage>()).Select(stage =>
                $"{stage.StageIndex}:{stage.Name ?? string.Empty}:{Fmt(stage.SurfaceLoadScale)}:" +
                $"{Fmt(stage.CutInterfaceScale)}:{SolverFingerprint(stage.Solver)}")));
            values.Add(string.Join(";", (BoundaryTemplates ?? new Dictionary<string, PlanarBoundaryActionSet>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value is null
                    ? $"{pair.Key}:null"
                    : $"{pair.Key}:{PlanarBoundaryActionFingerprint.Compute(
                        new PlanarBoundaryActionProviderResult
                        {
                            SourceMode = pair.Value.SourceMode,
                            ForceActions = pair.Value.ForceActions,
                            KinematicActions = pair.Value.KinematicActions,
                            SourceReferences = pair.Value.SourceReferences
                        },
                        BoundaryById(pair.Key)?.Cut ?? new PlanarCutInterface { Id = pair.Key })}")));

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values)));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        FloorJunctionBoundary? BoundaryById(string id) =>
            (Levels ?? new List<ColumnFloorLevel>())
                .SelectMany(level => level.Boundaries ?? new List<FloorJunctionBoundary>())
                .SingleOrDefault(boundary => string.Equals(boundary.Id, id, StringComparison.Ordinal));

        static string SectionFingerprint(PlateSection? section)
        {
            if (section is null) return "null";
            var rebar = (section.RebarLayers ?? new List<PlateRebarLayer>()).Select(layer =>
                $"{Fmt(layer.Asx)},{Fmt(layer.Asy)},{Fmt(layer.Zsx)},{Fmt(layer.Zsy)},{layer.MaterialId},{Fmt(layer.Angle)}");
            return string.Join(";", new[]
            {
                section.H.ToString("G17", CultureInfo.InvariantCulture),
                section.NLayers.ToString(CultureInfo.InvariantCulture),
                section.ConcreteMaterialId.ToString(CultureInfo.InvariantCulture),
                section.RebarMaterialId.ToString(CultureInfo.InvariantCulture),
                section.TensionConcrete ? "1" : "0",
                section.SofteningModel,
                Fmt(section.SofteningEpsC2),
                section.PlateModel,
                section.ConcreteDiagramType.ToString(),
                string.Join(";", rebar)
            });
        }

        static string SolverFingerprint(SolverParameters? solver)
        {
            if (solver is null) return "null";
            return string.Join(":", new[]
            {
                solver.Algorithm ?? string.Empty, Fmt(solver.EnergyTolerance), Fmt(solver.UnbalanceTolerance),
                solver.MaxIterations.ToString(CultureInfo.InvariantCulture),
                Fmt(solver.InitialStep), Fmt(solver.MinStep), Fmt(solver.MaxStep)
            });
        }

        static string CutFingerprint(PlanarCutInterface? cut)
        {
            if (cut is null) return "null";
            Frame3D frame = cut.Frame;
            var boundaryKey = cut.BoundaryKey is null
                ? "null"
                : $"{cut.BoundaryKey.Loop}:{cut.BoundaryKey.HoleIndex}:{cut.BoundaryKey.StartVertex}:{cut.BoundaryKey.EndVertex}";
            return string.Join(";", new[]
            {
                cut.Id, cut.Kind.ToString(),
                string.Join(",", (cut.Geometry?.Points ?? new List<PlanarPoint2D>())
                    .Select(point => $"{Fmt(point.U)},{Fmt(point.V)}")),
                Fmt(cut.NormalFromFragmentToOmittedSide.X), Fmt(cut.NormalFromFragmentToOmittedSide.Y),
                Fmt(cut.NormalFromFragmentToOmittedSide.Z), cut.ModeByDof.ToString(),
                cut.MeshConstraintId ?? string.Empty,
                Fmt(frame.Origin.X), Fmt(frame.Origin.Y), Fmt(frame.Origin.Z),
                Fmt(frame.LocalX.X), Fmt(frame.LocalX.Y), Fmt(frame.LocalX.Z),
                Fmt(frame.LocalY.X), Fmt(frame.LocalY.Y), Fmt(frame.LocalY.Z),
                Fmt(frame.LocalZ.X), Fmt(frame.LocalZ.Y), Fmt(frame.LocalZ.Z),
                Fmt(cut.ToleranceM), boundaryKey, cut.OmittedSideReference ?? string.Empty
            });
        }

        static string MeshSettingsFingerprint(PlanarMeshSettings? settings)
        {
            if (settings is null) return "null";
            return $"{Fmt(settings.MaxElementSizeM)}:{settings.Algorithm}:{settings.ElementMode}";
        }

        static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    }
}

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
    }
}

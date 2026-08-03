using System.Collections.Generic;
using System.Linq;
using CScore;
using CScore.Fem;
using CScore.Planar;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Доменный агрегат пары «горизонтальная плита + вертикальная стена» с одним явным
    /// пространственным junction. Оба региона заранее выбраны пользователем; автоматического
    /// поиска примыкающих элементов нет.
    /// </summary>
    public class FloorJunctionFragment
    {
        /// <summary>Идентификатор фрагмента junction.</summary>
        public int FragmentId { get; set; }
        /// <summary>Имя фрагмента junction.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Горизонтальная плита (источник геометрии, не мутируется mesh workflow).</summary>
        public PlanarRegion PlateRegion { get; set; } = new PlanarRegion();
        /// <summary>Вертикальная стена.</summary>
        public PlanarRegion WallRegion { get; set; } = new PlanarRegion();
        /// <summary>Секция плиты.</summary>
        public PlateSection PlateSection { get; set; } = new PlateSection();
        /// <summary>Секция стены.</summary>
        public PlateSection WallSection { get; set; } = new PlateSection();
        /// <summary>Явный пространственный junction; MeshMode обязан быть ConformingPartition.</summary>
        public PlanarConnection Connection { get; set; } = new PlanarConnection();
        /// <summary>Конфигурация стадий нелинейного нагружения фрагмента.</summary>
        public FragmentStageConfig StageConfig { get; set; } = FragmentStageConfig.CreateDefault1Stage();
        /// <summary>Внешние boundary interfaces (не junction), каждый с уникальным Id.</summary>
        public List<FloorJunctionBoundary> Boundaries { get; set; } = new List<FloorJunctionBoundary>();
        /// <summary>Template-наборы boundary actions на 100% величины, ключ — FloorJunctionBoundary.Id.</summary>
        public Dictionary<string, PlanarBoundaryActionSet> BoundaryTemplates { get; set; } =
            new Dictionary<string, PlanarBoundaryActionSet>();

        /// <summary>Проверяет инварианты агрегата. Blocking diagnostics имеют стабильные коды
        /// floor_junction_* и содержательное сообщение.</summary>
        public IReadOnlyList<FemValidationDiagnostic> Validate()
        {
            var diagnostics = new List<FemValidationDiagnostic>();

            if (PlateRegion is null || WallRegion is null ||
                PlateRegion.Id <= 0 || WallRegion.Id <= 0)
                diagnostics.Add(new("floor_junction_region_mismatch",
                    "Оба региона должны быть заданы с положительными ID."));
            else if (PlateRegion.Id == WallRegion.Id)
                diagnostics.Add(new("floor_junction_region_mismatch",
                    "PlateRegion.Id и WallRegion.Id должны различаться."));

            if (Connection is null)
            {
                diagnostics.Add(new("floor_junction_connection_missing", "Connection не задан."));
            }
            else
            {
                if (PlateRegion is not null && WallRegion is not null &&
                    (Connection.SideA?.RegionId != PlateRegion.Id ||
                     Connection.SideB?.RegionId != WallRegion.Id))
                    diagnostics.Add(new("floor_junction_region_mismatch",
                        "Стороны connection должны ссылаться на PlateRegion (SideA) и WallRegion (SideB)."));
                if (Connection.MeshMode != PlanarConnectionMeshMode.ConformingPartition)
                    diagnostics.Add(new("floor_junction_mesh_mode_unsupported",
                        $"MeshMode {Connection.MeshMode} не поддерживается; требуется ConformingPartition."));
            }

            var boundaryIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var boundary in Boundaries ?? new List<FloorJunctionBoundary>())
            {
                if (boundary is null || string.IsNullOrWhiteSpace(boundary.Id))
                {
                    diagnostics.Add(new("floor_junction_boundary_duplicate_id", "Boundary без Id."));
                    continue;
                }
                if (!boundaryIds.Add(boundary.Id))
                    diagnostics.Add(new("floor_junction_boundary_duplicate_id",
                        $"Boundary Id '{boundary.Id}' повторяется."));
                if (PlateRegion is null || WallRegion is null ||
                    (boundary.RegionId != PlateRegion.Id && boundary.RegionId != WallRegion.Id))
                    diagnostics.Add(new("floor_junction_boundary_unknown_region",
                        $"Boundary '{boundary.Id}' ссылается на неизвестный регион {boundary.RegionId}."));
                if (boundary.Cut is null)
                    diagnostics.Add(new("floor_junction_boundary_mapping_missing",
                        $"Boundary '{boundary.Id}' не содержит cut interface."));
                else if (boundary.Cut.MeshConstraintId is string meshConstraintId &&
                         meshConstraintId.StartsWith("connection:", System.StringComparison.Ordinal))
                    diagnostics.Add(new("floor_junction_boundary_uses_junction",
                        $"Boundary '{boundary.Id}' ссылается на junction mapping как на cut boundary."));
            }

            var templateKeys = BoundaryTemplates?.Keys ??
                new Dictionary<string, PlanarBoundaryActionSet>().Keys;
            foreach (var boundary in Boundaries ?? new List<FloorJunctionBoundary>())
            {
                if (boundary is null || string.IsNullOrWhiteSpace(boundary.Id)) continue;
                if (!templateKeys.Contains(boundary.Id))
                    diagnostics.Add(new("floor_junction_boundary_template_missing",
                        $"Для boundary '{boundary.Id}' не задан template в BoundaryTemplates."));
            }
            foreach (var key in templateKeys.OrderBy(k => k, System.StringComparer.Ordinal))
                if (boundaryIds.Contains(key) == false)
                    diagnostics.Add(new("floor_junction_boundary_template_missing",
                        $"Template '{key}' не соответствует ни одному boundary."));

            return diagnostics;
        }
    }
}

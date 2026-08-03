using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

        /// <summary>Хэш-отпечаток актуальности параметров агрегата: IDs, геометрические fingerprints
        /// обоих регионов, connection fingerprint, секции с арматурными слоями, boundary contracts,
        /// mesh settings обеих сторон и stage configuration. Используется только в памяти и в
        /// результате расчёта (schema migration вне среза).</summary>
        public string GetFingerprint(PlanarMeshSettings plateMeshSettings, PlanarMeshSettings wallMeshSettings)
        {
            var values = new List<string>
            {
                "floor-junction-v1",
                FragmentId.ToString(CultureInfo.InvariantCulture),
                PlateRegion?.Id.ToString(CultureInfo.InvariantCulture) ?? "null",
                PlateRegion?.GeometryFingerprint ?? "null",
                WallRegion?.Id.ToString(CultureInfo.InvariantCulture) ?? "null",
                WallRegion?.GeometryFingerprint ?? "null",
                Connection is null ? "null" : PlanarConnectionFingerprint.Compute(Connection),
                SectionFingerprint(PlateSection),
                SectionFingerprint(WallSection),
                string.Join(";", (Boundaries ?? new List<FloorJunctionBoundary>())
                    .OrderBy(boundary => boundary.Id, StringComparer.Ordinal)
                    .Select(boundary => $"{boundary.Id}:{boundary.RegionId}:{CutFingerprint(boundary.Cut)}")),
                string.Join(";", (BoundaryTemplates ?? new Dictionary<string, PlanarBoundaryActionSet>())
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value is null
                        ? $"{pair.Key}:null"
                        : $"{pair.Key}:{PlanarBoundaryActionFingerprint.Compute(
                            ProviderResult(pair.Value),
                            BoundaryById(pair.Key)?.Cut ?? new PlanarCutInterface { Id = pair.Key })}")),
                MeshSettingsFingerprint(plateMeshSettings),
                MeshSettingsFingerprint(wallMeshSettings),
                string.Join(";", (StageConfig?.Stages ?? new List<FragmentStage>()).Select(stage =>
                    $"{stage.StageIndex}:{Fmt(stage.SurfaceLoadScale)}:{Fmt(stage.CutInterfaceScale)}:" +
                    $"{stage.Solver?.Algorithm ?? string.Empty}:{stage.Solver?.MaxIterations ?? 0}"))
            };

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values)));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        static PlanarBoundaryActionProviderResult ProviderResult(PlanarBoundaryActionSet set) => new()
        {
            SourceMode = set.SourceMode,
            ForceActions = set.ForceActions,
            KinematicActions = set.KinematicActions,
            SourceReferences = set.SourceReferences
        };

        FloorJunctionBoundary? BoundaryById(string id) =>
            (Boundaries ?? new List<FloorJunctionBoundary>())
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
                string.Join(";", rebar)
            });
        }

        static string CutFingerprint(PlanarCutInterface? cut)
        {
            if (cut is null) return "null";
            return string.Join(";", new[]
            {
                cut.Id,
                cut.Kind.ToString(),
                string.Join(",", (cut.Geometry?.Points ?? new List<PlanarPoint2D>()).Select(point =>
                    $"{Fmt(point.U)},{Fmt(point.V)}")),
                Fmt(cut.NormalFromFragmentToOmittedSide.X),
                Fmt(cut.NormalFromFragmentToOmittedSide.Y),
                Fmt(cut.NormalFromFragmentToOmittedSide.Z),
                cut.ModeByDof.ToString(),
                cut.MeshConstraintId ?? string.Empty
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

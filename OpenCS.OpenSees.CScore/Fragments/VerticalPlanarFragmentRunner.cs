using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;

namespace OpenCS.OpenSees.CScore.Fragments
{
    /// <summary>
    /// Оркестратор нелинейного расчёта фрагментов вертикальных стен в OpenSees:
    /// Gmsh-сетка -> cut-interface mapping -> ShellOpenSeesModel -> boundary actions по стадиям
    /// -> нелинейный прогон -> послойные состояния -> аудит СП 63.
    /// </summary>
    public class VerticalPlanarFragmentRunner
    {
        public async Task<VerticalPlanarFragmentResult> RunAsync(
            VerticalPlanarFragment fragment,
            IPlanarMesher mesher,
            PlanarMeshSettings meshSettings,
            Func<int, Material?> lookupMaterial,
            CalcType calcType,
            string openSeesExecutablePath,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fragment);
            ArgumentNullException.ThrowIfNull(mesher);
            ArgumentNullException.ThrowIfNull(lookupMaterial);

            var result = new VerticalPlanarFragmentResult { FragmentId = fragment.FragmentId };

            var cuts = new List<PlanarCutInterface>();
            if (fragment.BottomCut is not null) cuts.Add(fragment.BottomCut);
            if (fragment.TopCut is not null) cuts.Add(fragment.TopCut);
            cuts.AddRange(fragment.SideCuts);

            var constraintObjects = cuts
                .Where(c => c.BoundaryKey is null)
                .Select(c => c.CreateMeshConstraint())
                .ToList();

            PlanarMeshSnapshot snapshot = await mesher.BuildAsync(
                new PlanarMeshingRequest(fragment.Region, meshSettings, constraintObjects),
                cancellationToken);

            if (!snapshot.IsCalculable)
            {
                result.MeshDiagnostics = snapshot.Diagnostics.Select(d => d.Message).ToList();
                return result;
            }

            var cutMappings = new Dictionary<string, PlanarCutInterfaceMeshMapping>();
            var boundaryDiagnostics = new List<string>();
            foreach (var cut in cuts)
            {
                var mapped = PlanarCutInterfaceMeshMapper.Map(cut, snapshot);
                if (!mapped.IsCalculable || mapped.Mapping is null)
                {
                    boundaryDiagnostics.AddRange(mapped.Diagnostics.Select(d => $"{cut.Id}: {d.Message}"));
                    continue;
                }
                cutMappings[cut.Id] = mapped.Mapping;
            }

            if (boundaryDiagnostics.Count > 0)
            {
                result.BoundaryDiagnostics = boundaryDiagnostics;
                return result;
            }

            // Шаги 3-9 (модель, boundary actions по стадиям, реальный прогон, послойные
            // состояния, баланс, энергетика, аудит) реализуются в Task 5/6.
            return result;
        }
    }
}

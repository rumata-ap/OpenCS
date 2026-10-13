using CScore.Fem;
using CScore.PlateRebar;

namespace CScore.PlateStrip;

/// <summary>Результат построения фибрового сечения полосы.</summary>
/// <param name="Section">Сечение или null при блокирующей диагностике.</param>
/// <param name="Diagnostics">Диагностики (ошибки блокируют, предупреждения фиксируют допущения).</param>
public sealed record PlateStripFiberSectionResult(
    CrossSection? Section, IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => Section is not null && !Diagnostics.Any(d => d.IsError);
}

/// <summary>
/// Срез 8c: конвертация <see cref="PlateSection"/> в фибровое <see cref="CrossSection"/> полосы.
///
/// Вложение полосы (<see cref="StripKinematicEmbedding"/>) задаёт εy = γxy = κy = κxy = 0, поэтому
/// плитное сечение работает одноосно по x, а усилия [N, My, Mz] — интеграл σx(εx)·[1, z, −v]:
///  • бетон — NLayers слоёв по толщине (центры слоёв как у <see cref="PlateSection"/>) в каждой
///    точке квадратуры Гаусса по ширине; площадь волокна = вес квадратуры × толщина слоя;
///  • арматура Asx — точечные волокна на Zsx с площадью Asx·вес (бетон не вычитается: диаграмма
///    арматуры в <see cref="PlateSection"/> полная, а не разностная); Asy в εy = 0 вклада не даёт.
/// Координаты: Fiber.X = z плиты, Fiber.Y = v. В соглашении CScoreDefault OpenSees это Z и Y —
/// то есть изгиб плиты об ось y полосы становится изгибом сечения об ось y OpenSees.
/// </summary>
public static class PlateStripFiberSectionBuilder
{
    /// <summary>Построить сечение.</summary>
    /// <param name="section">Плитное сечение: толщина, слои, материалы, тип диаграммы.</param>
    /// <param name="layersPerWidthPoint">Армирование в каждой точке квадратуры по ширине
    /// (уже разрешённое из <see cref="PlateRebarField"/>); длина = число точек квадратуры.</param>
    /// <param name="widthM">Ширина полосы, м.</param>
    /// <param name="materials">Материалы проекта по Id.</param>
    /// <param name="calc">Вид расчёта.</param>
    public static PlateStripFiberSectionResult Build(
        PlateSection section,
        IReadOnlyList<IReadOnlyList<PlateRebarLayer>> layersPerWidthPoint,
        double widthM,
        IReadOnlyDictionary<int, Material> materials,
        CalcType calc,
        IReadOnlyList<double>? materialFractions = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(layersPerWidthPoint);
        ArgumentNullException.ThrowIfNull(materials);

        var diagnostics = new List<FemValidationDiagnostic>();
        PlateStripFiberSectionResult Fail(string code, string message)
        {
            diagnostics.Add(new(code, message));
            return new(null, diagnostics);
        }

        if (!(section.H > 0.0) || !double.IsFinite(section.H))
            return Fail("plate_strip_fiber_invalid_thickness", "Толщина плитного сечения должна быть положительной.");
        if (!(widthM > 0.0) || !double.IsFinite(widthM))
            return Fail("plate_strip_fiber_invalid_width", "Ширина полосы должна быть положительной.");
        if (layersPerWidthPoint.Count < 1)
            return Fail("plate_strip_fiber_no_width_points", "Не заданы точки квадратуры по ширине полосы.");

        if (!materials.TryGetValue(section.ConcreteMaterialId, out var concrete))
            return Fail("plate_strip_fiber_concrete_not_found",
                $"Материал бетона Id={section.ConcreteMaterialId} не найден.");

        AddModelDiagnostics(section, diagnostics);

        int n = layersPerWidthPoint.Count;
        var (vs, weights) = EquivalentSectionCalculator.WidthGaussPoints(widthM, n);
        int layerCount = section.NLayers < 1 ? 1 : section.NLayers;
        double dz = section.H / layerCount;
        double z0 = -section.H / 2.0 + dz / 2.0;

        // Бетон: тот же тип диаграммы, что у PlateSectionLiveResponse (запасной — L3).
        var concreteType = DiagrammCompatibility.Coerce(concrete.Type, section.ConcreteDiagramType);
        var concreteDiagrams = concrete.GetDiagramms(concreteType);
        if (concreteDiagrams is null || !concreteDiagrams.ContainsKey(calc))
            concreteType = DiagrammCompatibility.Coerce(concrete.Type, DiagrammType.L3);

        var concreteArea = new MaterialArea
        {
            Tag = "concrete",
            Category = AreaCategory.Region,
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = concreteType
        };
        // Доля материала в ячейке ширины: отверстие убирает соответствующую часть площади волокон.
        double Fraction(int g) => materialFractions is { } f && g < f.Count ? Math.Clamp(f[g], 0.0, 1.0) : 1.0;
        for (int g = 0; g < n; g++)
        for (int k = 0; k < layerCount; k++)
        {
            if (Fraction(g) <= 0.0) continue;
            concreteArea.Fibers.Add(new Fiber(x: z0 + k * dz, y: vs[g])
            {
                Area = weights[g] * Fraction(g) * dz,
                TypeFiber = FiberType.poly
            });
        }

        var areas = new List<MaterialArea> { concreteArea };

        // Арматура: области по материалу слоя; фибры — Asx·вес на Zsx.
        var rebarByMaterial = new SortedDictionary<int, List<Fiber>>();
        for (int g = 0; g < n; g++)
        {
            if (Fraction(g) <= 0.0) continue;
            foreach (var layer in layersPerWidthPoint[g])
            {
                if (layer.Asx <= 0.0) continue;
                int materialId = layer.MaterialId != 0 ? layer.MaterialId : section.RebarMaterialId;
                if (!rebarByMaterial.TryGetValue(materialId, out var list))
                    rebarByMaterial[materialId] = list = [];
                list.Add(new Fiber(x: layer.Zsx, y: vs[g])
                {
                    Area = layer.Asx * weights[g] * Fraction(g),
                    TypeFiber = FiberType.point
                });
            }
        }

        foreach (var (materialId, fibers) in rebarByMaterial)
        {
            if (!materials.TryGetValue(materialId, out var rebar))
                return Fail("plate_strip_fiber_rebar_not_found", $"Материал арматуры Id={materialId} не найден.");
            var rebarType = DiagrammCompatibility.Coerce(rebar.Type, DiagrammType.L2);
            areas.Add(new MaterialArea
            {
                Tag = $"rebar:{materialId}",
                Category = AreaCategory.RebarGroup,
                Material = rebar,
                MaterialId = rebar.Id,
                DiagrammType = rebarType,
                HostArea = concreteArea,
                HostAreaId = concreteArea.Id,
                Fibers = fibers
            });
        }

        return new(new CrossSection { Tag = "plate-strip-fiber", Areas = areas }, diagnostics);
    }

    static void AddModelDiagnostics(PlateSection section, List<FemValidationDiagnostic> diagnostics)
    {
        if (section.PoissonUncracked != 0.0)
            diagnostics.Add(new("plate_strip_fiber_poisson_ignored",
                "PoissonUncracked ≠ 0: одноосный фибровый стержень не воспроизводит поправку на ν — " +
                "сверка приближённая.", false));
        if (section.PlateModel is "char1d_axial" or "char1d_principal")
            diagnostics.Add(new("plate_strip_fiber_char1d_discretized",
                "Модель по характерным точкам заменена слоистой дискретизацией по NLayers — " +
                "сверка приближённая.", false));
    }
}

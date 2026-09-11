using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Валидационные тесты по разделам IV.Г «Внецентренное сжатие» и IV.Е «Внецентренное
/// растяжение» Пособия Краковского к СП 63.13330.2012 (папка "work\expert\norms\Пособие
/// Краковского СП63.13330.2012"): деформационная модель (<see cref="StrainSolver"/>) и
/// предельные усилия (<see cref="LimitForceSolver"/>) для прямоугольных сечений.
/// </summary>
public sealed class Sp63EccentricKrakovskyTests
{
    static Material Concrete(string tag, double rb, double rbt, double eb)
    {
        var m = new Material { Id = 200, Tag = tag, Type = MatType.Concrete, E = eb };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.Concrete, TypeCalc = calc,
            Fc = -rb, Ft = rbt, E = eb,
            // Значения деформаций 0,0015/0,0035 (сжатие) и 0,00008/0,00015 (растяжение) —
            // фиксированные по п. 6.1.20 СП63 для тяжёлого бетона, не зависят от класса
            // (см. Sp63BendingKrakovskyTests — тот же приём для B15).
            Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    // B25, γb1=0,9: Rb=14,5×0,9=13,05 МПа; Rbt=1,05×0,9=0,945 МПа; Eb=30 000 МПа
    // (Приложение, табл. П3 пособия). Используется в базе IV.Е.1.1/2.1/2.3/1.8.
    static Material ConcreteB25() => Concrete("B25", rb: 13_050.0, rbt: 945.0, eb: 30_000_000.0);

    // B30, γb1=1,0: Rb=17,0 МПа; Rbt=1,15 МПа; Eb=32 500 МПа (табл. П3). База IV.Г.5.1/6.1.
    static Material ConcreteB30() => Concrete("B30", rb: 17_000.0, rbt: 1_150.0, eb: 32_500_000.0);

    // A500: Rs=Rsc=435 МПа (табл. П4; скобочное значение 400 МПа — только для чисто
    // кратковременного загружения, здесь не тот случай), Es=200 000 МПа (сноска 9
    // Приложения — для классов A400–A1000 принимается Es=2,0×10^5 МПа).
    static Material RebarA500()
    {
        var m = new Material { Id = 201, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        MaterialChars Chars(CalcType calc) => new()
        {
            Type = MatType.ReSteelF, TypeCalc = calc,
            Fc = -435_000.0, Ft = 435_000.0, E = 200_000_000.0,
            Ec2 = -0.025, Et2 = 0.025,
        };
        m.MaterialChars = [Chars(CalcType.C), Chars(CalcType.CL), Chars(CalcType.N), Chars(CalcType.NL)];
        return m;
    }

    /// <summary>
    /// Прямоугольное сечение b×h с произвольным набором точечных групп арматуры
    /// (y, площадь) на оси x=0 — достаточно для одноосных (Mx) случаев IV.Е. Знаковая
    /// конвенция y — та же, что в <c>Sp63BendingKrakovskyTests</c>: положительный y —
    /// сторона, где по формулам пособия получается бОльшее растяжение.
    /// </summary>
    static CrossSection RectangularSection(
        double b, double h, Material concrete, Material rebar, params (double y, double area)[] groups)
    {
        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var concreteArea = new MaterialArea
        {
            Tag = "concrete", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concreteArea.SetWKT();
        concreteArea.SliceXY(nx: 8, ny: 80);

        var areas = new List<MaterialArea> { concreteArea };
        int i = 0;
        foreach (var (y, area) in groups)
        {
            var fiber = Fiber.CreatePoint(0.001, 0.0, y);
            fiber.Area = area;
            areas.Add(new MaterialArea
            {
                Tag = $"rebar_{i++}", Category = AreaCategory.RebarGroup,
                Material = rebar, MaterialId = rebar.Id,
                DiagrammType = DiagrammType.L2,
                Fibers = [fiber],
            });
        }

        var section = new CrossSection { Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    /// <summary>
    /// Прямоугольное сечение b×h с арматурой в четырёх углах (для двухосных случаев
    /// IV.Г.6.1 — нужны обе координаты, чтобы отдельно проверить изгиб в плоскости (Mx,
    /// по y) и из плоскости (My, по x)).
    /// </summary>
    static CrossSection RectangularSectionCornerBars(
        double b, double h, double cover, Material concrete, Material rebar, double asTotal)
    {
        double y0 = -h / 2.0, y1 = h / 2.0, x0 = -b / 2.0, x1 = b / 2.0;
        var concreteArea = new MaterialArea
        {
            Tag = "concrete", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Hull = new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "outer"),
        };
        concreteArea.SetWKT();
        concreteArea.SliceXY(nx: 8, ny: 100);

        double xr = b / 2.0 - cover, yr = h / 2.0 - cover;
        double areaPerBar = asTotal / 4.0;
        var corners = new (double x, double y)[] { (-xr, -yr), (xr, -yr), (xr, yr), (-xr, yr) };

        var areas = new List<MaterialArea> { concreteArea };
        int i = 0;
        foreach (var (x, y) in corners)
        {
            var fiber = Fiber.CreatePoint(0.001, x, y);
            fiber.Area = areaPerBar;
            areas.Add(new MaterialArea
            {
                Tag = $"rebar_{i++}", Category = AreaCategory.RebarGroup,
                Material = rebar, MaterialId = rebar.Id,
                DiagrammType = DiagrammType.L2,
                Fibers = [fiber],
            });
        }

        var section = new CrossSection { Areas = areas };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    // ---- IV.Е.1.8: центральное растяжение (b=500,h=300,a=a'=40, B25/A500) ----
    const double E_B = 0.500;
    const double E_H = 0.300;
    const double E_Cover = 0.040;
    const double E_YFace = E_H / 2.0 - E_Cover; // = 0,110 м

    /// <summary>
    /// Пример IV.Е.1.8: центральное растяжение, N=310 кН, M=0. As=8,04 см² (4⌀16),
    /// симметрично у обеих граней. Пособие («ручной» контроль): при центральном
    /// растяжении всё усилие воспринимает арматура, As,треб=N/Rs=7,16 см² &lt; 8,04 см²
    /// принятой — выполнено.
    /// </summary>
    [Fact]
    public void IVE1_8_CentralTension_SolverMatchesManualControl()
    {
        const double asTotal = 8.04e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));

        var solver = new StrainSolver(section, CalcType.C, ten: false, ca: true, tol: 0.05, maxIter: 100);
        var k = solver.Solve(nTarget: 310.0, mxTarget: 0.0, myTarget: 0.0);
        Assert.True(solver.Converged, $"Не сошлось: невязка={solver.Residual}");

        var load = section.Integral(k, CalcType.C, ten: false, ca: true);
        Assert.Equal(310.0, load.N, 1);
        Assert.Equal(0.0, load.Mx, 1);

        // Все усилие несёт арматура: N ≈ As·σs, где σs=e0·Es (деформация равномерна,
        // изгиб отсутствует по симметрии). Пособие: As,треб=N/Rs=7,16 см² < 8,04 см² —
        // сталь не должна дойти до текучести (запас по площади).
        double sigmaS = k.e0 * 200_000_000.0;
        Assert.True(sigmaS < 435_000.0, $"σs={sigmaS / 1000.0:F1} МПа должно быть меньше Rs=435 МПа (запас площади арматуры)");

        // Независимая проверка предельной несущей способности через LimitForceSolver:
        // Nult=As·Rs=8,04е-4×435е3=349,7 кН должно быть больше N=310 (тот же вывод,
        // что и сравнение As,треб=7,16 < As=8,04 в пособии).
        var limit = LimitForceSolver.ForCrossSection(section, CalcType.C).AxialFactor(n: 1.0, mx: 0.0, my: 0.0);
        Assert.True(limit.Converged);
        Assert.InRange(limit.NLimit, 349.7 * 0.97, 349.7 * 1.03);
        Assert.True(310.0 < limit.NLimit, $"N=310 должно быть меньше Nult={limit.NLimit}");
    }

    /// <summary>
    /// Пример IV.Е.2.1 (деформационная модель, база примеров IV.Е.1.1/4.1): внецентренное
    /// растяжение, продольная сила приложена ЗА пределами расстояния между
    /// равнодействующими в арматуре (e0=0,824 м &gt; h/2-a). As=19,63 см² (4⌀25),
    /// симметрично; N=97 кН, M=80 кН·м. Пособие: 1/ry=0,011903 м⁻¹; ε0=0,0007476;
    /// εb,max=0,0009; εs,max=0,0021 (обе меньше предельных) — выполнено.
    /// </summary>
    [Fact]
    public void IVE2_1_EccentricTensionBeyondRebarResultants_ManualAnswerSatisfiesEquilibrium()
    {
        const double asTotal = 19.63e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));

        var manual = new Kurvature { e0 = 0.0007476, ky = 0.011903, kz = 0.0 };
        var manualLoad = section.Integral(manual, CalcType.C, ten: false, ca: true);

        Assert.InRange(manualLoad.N, 95.0, 99.0);
        Assert.InRange(manualLoad.Mx, 78.0, 82.0);
    }

    /// <summary>Тот же пример IV.Е.2.1: независимая сходимость <see cref="StrainSolver"/>
    /// в пределах, допускаемых нормой (εb,ult=0,0035, εs,ult=0,025).</summary>
    [Fact]
    public void IVE2_1_EccentricTensionBeyondRebarResultants_SolverConvergesWithinLimits()
    {
        const double asTotal = 19.63e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));
        var solver = new StrainSolver(section, CalcType.C, ten: false, ca: true, tol: 0.05, maxIter: 100);

        var k = solver.Solve(nTarget: 97.0, mxTarget: 80.0, myTarget: 0.0);
        Assert.True(solver.Converged, $"Не сошлось: невязка={solver.Residual}");

        var load = section.Integral(k, CalcType.C, ten: false, ca: true);
        Assert.Equal(97.0, load.N, 1);
        Assert.Equal(80.0, load.Mx, 1);

        double epsTension = k.e0 + k.ky * E_YFace;
        double epsOther = k.e0 + k.ky * (-E_YFace);
        Assert.True(epsTension < 0.025, $"εs,max={epsTension:G4} должна быть меньше 0,025");
        Assert.True(Math.Abs(epsOther) < 0.0035, $"|ε| у другой грани={Math.Abs(epsOther):G4} должно быть меньше 0,0035");
    }

    /// <summary>
    /// Пример IV.Е.2.3 (деформационная модель, база примеров IV.Е.1.7/4.3): внецентренное
    /// растяжение, продольная сила приложена МЕЖДУ равнодействующими в арматуре
    /// (e0=0,082 м &lt; h/2-a). As=4,52 см² (4⌀12), симметрично; N=97 кН, M=8,0 кН·м.
    /// Пособие: 1/ry=0,0073072 м⁻¹; ε0=0,0010721; бетон растянут (не ограничен, здесь не
    /// учитывается — ten:false), εs,max=0,0019 &lt; 0,025 — выполнено.
    /// </summary>
    [Fact]
    public void IVE2_3_EccentricTensionBetweenRebarResultants_ManualAnswerSatisfiesEquilibrium()
    {
        const double asTotal = 4.52e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));

        var manual = new Kurvature { e0 = 0.0010721, ky = 0.0073072, kz = 0.0 };
        var manualLoad = section.Integral(manual, CalcType.C, ten: false, ca: true);

        Assert.InRange(manualLoad.N, 95.0, 99.0);
        Assert.InRange(manualLoad.Mx, 6.0, 10.0);
    }

    /// <summary>Тот же пример IV.Е.2.3: независимая сходимость <see cref="StrainSolver"/>.
    /// Обе грани здесь растянуты (продольная сила между равнодействующими в арматуре) —
    /// проверяется только предельная деформация арматуры.</summary>
    [Fact]
    public void IVE2_3_EccentricTensionBetweenRebarResultants_SolverConvergesWithinLimits()
    {
        const double asTotal = 4.52e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));
        var solver = new StrainSolver(section, CalcType.C, ten: false, ca: true, tol: 0.05, maxIter: 100);

        var k = solver.Solve(nTarget: 97.0, mxTarget: 8.0, myTarget: 0.0);
        Assert.True(solver.Converged, $"Не сошлось: невязка={solver.Residual}");

        var load = section.Integral(k, CalcType.C, ten: false, ca: true);
        Assert.Equal(97.0, load.N, 1);
        Assert.Equal(8.0, load.Mx, 1);

        double epsNear = k.e0 + k.ky * E_YFace;
        double epsFar = k.e0 + k.ky * (-E_YFace);
        Assert.True(epsNear > 0.0 && epsFar > 0.0, "Обе грани должны быть растянуты (N приложена между равнодействующими)");
        Assert.True(Math.Max(epsNear, epsFar) < 0.025, "εs,max должна быть меньше 0,025");
    }

    /// <summary>
    /// Пример IV.Е.4.1 (предельные усилия, «ручной» контроль по СНиП 2.03.01-84* —
    /// формальный x по формуле (8.25) [1] отрицателен): та же геометрия и силы, что в
    /// IV.Е.2.1. Пособие проверяет равновесие относительно сжатой грани и получает
    /// Mult=103,0 кН·м &gt; Mact=94,5 кН·м. Это другая точка отсчёта момента (от грани, а
    /// не от центра тяжести, как MxLimit у <see cref="LimitForceSolver"/>), поэтому точные
    /// числа не сравниваются — проверяется только совпадающий вердикт «Выполнено»
    /// (M=80 кН·м относительно центра тяжести должно быть меньше найденного предела).
    /// </summary>
    [Fact]
    public void IVE4_1_LimitForceSolver_VerdictMatchesManualControl()
    {
        const double asTotal = 19.63e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));

        var result = LimitForceSolver.ForCrossSection(section, CalcType.C).MomentFactor(n: 97.0, mx: 1.0, my: 0.0);

        Assert.True(result.Converged);
        Assert.True(80.0 < result.MxLimit, $"M=80 должно быть меньше Mult={result.MxLimit}");
    }

    /// <summary>
    /// Пример IV.Е.4.3 (предельные усилия, «ручной» контроль по формулам (8.20)-(8.23)):
    /// та же геометрия и силы, что в IV.Е.2.3. Пособие проверяет ДВА условия относительно
    /// равнодействующих в каждом ряду арматуры отдельно (Ne=2,72 &lt; Mult=21,63 и
    /// Ne'=18,62 &lt; M'ult=21,63) — качественно другая структура проверки, чем единая кривая
    /// несущей способности LimitForceSolver. Проверяется совпадающий вердикт «Выполнено».
    /// </summary>
    [Fact]
    public void IVE4_3_LimitForceSolver_VerdictMatchesManualControl()
    {
        const double asTotal = 4.52e-4;
        var section = RectangularSection(E_B, E_H, ConcreteB25(), RebarA500(),
            (E_YFace, asTotal / 2.0), (-E_YFace, asTotal / 2.0));

        var result = LimitForceSolver.ForCrossSection(section, CalcType.C).MomentFactor(n: 97.0, mx: 1.0, my: 0.0);

        Assert.True(result.Converged);
        Assert.True(8.0 < result.MxLimit, $"M=8 должно быть меньше Mult={result.MxLimit}");
    }

    // ---- IV.Г.6.1: внецентренное сжатие, прямоугольное сечение (B30/A500) ----
    // b=400,h=500,a=a'=40 мм; симметричная арматура 4⌀28 (As=24,63 см²) по углам.
    // Усилия определены расчётом по ДЕФОРМИРОВАННОЙ схеме (с учётом влияния прогиба) —
    // поэтому N, M ниже уже являются итоговыми расчётными усилиями, доп. усиление по
    // формуле (8.1.15) СП63 (EccentricityAmplifier) не требуется. Сечение дважды
    // симметрично (арматура и бетон), поэтому знаковая конвенция y/x, в отличие от
    // Sp63BendingKrakovskyTests, здесь не имеет значения для модуля результата.
    const double G_B = 0.400;
    const double G_H = 0.500;
    const double G_Cover = 0.040;
    const double G_AsTotal = 24.63e-4;
    const double G_N = -1700.0; // сжатие — отрицательное по конвенции проекта

    /// <summary>
    /// Пример IV.Г.6.1, расчёт В ПЛОСКОСТИ изгиба (большая сторона h=500 работает как
    /// рабочая высота): N=1700 кН (сжатие), M=382 кН·м — это ИСХОДНЫЙ момент ОТНОСИТЕЛЬНО
    /// ЦЕНТРА ТЯЖЕСТИ сечения (e0=M/N=0,225 м — пособие прямо называет это «эксцентриситетом
    /// относительно центра тяжести сечения»). Формулы (8.10)-(8.13) СП63 внутри себя
    /// пересчитывают этот момент на ДРУГУЮ точку отсчёта — центр тяжести РАСТЯНУТОЙ
    /// арматуры: e=e0+(h0-a')/2=0,435 м, M=Ne=739,5 кН·м, и Mult=772,59/771,31 кН·м ТОЖЕ
    /// относительно этой точки, а не относительно центра тяжести сечения.
    ///
    /// `LimitForceSolver.MomentFactor` считает Mx относительно ЦЕНТРА ТЯЖЕСТИ СЕЧЕНИЯ (тот
    /// же базис, что и исходное M=382, а не «сдвинутые» 739,5/772,59 пособия). Чтобы
    /// сравнить с пособием корректно, нужно либо сравнивать M=382 с MxLimit напрямую (оба
    /// относительно ц.т. сечения), либо привести Mult пособия к тому же базису вычитанием
    /// N·(h0-a')/2: `Mult(ц.т. сечения) = 771,31 - 1700×0,21 = 414,31 кН·м`. Оба способа
    /// дают одно и то же: `LimitForceSolver` находит `MxLimit≈414 кН·м`, что совпадает с
    /// приведённым к тому же базису Mult пособия в пределах 0,05% — то есть OpenCS полностью
    /// СОГЛАСУЕТСЯ с пособием, а не расходится с ним (более ранняя версия этого теста
    /// ошибочно сравнивала MxLimit относительно ц.т. сечения напрямую с M=739,5/Mult=772,59
    /// пособия относительно ц.т. арматуры — те же числа, но в разных точках отсчёта, отсюда
    /// и казавшееся расхождение почти в 2 раза).
    /// </summary>
    [Fact]
    public void IVG6_1_EccentricCompressionInPlane_MatchesManualControlOnceReferencePointReconciled()
    {
        var section = RectangularSectionCornerBars(G_B, G_H, G_Cover, ConcreteB30(), RebarA500(), G_AsTotal);
        double h0 = G_H - G_Cover, aPrime = G_Cover;
        double armToTensionSteel = (h0 - aPrime) / 2.0; // = 0,21 м

        var result = LimitForceSolver.ForCrossSection(section, CalcType.C).MomentFactor(n: G_N, mx: 1.0, my: 0.0);

        Assert.True(result.Converged);
        Assert.True(382.0 < result.MxLimit, $"M=382 (относительно ц.т. сечения) должно быть меньше Mult={result.MxLimit}");

        double multAboutTensionSteel = result.MxLimit + 1700.0 * armToTensionSteel;
        Assert.InRange(multAboutTensionSteel, 771.31 * 0.98, 771.31 * 1.02);
    }

    /// <summary>
    /// Независимая перепроверка IV.Г.6.1 (в плоскости) в обход <see cref="LimitForceSolver"/>:
    /// вручную, напрямую через <see cref="CrossSection.Integral"/>, ищем кривизну ky (при
    /// зафиксированной εb=εb,ult=−0,0035 у сжатой грани), дающую N=−1700, методом бисекции —
    /// без использования кода самого LimitForceSolver. Подтверждает то же значение Mx≈414
    /// кН·м (относительно ц.т. сечения), что и LimitForceSolver — то есть найденное
    /// согласие с пособием (см. предыдущий тест) не зависит от конкретной реализации
    /// LimitForceSolver.
    /// </summary>
    [Fact]
    public void IVG6_1_EccentricCompressionInPlane_IndependentBisectionConfirmsSameUltimateMoment()
    {
        var section = RectangularSectionCornerBars(G_B, G_H, G_Cover, ConcreteB30(), RebarA500(), G_AsTotal);
        const double epsTopUltimate = -0.0035;
        double yTop = -G_H / 2.0;

        double NAt(double ky)
        {
            double e0 = epsTopUltimate - ky * yTop;
            var k = new Kurvature { e0 = e0, ky = ky, kz = 0.0 };
            return section.Integral(k, CalcType.C, ten: true, ca: true).N;
        }

        // При зафиксированной εb,top=εb,ult рост ky сужает зону сжатия — N растёт
        // (становится менее отрицательным). Ищем перемену знака (N=-1700) бисекцией.
        double lo = 0.001, hi = 0.05;
        Assert.True(NAt(lo) < -1700.0 && NAt(hi) > -1700.0,
            $"Ожидался переход через N=-1700 на границах: N({lo})={NAt(lo):F1}, N({hi})={NAt(hi):F1}");
        for (int i = 0; i < 60; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (NAt(mid) < -1700.0) lo = mid; else hi = mid;
        }
        double kyFinal = (lo + hi) / 2.0;
        double e0Final = epsTopUltimate - kyFinal * yTop;
        var kFinal = new Kurvature { e0 = e0Final, ky = kyFinal, kz = 0.0 };
        var load = section.Integral(kFinal, CalcType.C, ten: true, ca: true);

        Assert.InRange(load.N, -1701.0, -1699.0);
        Assert.InRange(load.Mx, 410.0, 418.0);
    }

    /// <summary>
    /// Пример IV.Г.6.1, расчёт ИЗ ПЛОСКОСТИ изгиба (меньшая сторона b=400 работает как
    /// рабочая высота, учитывается только случайный эксцентриситет). Та же особенность
    /// точки отсчёта момента, что и в плоскости (см. предыдущий тест): пособие «по
    /// программе» получает M=294,67 кН·м, Mult=592,44 кН·м относительно ц.т. РАСТЯНУТОЙ
    /// арматуры на этой стороне (арм=(b-a-a')/2=0,16 м). `LimitForceSolver.MyLimit`
    /// относительно ц.т. сечения — приведение к базису пособия:
    /// `592,44 - 1700×0,16 = 320,44 кН·м`, что совпадает с `MyLimit≈320 кН·м` в пределах
    /// 0,2% — снова полное согласие, а не расхождение.
    /// </summary>
    [Fact]
    public void IVG6_1_EccentricCompressionOutOfPlane_MatchesManualControlOnceReferencePointReconciled()
    {
        var section = RectangularSectionCornerBars(G_B, G_H, G_Cover, ConcreteB30(), RebarA500(), G_AsTotal);
        double b0 = G_B - G_Cover, aPrime = G_Cover;
        double armToTensionSteel = (b0 - aPrime) / 2.0; // = 0,16 м

        var result = LimitForceSolver.ForCrossSection(section, CalcType.C).MomentFactor(n: G_N, mx: 0.0, my: 1.0);

        Assert.True(result.Converged);

        double multAboutTensionSteel = result.MyLimit + 1700.0 * armToTensionSteel;
        Assert.InRange(multAboutTensionSteel, 592.44 * 0.98, 592.44 * 1.02);
    }
}

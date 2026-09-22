using System;
using System.Linq;
using Xunit;
using CScore;
using CScore.Fem;

namespace CScore.Tests;

// Момент трещинообразования плитного сечения по ДЕФОРМАЦИОННОЙ МОДЕЛИ, п. 8.2.14 СП 63:
//
//   «Определение момента образования трещин на основе нелинейной деформационной модели
//    производят исходя из общих положений 6.1.24 и 8.1.20–8.1.30, но с учётом работы бетона
//    в растянутой зоне… Значение M_crc определяют из решения системы уравнений 8.1.20–8.1.30,
//    принимая относительную деформацию бетона у растянутой грани элемента равной предельному
//    значению относительной деформации бетона при растяжении ε_bt,ult согласно 8.1.30.»
//
// П. 8.2.8 делает этот путь ОСНОВНЫМ, а Wpl = γ·Wred (п. 8.2.10–8.2.12) — лишь допускаемым
// упрощением для прямоугольных, тавровых и двутавровых сечений. Слоистая модель обязана
// пользоваться основным путём, иначе в неё затягивается эмпирический γ.
//
// Сечение: стена h = 200 мм, B25, A500, ⌀12 шаг 100 у обеих граней, привязка 35 мм.
public class ShellCrackingSolverTests
{
    const double H = 0.2, Cover = 0.035, As = 1131e-6;

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs, double rsc) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rsc, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
    };

    static Material Concrete()
    {
        var m = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.C = ConcreteChars(CalcType.C, 14_500.0, 1_050.0);
        m.CL = ConcreteChars(CalcType.CL, 14_500.0, 1_050.0);
        m.N = ConcreteChars(CalcType.N, 18_500.0, 1_550.0);
        m.NL = ConcreteChars(CalcType.NL, 18_500.0, 1_550.0);
        return m;
    }

    static Material Rebar()
    {
        var m = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.C = RebarChars(CalcType.C, 435_000.0, 400_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0, 400_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0, 500_000.0);
        return m;
    }

    static PlateSection Section() => new()
    {
        H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        TensionConcrete = false,   // важно: решатель обязан включать растянутую ветвь сам
        RebarLayers =
        [
            new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = As, Asy = As,
                Zsx = -(H / 2 - Cover), Zsy = -(H / 2 - Cover), DiameterX = 0.012, DiameterY = 0.012 },
            new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = As, Asy = As,
                Zsx = H / 2 - Cover, Zsy = H / 2 - Cover, DiameterX = 0.012, DiameterY = 0.012 },
        ],
    };

    static ShellCrackingSolver Solver()
    {
        var c = Concrete();
        var r = Rebar();
        return new ShellCrackingSolver(
            Section(),
            c.GetDiagramms(DiagrammType.L3)![CalcType.N],
            r.GetDiagramms(DiagrammCompatibility.Coerce(r.Type, DiagrammType.L2))![CalcType.N]);
    }

    // Предел растяжения — из диаграммы бетона, как в CrackingSolver.TensionLimit.
    [Fact]
    public void TensionLimit_ComesFromConcreteDiagram()
    {
        double limit = Solver().TensionLimit();

        Assert.InRange(limit, 1e-5, 1e-3);
        Assert.Equal(Concrete().GetDiagramms(DiagrammType.L3)![CalcType.N].It.X.Max(), limit, 12);
    }

    // Определяющая проверка: при найденном M_crc деформация крайнего растянутого волокна
    // обязана равняться ε_bt,ult. Эталон считается независимо — повторным решением 6×6.
    [Fact]
    public void Mcrc_IsMomentWhereExtremeFibreReachesTensileLimit()
    {
        var solver = Solver();
        var res = solver.Solve([0, 0, 0, 40.0, 0, 0], alongX: true);

        Assert.True(res.Converged, "решатель не нашёл момент трещинообразования");
        Assert.True(res.Mcrc > 0.0);

        // Независимая проверка: решаем ту же задачу при M = M_crc с растянутым бетоном.
        var c = Concrete();
        var r = Rebar();
        var check = new ShellStrainSolver(Section(),
                c.GetDiagramms(DiagrammType.L3)![CalcType.N],
                r.GetDiagramms(DiagrammCompatibility.Coerce(r.Type, DiagrammType.L2))![CalcType.N],
                tensionOverride: true)
            .Solve([0, 0, 0, res.Mcrc, 0, 0]);

        Assert.True(check.Converged);
        double epsMax = Math.Max(check.StrainState.EpsX(H / 2), check.StrainState.EpsX(-H / 2));
        Assert.Equal(solver.TensionLimit(), epsMax, 6);
    }

    // Деформационный M_crc выше формульного при γ = 1,3: у стержневых сечений это уже
    // закреплено (CrackingMomentGammaTests — отношение 0,70…0,75), и для пластины ожидается
    // тот же порядок. Формульное значение считается здесь же, как в ShellLayeredCrackWidth.
    [Fact]
    public void DeformationMcrc_ExceedsFormulaMcrcWithGamma13()
    {
        var res = Solver().Solve([0, 0, 0, 40.0, 0, 0], alongX: true);
        Assert.True(res.Converged);

        const double h0 = H - Cover, aPrime = Cover;
        ShellSimplSolver.FullSectionProps(H, h0, aPrime, As, 0.0,
            200_000_000.0 / 30_000_000.0, out double aRed, out double iRed);
        double sRed = H * H / 2.0 + 200_000_000.0 / 30_000_000.0 * As * h0;
        double yt = H - sRed / aRed;
        double mcrcFormula = 1_550.0 * 1.3 * (iRed / yt);

        Assert.InRange(mcrcFormula / res.Mcrc, 0.65, 0.85);
    }

    // Продольная сила смещает момент трещинообразования: растяжение часть предельной
    // деформации выбирает само, обжатие — наоборот, откладывает трещинообразование.
    // Инвариант физический и от способа поиска не зависит.
    [Fact]
    public void AxialForce_ShiftsCrackingMoment()
    {
        var free    = Solver().Solve([0, 0, 0, 40.0, 0, 0], alongX: true);
        var pulled  = Solver().Solve([200.0, 0, 0, 40.0, 0, 0], alongX: true);
        var pressed = Solver().Solve([-200.0, 0, 0, 40.0, 0, 0], alongX: true);

        Assert.True(free.Converged, $"N=0: {free.Description}");
        Assert.True(pulled.Converged, $"N=+200: {pulled.Description}");
        Assert.True(pressed.Converged, $"N=-200: {pressed.Description}");
        Assert.True(pulled.Mcrc < free.Mcrc, $"растяжение не снизило Mcrc: {pulled.Mcrc:F3} против {free.Mcrc:F3}");
        Assert.True(pressed.Mcrc > free.Mcrc, $"обжатие не повысило Mcrc: {pressed.Mcrc:F3} против {free.Mcrc:F3}");

        // Опорное значение для этого сечения при чистом изгибе: M_crc = 20,05 кН·м/м.
        // Формульный при γ = 1,3 даёт 14,39, то есть 0,718 деформационного — тот же порядок,
        // что закреплён для стержневых сечений в CrackingMomentGammaTests (0,70…0,75).
        Assert.InRange(free.Mcrc, 20.0, 20.1);
    }

    // Поиск идёт по лучу всех трёх моментов, критерий — главная деформация грани: кручение
    // растягивает грань наравне с изгибом. Порог по одному Mx пропускал трещину на стене с
    // заметным Mxy (элемент 244, ShellSimplLiraWall244Tests), где длительное сочетание имеет
    // Mx ниже одноосного M_crc, но с кручением сечение уже с трещиной.
    [Fact]
    public void Twisting_LowersCrackingFactor_AlongMomentRay()
    {
        var bending = Solver().Solve([0, 0, 0, 17.0, 0, 0], alongX: true);
        var twisted = Solver().Solve([0, 0, 0, 17.0, 0, 9.0], alongX: true);

        Assert.True(bending.Converged && twisted.Converged);
        Assert.True(bending.MomentFactor > 1.0, $"без кручения k_crc = {bending.MomentFactor:F3}");
        Assert.True(twisted.MomentFactor < 1.0, $"с кручением k_crc = {twisted.MomentFactor:F3}");
        Assert.Equal(twisted.MomentFactor * 17.0, twisted.Mcrc, 9);

        // Найденное состояние — на пороге по главной деформации, а не по εx.
        var s = twisted.StrainState!;
        double e1Max = double.NegativeInfinity;
        foreach (double z in new[] { H / 2, -H / 2 })
        {
            PlateSection.PrincipalStrains2D(s.EpsX(z), s.EpsY(z), s.GammaXY(z), out double e1, out _, out _);
            e1Max = Math.Max(e1Max, e1);
        }
        Assert.Equal(Solver().TensionLimit(), e1Max, 6);
    }

    // Сечение, обжатое почти до предела (N = −4800 при N_ult ≈ −4830 кН/м в обоих
    // направлениях): при росте момента равновесие теряется раньше, чем растянутая грань
    // доходит до ε_bt,ult. Поиск не должен выдавать потерю равновесия за трещину. Случай
    // редкий: с площадкой сжатой ветви L3 сечение успевает повернуться, и даже при
    // N = −4500 грань доходит до ε_bt,ult раньше (k ≈ 5,4 при M = 5).
    [Fact]
    public void HeavyCompression_StrengthGovernsBeforeCracking()
    {
        var res = Solver().Solve([-4800.0, -4800.0, 0, 1.0, 0, 0], alongX: true);

        Assert.True(res.Converged, res.Description);
        Assert.False(res.CrackingReached, $"k = {res.MomentFactor:F3}, ε = {res.MaxTensileStrain:E3}");
        Assert.True(res.MaxTensileStrain < 0.0, $"грань растянута: ε = {res.MaxTensileStrain:E3}");
        Assert.True(res.MomentFactor > 1.0, $"k = {res.MomentFactor:F4}");

        var bending = Solver().Solve([0, 0, 0, 40.0, 0, 0], alongX: true);
        Assert.True(bending.CrackingReached);
    }

    // Кэширующий пробник: те же усилия — тот же результат без повторного поиска, другие
    // усилия — новый поиск с тем же результатом, что у прямого Solve.
    [Fact]
    public void CachedProbe_ReusesResultForSameForces()
    {
        var probe = Solver().CachedProbe();
        double[] a = [0, 0, 0, 17.0, 0, 9.0];

        var first = probe(a, true);
        var again = probe((double[])a.Clone(), true);
        var other = probe([0, 0, 0, 17.0, 0, 0], true);

        Assert.Same(first, again);
        Assert.NotSame(first, other);
        Assert.Equal(Solver().Solve(a, true).MomentFactor, first!.MomentFactor, 12);
    }

    // Чистое кручение трещит, хотя Mx = 0: M_crc направления тогда 0, а признак трещины несёт
    // множитель k_crc.
    [Fact]
    public void PureTwisting_Cracks_WithZeroDirectionMoment()
    {
        var res = Solver().Solve([0, 0, 0, 0, 0, 40.0], alongX: true);

        Assert.True(res.Converged, res.Description);
        Assert.InRange(res.MomentFactor, 1e-3, 1.0);
        Assert.Equal(0.0, res.Mcrc);
    }
}

using CScore;
using CSfea.Core;

namespace CSfea.CScoreBridge.Structural;

/// <summary>Как трещина выключает растянутый бетон слоистого сечения пластины.</summary>
public enum PlateCrackRule
{
    /// <summary>
    /// Послойно: растяжение теряет только слой с трещиной (ε₁ &gt; ε_bt,ult); нетреснувшие растянутые слои над ней
    /// работают по диаграмме. Вместе с ψs работа бетона между трещинами учитывается дважды.
    /// </summary>
    Layer,

    /// <summary>
    /// Сечение с трещиной (как стержневой НДМ по п. 8.2.32 СП 63): первая трещина в любом слое делает треснувшими все
    /// слои — растянутый бетон сечения не учитывается, ν = 0 (как у слоя с трещиной), работу бетона между трещинами
    /// даёт только ψs арматуры.
    /// </summary>
    Section,
}

/// <summary>
/// Секущий закон оболочечного КЭ по слоистому сечению <see cref="PlateSection"/> (одна точка — центр КЭ): память
/// трещин слоёв (<see cref="PlateLayerState"/>, трещина необратима — слой больше не работает на растяжение, ν
/// выключается), εs,crc при первой трещине (<see cref="PlateSection.ActivatePsi"/>) и ψs арматуры (п. 8.2.32 СП 63),
/// секущая ABD — <see cref="SecantLaminateBuilder"/>. Напряжения — от полных деформаций, поэтому слой за ε_bt,ult
/// не держит Rbt («пластилина» шагового процессора нет). Состояние — своё на каждый КЭ; сечение и диаграммы — только
/// на чтение (можно делить между КЭ и потоками).
/// </summary>
public sealed class PlateSecantShellState : ISecantShellState
{
    private readonly PlateSection _section;
    private readonly PlateSectionMaterials _m;
    private readonly PlateLayerState _layers;
    private readonly bool _psi;
    private readonly double[,] _as;
    private readonly double _concreteLimit;
    private readonly PlateCrackRule _rule;
    private readonly double _band;

    /// <param name="section">Слоистое сечение (<c>PlateModel = "layered"</c>) с нужными TensionConcrete и ν.</param>
    /// <param name="materials">Диаграммы; <see cref="PlateSectionMaterials.ConcreteE_MPa"/> — для упругой As.</param>
    /// <param name="psi">Учитывать ψs арматуры у трещин.</param>
    /// <param name="rule">Правило выключения растянутого бетона трещиной.</param>
    /// <param name="zeroStrainBand">Полоса регуляризации секущей бетона без растяжения у нуля
    /// (<see cref="SecantLaminateBuilder.Build"/>).</param>
    public PlateSecantShellState(PlateSection section, PlateSectionMaterials materials, bool psi,
        PlateCrackRule rule = PlateCrackRule.Layer, double zeroStrainBand = 0.0)
    {
        _rule = rule;
        _band = zeroStrainBand;
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _m = materials ?? throw new ArgumentNullException(nameof(materials));
        _layers = PlateLayerState.For(section);
        _psi = psi;
        _as = materials.AsOverride ?? section.BuildAs(materials.ConcreteE_MPa, materials.Nu, materials.KShear);
        _concreteLimit = materials.ConcreteDiagram.Ic.X.Min();
        Initial = Tangent(new ShellStrainState(0, 0, 0, 0, 0, 0));
        Response = new SecantShellResponse(Initial);
    }

    public SecantShellResponse Response { get; }
    public ShellTangent Initial { get; }

    /// <summary>Память трещин КЭ (пробное состояние).</summary>
    public PlateLayerState Layers => _layers;

    public SecantShellEvaluation Evaluate(double[] epsM, double[] kappa, double[] gamma)
    {
        var s = State(epsM, kappa);
        var upd = _section.UpdateCracks(_layers, s, _m.ConcreteDiagram);
        if (_rule == PlateCrackRule.Section && _layers.CrackedCount is > 0 and var cracked && cracked < _layers.ConcreteLayerCount)
        {
            for (int i = 0; i < _layers.ConcreteLayerCount; i++) _layers.MarkCracked(i);
            upd = _section.UpdateCracks(_layers, s, _m.ConcreteDiagram);   // ψs ждёт вся арматура сечения
        }
        if (_psi && upd.PendingPsi.Count > 0)
        {
            // Соотношение усилий для M_crc — по секущим усилиям S·ε: они уравновешены решением, а истинные усилия
            // при деформациях итерации — нет (у полосы без продольной связи появляется ложная Nx, и εs,crc зависит от
            // пути итераций).
            var f = Response.Forces(epsM, kappa, gamma);
            double n = UnitScale.ShellForce, m = UnitScale.ShellMoment;
            _section.ActivatePsi(_layers, upd.PendingPsi, [f.N[0] / n, f.N[1] / n, f.N[2] / n, f.M[0] / m, f.M[1] / m,
                f.M[2] / m], _m.ConcreteDiagram, _m.RebarDiagram);
        }
        return new SecantShellEvaluation(Tangent(s), Status(s));
    }

    public ShellForces TrueForces(double[] epsM, double[] kappa, double[] gamma)
    {
        var r = Compute(State(epsM, kappa));
        var q = new double[2];
        for (int i = 0; i < 2; i++)
            q[i] = (_as[i, 0] * gamma[0] + _as[i, 1] * gamma[1]) * UnitScale.ShellForce;
        return new ShellForces(
            [r.Nx * UnitScale.ShellForce, r.Ny * UnitScale.ShellForce, r.Nxy * UnitScale.ShellForce],
            [r.Mx * UnitScale.ShellMoment, r.My * UnitScale.ShellMoment, r.Mxy * UnitScale.ShellMoment], q);
    }

    public void Commit() => _layers.Commit();
    public void Revert() => _layers.Revert();

    private static ShellStrainState State(double[] e, double[] k) => new(e[0], e[1], e[2], k[0], k[1], k[2]);

    private ShellResult Compute(ShellStrainState s)
        => _section.Compute(s, _m.ConcreteDiagram, _m.RebarDiagram, _m.LayerDiagrams, computeStiffness: false,
                            layerState: _layers);

    private ShellTangent Tangent(ShellStrainState s)
        => SecantLaminateBuilder.ToCsfea(SecantLaminateBuilder.Build(_section, s, _m.ConcreteDiagram, _m.RebarDiagram,
            _m.LayerDiagrams, _layers, zeroStrainBand: _band), _as);

    /// <summary>Трещина — хотя бы один слой; текучесть — растянутая арматура на площадке диаграммы; отказ —
    /// бетон за εb2 или арматура за εs2.</summary>
    private SecantSectionStatus Status(ShellStrainState s)
    {
        bool failed = false, yielded = false;
        int nl = _section.NLayers < 1 ? 1 : _section.NLayers;
        for (int i = 0; i < nl && !failed; i++)
            if (_section.EvaluateConcreteLayer(i, s, _m.ConcreteDiagram, null, _layers).Eps2Eq < _concreteLimit) failed = true;
        for (int li = 0; li < _section.RebarLayers.Count; li++)
            foreach (bool alongX in new[] { true, false })
            {
                var d = _m.LayerDiagrams is { } ld && li < ld.Count && ld[li] is { } l ? l : _m.RebarDiagram;
                var r = _section.EvaluateRebar(li, alongX, s, _m.RebarDiagram, _m.LayerDiagrams, _layers);
                if (r.Area <= 0.0) continue;
                var (yieldStrain, ultimate) = RebarLimits(d);
                if (r.Eps > ultimate) failed = true;
                if (r.Eps >= yieldStrain) yielded = true;
            }
        return new SecantSectionStatus(_layers.CrackedCount > 0, yielded, failed, _layers.CrackedCount);
    }

    /// <summary>Деформация начала площадки текучести (σ ≥ 0,995·σ(εs2)) и εs2 растянутой ветви.</summary>
    internal static (double Yield, double Ultimate) RebarLimits(Diagramm d)
    {
        double ult = d.It.X.Max();
        double sMax = d.Sig(ult, out _);
        var xs = d.It.X.Where(x => x > 0.0).OrderBy(x => x).ToArray();
        double y = xs.FirstOrDefault(x => d.Sig(x, out _) >= 0.995 * sMax, ult);
        return (y, ult);
    }
}

/// <summary>
/// Секущий закон стержневого КЭ по сечению CScore <see cref="CrossSection"/>: пофибровая связанная секущая 3×3 в
/// точках ξ = 0, ½, 1 (<see cref="SecantCrossSectionBuilder"/>), средняя податливость КЭ
/// (<see cref="BeamElements.MeanCompliance"/>). Трещина в точке необратима: до неё бетон работает на растяжение
/// (если разрешено), после — без растяжения и с ψs по εs,crc при M = M_crc (как в <see cref="TotalCurvatureSolver"/>).
/// Сечение без трещины (в т. ч. внецентренно сжатое) — законный случай, ψs к нему не применяется. Сечение CScore
/// мутирует фибры — вызовы последовательные; сечение можно делить между КЭ.
/// </summary>
public sealed class CrossSectionSecantBeamState : ISecantBeamState
{
    private readonly CrossSection _section;
    private readonly CalcType _calc;
    private readonly bool _tension;
    private readonly bool _psi;
    private readonly bool[] _cracked = new bool[3], _crackedCommitted = new bool[3];
    private readonly Dictionary<Fiber, double>?[] _epsCrc = new Dictionary<Fiber, double>?[3];
    private readonly Dictionary<Fiber, double>?[] _epsCrcCommitted = new Dictionary<Fiber, double>?[3];

    public CrossSectionSecantBeamState(CrossSection section, CalcType calc, double torsionGJ, bool tension, bool psi)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calc = calc;
        _tension = tension;
        _psi = psi;
        var (s0, _) = SecantCrossSectionBuilder.Build(section, new Kurvature(), calc, tension);
        Initial = SecantCrossSectionBuilder.ToCsfea(s0);
        Response = new SecantBeamResponse(Initial, torsionGJ);
    }

    public SecantBeamResponse Response { get; }
    public double[,] Initial { get; }

    /// <summary>Трещины в точках ξ = 0, ½, 1 (пробное состояние).</summary>
    public IReadOnlyList<bool> Cracked => _cracked;

    public SecantBeamEvaluation Evaluate(IReadOnlyList<(double Eps0, double KappaY, double KappaZ)> strains)
    {
        if (strains.Count != 3) throw new ArgumentException("Нужны деформации в трёх точках Лобатто.");
        var sp = new double[3][,];
        bool yielded = false, failed = false;
        for (int p = 0; p < 3; p++)
        {
            var k = new Kurvature { e0 = strains[p].Eps0, ky = strains[p].KappaY, kz = strains[p].KappaZ };
            if (!_cracked[p] && SecantCrossSectionBuilder.IsCracked(_section, k, _calc))
            {
                _cracked[p] = true;
                if (_psi)
                {
                    // Соотношение усилий — по секущим (уравновешенным) усилиям S·ε точки, не по истинным усилиям итерации.
                    var f = Response.Forces(k.e0, k.ky, k.kz);
                    _epsCrc[p] = SecantCrossSectionBuilder.EpsCrcAtCracking(_section, _calc, _calc,
                        f.N / UnitScale.Force, f.My / UnitScale.Moment, f.Mz / UnitScale.Moment);
                }
            }
            var (s, _) = SecantCrossSectionBuilder.Build(_section, k, _calc, Ten(p), epsCrcByFiber: Psi(p));
            sp[p] = SecantCrossSectionBuilder.ToCsfea(s);
            var (y, fl) = FiberStatus();
            yielded |= y;
            failed |= fl;
        }
        double[,] target;
        try { target = BeamElements.MeanCompliance(sp[0], sp[1], sp[2]); }
        catch (InvalidOperationException)
        {
            // Вырожденная матрица точки (например, сечение без жёсткости в одном направлении) — среднее арифметическое.
            target = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    target[i, j] = (sp[0][i, j] + 4 * sp[1][i, j] + sp[2][i, j]) / 6.0;
        }
        return new SecantBeamEvaluation(target, new SecantSectionStatus(_cracked.Any(c => c), yielded, failed, _cracked.Count(c => c)));
    }

    public BeamForces TrueForces(double xi, double eps0, double kappaY, double kappaZ)
    {
        int p = xi < 1.0 / 3.0 ? 0 : xi > 2.0 / 3.0 ? 2 : 1;
        var k = new Kurvature { e0 = eps0, ky = kappaY, kz = kappaZ };
        var load = _section.Integral(k, _calc, Ten(p), true);
        if (Psi(p) is { } map) load = Curvature8232.ApplyPsiCorrection(_section, k, load, map, _calc);
        return new BeamForces(UnitScale.ToCsfeaForce(load.N), UnitScale.ToCsfeaMoment(load.Mx),
            UnitScale.ToCsfeaMoment(load.My));
    }

    public void Commit()
    {
        Array.Copy(_cracked, _crackedCommitted, 3);
        Array.Copy(_epsCrc, _epsCrcCommitted, 3);
    }

    public void Revert()
    {
        Array.Copy(_crackedCommitted, _cracked, 3);
        Array.Copy(_epsCrcCommitted, _epsCrc, 3);
    }

    private bool Ten(int p) => _tension && !_cracked[p];
    private Dictionary<Fiber, double>? Psi(int p) => _psi && _cracked[p] ? _epsCrc[p] : null;

    /// <summary>По деформациям фибр последнего интегрирования: текучесть растянутой арматуры, отказ — бетон за εb2
    /// или арматура за εs2.</summary>
    private (bool Yielded, bool Failed) FiberStatus()
    {
        bool yielded = false, failed = false;
        foreach (var area in _section.Areas)
        {
            if (area.Material == null || !area.Diagramms.TryGetValue(_calc, out var d)) continue;
            bool concrete = area.Material.Type == MatType.Concrete;
            double cLimit = d.Ic.X.Min();
            var (yStrain, ult) = concrete ? (0.0, 0.0) : PlateSecantShellState.RebarLimits(d);
            foreach (var f in area.Fibers)
            {
                if (f.Eps < cLimit) failed = true;
                if (!concrete && f.Eps > ult) failed = true;
                if (!concrete && f.Eps >= yStrain) yielded = true;
            }
        }
        return (yielded, failed);
    }
}

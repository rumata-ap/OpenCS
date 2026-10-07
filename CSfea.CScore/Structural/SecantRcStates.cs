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
    private readonly bool _dropCoupling;

    /// <param name="section">Слоистое сечение (<c>PlateModel = "layered"</c>) с нужными TensionConcrete и ν.</param>
    /// <param name="materials">Диаграммы; <see cref="PlateSectionMaterials.ConcreteE_MPa"/> — для упругой As.</param>
    /// <param name="psi">Учитывать ψs арматуры у трещин.</param>
    /// <param name="rule">Правило выключения растянутого бетона трещиной.</param>
    /// <param name="zeroStrainBand">Полоса регуляризации секущей бетона без растяжения у нуля
    /// (<see cref="SecantLaminateBuilder.Build"/>).</param>
    /// <param name="dropCoupling">Диагностика: обнулять блок B секущей ABD (без связи мембранных усилий с изгибом —
    /// оценка вклада физического распора). Неподвижная точка тогда не воспроизводит истинные усилия сечения.</param>
    public PlateSecantShellState(PlateSection section, PlateSectionMaterials materials, bool psi,
        PlateCrackRule rule = PlateCrackRule.Layer, double zeroStrainBand = 0.0, bool dropCoupling = false)
    {
        _dropCoupling = dropCoupling;
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
    {
        var t = SecantLaminateBuilder.ToCsfea(SecantLaminateBuilder.Build(_section, s, _m.ConcreteDiagram, _m.RebarDiagram,
            _m.LayerDiagrams, _layers, zeroStrainBand: _band), _as);
        return _dropCoupling ? t with { B = new double[3, 3] } : t;
    }

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

    /// <summary>
    /// Деформация начала текучести и εs2 растянутой ветви. Текучесть — первый излом, за которым касательный модуль
    /// меньше 10 % начального (площадка или упрочнение), иначе — начало площадки σ ≥ 0,995·σ(εs2).
    /// </summary>
    internal static (double Yield, double Ultimate) RebarLimits(Diagramm d)
    {
        double ult = d.It.X.Max();
        double sMax = d.Sig(ult, out _);
        var xs = d.It.X.Where(x => x > 0.0).OrderBy(x => x).ToArray();
        double e0 = xs.Length > 0 ? d.Sig(xs[0], out _) / xs[0] : 0.0;
        for (int i = 0; i + 1 < xs.Length; i++)
        {
            double slope = (d.Sig(xs[i + 1], out _) - d.Sig(xs[i], out _)) / (xs[i + 1] - xs[i]);
            if (slope < 0.1 * e0) return (xs[i], ult);
        }
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
///
/// Сдвиг (<see cref="BeamShearSection"/>, null — Бернулли): до трещины упругий GA₀, наклонная трещина появляется в КЭ
/// вместе с первой нормальной (изгибно-сдвиговые трещины) и необратима; Q_cr — поперечная сила КЭ в момент трещины,
/// приведённая к M = M_crc точки трещины (при пропорциональном нагружении не зависит от шага). Дальше
/// Q = Q_cr + K_v·(γ − Q_cr/GA₀) (без площадки, см. <see cref="BeamShearCracked"/>), секущая GA = Q/γ.
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
    private readonly BeamShearSection? _shear;
    private ShearCrack? _shearCrack, _shearCrackCommitted;

    /// <summary>Наклонная трещина КЭ: Q_cr и закон после трещины по осям y и z (null — упругий сдвиг по оси).</summary>
    private sealed record ShearCrack(double QcrY, BeamShearCracked? LawY, double QcrZ, BeamShearCracked? LawZ);

    public CrossSectionSecantBeamState(CrossSection section, CalcType calc, double torsionGJ, bool tension, bool psi,
        BeamShearSection? shear = null)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _calc = calc;
        _tension = tension;
        _psi = psi;
        _shear = shear;
        var (s0, _) = SecantCrossSectionBuilder.Build(section, new Kurvature(), calc, tension);
        Initial = SecantCrossSectionBuilder.ToCsfea(s0);
        InitialShear = shear?.Initial ?? BeamShearStiffness.Rigid;
        Response = new SecantBeamResponse(Initial, torsionGJ, InitialShear);
    }

    public SecantBeamResponse Response { get; }
    public double[,] Initial { get; }
    public BeamShearStiffness InitialShear { get; }

    /// <summary>Есть ли в КЭ наклонная трещина (пробное состояние).</summary>
    public bool ShearCracked => _shearCrack != null;

    /// <summary>Трещины в точках ξ = 0, ½, 1 (пробное состояние).</summary>
    public IReadOnlyList<bool> Cracked => _cracked;

    public SecantBeamEvaluation Evaluate(IReadOnlyList<(double Eps0, double KappaY, double KappaZ)> strains,
        double gammaY = 0.0, double gammaZ = 0.0)
    {
        if (strains.Count != 3) throw new ArgumentException("Нужны деформации в трёх точках Лобатто.");
        var sp = new double[3][,];
        bool yielded = false, failed = false;
        var newCracks = new List<Kurvature>();
        for (int p = 0; p < 3; p++)
        {
            var k = new Kurvature { e0 = strains[p].Eps0, ky = strains[p].KappaY, kz = strains[p].KappaZ };
            if (!_cracked[p] && SecantCrossSectionBuilder.IsCracked(_section, k, _calc))
            {
                _cracked[p] = true;
                newCracks.Add(k);
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
        BeamShearStiffness? shearTarget = null;
        if (_shear != null)
        {
            if (_shearCrack == null && newCracks.Count > 0) _shearCrack = ShearCrackAt(newCracks, gammaY, gammaZ);
            shearTarget = new BeamShearStiffness(
                ShearSecant(gammaY, InitialShear.GAvY, _shearCrack?.QcrY ?? 0.0, _shearCrack?.LawY),
                ShearSecant(gammaZ, InitialShear.GAvZ, _shearCrack?.QcrZ ?? 0.0, _shearCrack?.LawZ));
        }
        return new SecantBeamEvaluation(target, new SecantSectionStatus(_cracked.Any(c => c), yielded, failed,
            _cracked.Count(c => c)), shearTarget);
    }

    /// <summary>
    /// Наклонная трещина вместе с первой нормальной: Q_cr = r·|Q|, Q — секущая поперечная сила КЭ (уравновешенная), r —
    /// наименьшее по новым точкам трещины M_crc/M (момент трещинообразования по лучу текущих усилий при неизменной N).
    /// Закон после трещины — по растянутой стороне в точке трещины.
    /// </summary>
    private ShearCrack ShearCrackAt(List<Kurvature> points, double gammaY, double gammaZ)
    {
        double ratio = 1.0;
        foreach (var k in points)
        {
            var f = Response.Forces(k.e0, k.ky, k.kz);
            double n = f.N / UnitScale.Force, mx = f.My / UnitScale.Moment, my = f.Mz / UnitScale.Moment;
            double m = Math.Sqrt(mx * mx + my * my);
            if (!(m > 0.0)) continue;
            var crc = new CrackingSolver(_section, _calc,
                tensionZone: CrackingSolver.LoadedTensionZone(_section, n, mx, my, nAtZeroMoment: n)).CrackingMoment(n, mx, my);
            if (crc.Converged) ratio = Math.Min(ratio, Math.Sqrt(crc.Mx * crc.Mx + crc.My * crc.My) / m);
        }
        var k0 = points[0];
        var cur = Response.Shear;
        double qy = double.IsFinite(cur.GAvY) ? Math.Abs(cur.GAvY * gammaY) : 0.0;
        double qz = double.IsFinite(cur.GAvZ) ? Math.Abs(cur.GAvZ * gammaZ) : 0.0;
        // ε = e0 + ky·Y + kz·X: растянута сторона +Y при ky > 0, +X — при kz > 0.
        return new ShearCrack(ratio * qy, _shear!.CrackedY(k0.kz > 0.0), ratio * qz, _shear.CrackedZ(k0.ky > 0.0));
    }

    /// <summary>Секущая GA по углу сдвига γ.</summary>
    private static double ShearSecant(double gamma, double ga0, double qcr, BeamShearCracked? law)
    {
        double g = Math.Abs(gamma);
        if (law == null || !double.IsFinite(ga0) || !(g > 0.0)) return ga0;
        double gcr = qcr / ga0;
        return g <= gcr ? ga0 : (qcr + Math.Min(law.Kv, ga0) * (g - gcr)) / g;
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
        _shearCrackCommitted = _shearCrack;
    }

    public void Revert()
    {
        Array.Copy(_crackedCommitted, _cracked, 3);
        Array.Copy(_epsCrcCommitted, _epsCrc, 3);
        _shearCrack = _shearCrackCommitted;
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

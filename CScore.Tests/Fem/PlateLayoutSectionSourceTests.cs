using System.Text.Json;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.Fem;

/// <summary>Раскладка армирования OpenCS (фон + зоны региона) как источник армирования КЭ в проверке по КЭ.</summary>
public class PlateLayoutSectionSourceTests
{
    const double H = 0.2;
    const double Cover = 0.035;
    const double Bottom = -(H / 2 - Cover);
    const double A12 = 565e-6;      // ⌀12 шаг 200, м²/м
    const double Extra = 20.1e-4;   // усиление зоны, м²/м

    static MaterialChars ConcreteChars(CalcType ct, double rb, double rbt) => new(ct)
    {
        Type = MatType.Concrete, E = 30_000_000.0, Fc = -rb, Ft = rbt,
        Ec0 = -0.002, Ec1 = -0.6 * rb / 30_000_000.0, Ec2 = -0.0035, Ec1Red = -0.0015,
        Et0 = 0.0001, Et1 = 0.6 * rbt / 30_000_000.0, Et2 = 0.00015, Et1Red = 0.00008,
    };

    static MaterialChars RebarChars(CalcType ct, double rs) => new(ct)
    {
        Type = MatType.ReSteelF, E = 200_000_000.0, Fc = -rs, Ft = rs, Ec2 = -0.025, Et2 = 0.025,
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
        m.C = RebarChars(CalcType.C, 435_000.0);
        m.CL = RebarChars(CalcType.CL, 435_000.0);
        m.N = RebarChars(CalcType.N, 500_000.0);
        m.NL = RebarChars(CalcType.NL, 500_000.0);
        return m;
    }

    static PlateRebarLayer Layer(string name, double z, RebarFace face, double asx = A12, double asy = A12) => new()
    {
        Name = name, InputMode = "direct", Asx = asx, Asy = asy, Zsx = z, Zsy = z,
        DiameterX = 0.012, DiameterY = 0.012, Face = face,
    };

    /// <summary>Сечение элемента: ⌀12 шаг 200 у обеих граней, привязка 35 мм.</summary>
    static PlateSection Section() => new()
    {
        Id = 5, Tag = "Пл200", H = H, NLayers = 40, PlateModel = "layered", ConcreteDiagramType = DiagrammType.L3,
        ConcreteMaterialId = 1, RebarMaterialId = 2,
        RebarLayers = [Layer("низ", Bottom, RebarFace.MinusN), Layer("верх", -Bottom, RebarFace.PlusN)],
    };

    /// <summary>Зона усиления нижней грани на квадрате 0…2 × 0…2 локальной плоскости элемента.</summary>
    static RebarZone BottomZone(double asx = Extra, double asy = Extra, string name = "Усиление низа") => new()
    {
        Name = name, Face = RebarFace.MinusN, Operation = RebarZoneOperation.Add, Priority = 1,
        Polygon = [new() { U = 0, V = 0 }, new() { U = 2, V = 0 }, new() { U = 2, V = 2 }, new() { U = 0, V = 2 }],
        Layout = Layer("", Bottom, RebarFace.PlusN, asx, asy),
    };

    /// <summary>Модель: плита на отметке z = 3 (оси элемента — глобальные) с КЭ сетки ЛИРЫ.</summary>
    sealed class Model
    {
        public PlateSection Section { get; } = PlateLayoutSectionSourceTests.Section();
        public PlanarRegion Region { get; } = new() { Id = 1, Frame = PlanarFrameBuilder.BuildPlateFrame(new PlanarVector3(0, 0, 3)) };
        public FemMember Member { get; } = new() { ElemTag = "Плита", ElemType = "shell", PlanarRegionId = 1, PlateSectionId = 5 };
        public List<FemElement> Elements { get; } = [];
        public List<FemMeshNode> Nodes { get; } = [];
        int _nextNode = 1;

        /// <summary>Добавить КЭ по координатам узлов в порядке записи ЛИРЫ «1 2 4 3» (третий узел — над первым).</summary>
        public FemElement Add(int num, (double X, double Y, double Z)[] nodes, string? memberTag = "Плита",
            double? thickness = null, double? axisAngle = null)
        {
            var ids = new List<int>();
            foreach (var (x, y, z) in nodes)
            {
                Nodes.Add(new FemMeshNode { NodeTag = _nextNode.ToString(), X = x, Y = y, Z = z, Origin = FemMember.MeshSourceImported });
                ids.Add(_nextNode++);
            }
            var e = new FemElement
            {
                ElemTag = num.ToString(), ElemType = "shell", NodeIdsJson = JsonSerializer.Serialize(ids),
                SourceMemberTag = memberTag, ThicknessM = thickness, LocalAxisAngleDeg = axisAngle,
                Origin = FemMember.MeshSourceImported,
            };
            Elements.Add(e);
            return e;
        }

        /// <summary>Квадратный КЭ 1×1 с углом (x0, y0): узловая ось вдоль +X, нормаль +Z.</summary>
        public FemElement Quad(int num, double x0, double y0, double? thickness = null, double? axisAngle = null, string? memberTag = "Плита") =>
            Add(num, [(x0, y0, 3), (x0 + 1, y0, 3), (x0, y0 + 1, 3), (x0 + 1, y0 + 1, 3)], memberTag, thickness, axisAngle);

        /// <summary>КЭ 1×1 с узловой осью вдоль +Y и нормалью +Z.</summary>
        public FemElement QuadAlongY(int num, double x0, double y0, double? axisAngle) =>
            Add(num, [(x0 + 1, y0, 3), (x0 + 1, y0 + 1, 3), (x0, y0, 3), (x0, y0 + 1, 3)], axisAngle: axisAngle);

        /// <summary>КЭ 1×1 с нормалью −Z (обход узлов по часовой стрелке), узловая ось вдоль +Y.</summary>
        public FemElement QuadFlipped(int num, double x0, double y0) =>
            Add(num, [(x0, y0, 3), (x0, y0 + 1, 3), (x0 + 1, y0, 3), (x0 + 1, y0 + 1, 3)]);

        public PlateLayoutResolver Resolver(PlateSection? fallback = null) =>
            new([Member], [Region], id => id == Section.Id ? Section : null, Elements, Nodes, fallback);

        public FemCheckScopeElement Scope(FemElement e) => new(int.Parse(e.ElemTag), e, Member);
    }

    // ── Раскладка на КЭ ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Background_OutsideZones()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        m.Quad(1, 5, 5);

        var r = m.Resolver().Resolve("1");

        Assert.Null(r.Reason);
        Assert.Equal("Раскладка: фон", r.Label);
        Assert.Equal(2, r.Layers!.Count);
        Assert.Equal(H, r.ThicknessM);
        Assert.False(r.Mirrored);
        Assert.False(r.AxesKnown);
        Assert.Equal(0, r.ForceAngleDeg);
    }

    [Fact]
    public void Zone_IsAddedByElementCentroid()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        m.Quad(1, 0.5, 0.5);    // центроид (1; 1) — в зоне
        m.Quad(2, 1.6, 0.5);    // центроид (2,1; 1) — вне зоны, хотя КЭ её задевает
        var resolver = m.Resolver();

        var inside = resolver.Resolve("1");
        var outside = resolver.Resolve("2");

        Assert.Equal("Раскладка: фон + Усиление низа", inside.Label);
        Assert.Equal(A12 + Extra, inside.Layers!.Where(l => l.Zsx < 0).Sum(l => l.Asx), 12);
        Assert.Equal(A12, inside.Layers!.Where(l => l.Zsx > 0).Sum(l => l.Asx), 12);
        Assert.All(inside.Layers!.Where(l => l.Zsx < 0), l => Assert.Equal(RebarFace.MinusN, l.Face));
        Assert.Equal("Раскладка: фон", outside.Label);

        var mosaic = new PlateLayoutRebarSource(resolver);
        Assert.Equal((A12 + Extra) * 1e4, mosaic.Get("1", PlateRebarMosaicComponent.BottomX).Value!.Value, 9);
        Assert.Equal(A12 * 1e4, mosaic.Get("1", PlateRebarMosaicComponent.TopY).Value!.Value, 9);
        Assert.Equal(A12 * 1e4, mosaic.Get("2", PlateRebarMosaicComponent.BottomX).Value!.Value, 9);
        Assert.False(mosaic.Supports(PlateRebarMosaicComponent.Transverse));
        Assert.True(mosaic.Get("99", PlateRebarMosaicComponent.BottomX).IsMissing);
    }

    [Fact]
    public void ReplaceZone_DropsBackgroundOfItsFace()
    {
        var m = new Model();
        var zone = BottomZone(asx: 3e-4, asy: 0);
        zone.Operation = RebarZoneOperation.Replace;
        m.Region.RebarZones.Add(zone);
        m.Quad(1, 0.5, 0.5);

        var r = m.Resolver().Resolve("1");

        Assert.Equal(3e-4, r.Layers!.Where(l => l.Zsx < 0).Sum(l => l.Asx), 12);
        Assert.Equal(0, r.Layers!.Where(l => l.Zsy < 0).Sum(l => l.Asy), 12);
        Assert.Equal(A12, r.Layers!.Where(l => l.Zsx > 0).Sum(l => l.Asx), 12);
    }

    /// <summary>Нормаль КЭ против нормали элемента: грань Z− усилий КЭ — это грань «плюс» раскладки.</summary>
    [Fact]
    public void FlippedNormal_MirrorsLayersThroughThickness()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        m.QuadFlipped(1, 0.5, 0.5);
        var resolver = m.Resolver();

        var r = resolver.Resolve("1");

        Assert.True(r.Mirrored);
        Assert.Equal(A12 + Extra, r.Layers!.Where(l => l.Zsx > 0).Sum(l => l.Asx), 12);
        Assert.Equal(A12, r.Layers!.Where(l => l.Zsx < 0).Sum(l => l.Asx), 12);
        Assert.Equal((A12 + Extra) * 1e4, new PlateLayoutRebarSource(resolver).Get("1", PlateRebarMosaicComponent.TopX).Value!.Value, 9);

        var source = new LayoutPlateSectionSource(m.Section, resolver);
        Assert.Contains(source.Warnings([m.Scope(m.Elements[0])], null, null), w => w.Contains("зеркально"));
    }

    [Fact]
    public void ElementThickness_KeepsDistanceFromFace()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        m.Quad(1, 0.5, 0.5, thickness: 0.3);

        var r = m.Resolver().Resolve("1");

        Assert.Equal(0.3, r.ThicknessM);
        Assert.All(r.Layers!.Where(l => l.Zsx < 0), l => Assert.Equal(-(0.15 - Cover), l.Zsx, 12));
        Assert.All(r.Layers!.Where(l => l.Zsx > 0), l => Assert.Equal(0.15 - Cover, l.Zsy, 12));
    }

    /// <summary>Стена: центроид КЭ переводится в локальные (u, v) элемента — X вдоль стены, Y вверх.</summary>
    [Fact]
    public void Wall_ZoneIsFoundInLocalCoordinates()
    {
        var m = new Model();
        var wall = new PlanarRegion { Id = 2, Frame = PlanarFrameBuilder.BuildWallFrame(new PlanarVector3(10, 0, 0), new PlanarVector3(11, 0, 0)) };
        wall.RebarZones.Add(BottomZone());
        var member = new FemMember { ElemTag = "Стена", ElemType = "shell", PlanarRegionId = 2, PlateSectionId = 5 };
        // КЭ в плоскости y = 0: u = x − 10, v = z; нормаль (0, −1, 0) совпадает с нормалью элемента.
        m.Add(1, [(10.5, 0, 0.5), (11.5, 0, 0.5), (10.5, 0, 1.5), (11.5, 0, 1.5)], memberTag: "Стена");
        m.Add(2, [(10.5, 0, 4.5), (11.5, 0, 4.5), (10.5, 0, 5.5), (11.5, 0, 5.5)], memberTag: "Стена");
        var resolver = new PlateLayoutResolver([member], [wall], _ => m.Section, m.Elements, m.Nodes);

        Assert.Equal("Раскладка: фон + Усиление низа", resolver.Resolve("1").Label);
        Assert.False(resolver.Resolve("1").Mirrored);
        Assert.Equal("Раскладка: фон", resolver.Resolve("2").Label);
    }

    [Fact]
    public void SameCombination_SharesSection()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        var a = m.Quad(1, 0.2, 0.2);
        var b = m.Quad(2, 0.8, 0.8);
        var c = m.Quad(3, 5, 5);
        var source = new LayoutPlateSectionSource(m.Section, m.Resolver());

        var ra = source.Resolve(m.Scope(a));
        var rb = source.Resolve(m.Scope(b));
        var rc = source.Resolve(m.Scope(c));

        Assert.Equal(FemCheckRebarSource.Layout, source.Key);
        Assert.Same(ra.Section, rb.Section);
        Assert.Equal(ra.RebarKey, rb.RebarKey);
        Assert.NotSame(ra.Section, rc.Section);
        Assert.Equal(40, ra.Section!.NLayers);   // модель и бетон — от сечения элемента
    }

    [Fact]
    public void MissingData_GivesReason()
    {
        var m = new Model();
        m.Quad(1, 0.5, 0.5, memberTag: null);
        m.Quad(2, 0.5, 0.5, memberTag: "Другой");
        var resolver = m.Resolver();

        Assert.Contains("не принадлежит", resolver.Resolve("1").Reason);
        Assert.Contains("не принадлежит", resolver.Resolve("2").Reason);
        Assert.Contains("нет в сетке", resolver.Resolve("77").Reason);

        // У элемента нет сечения: фон берётся от сечения цели, без него раскладки нет.
        m.Quad(3, 0.5, 0.5);
        m.Member.PlateSectionId = null;
        Assert.Contains("не задано сечение", m.Resolver().Resolve("3").Reason);
        Assert.Null(m.Resolver(fallback: m.Section).Resolve("3").Reason);
    }

    [Fact]
    public void UnsupportedLayout_GivesReason()
    {
        // Арматура под углом к осям элемента.
        var angled = new Model();
        var zone = BottomZone();
        zone.Layout.Angle = 30;
        angled.Region.RebarZones.Add(zone);
        angled.Quad(1, 0.5, 0.5);
        Assert.Contains("под углом", angled.Resolver().Resolve("1").Reason);

        // Отметка слоя зоны не у её грани.
        var wrongFace = new Model();
        zone = BottomZone();
        zone.Face = RebarFace.PlusN;
        wrongFace.Region.RebarZones.Add(zone);
        wrongFace.Quad(1, 0.5, 0.5);
        Assert.Contains("не соответствует грани", wrongFace.Resolver().Resolve("1").Reason);

        // Две зоны одного приоритета в одной точке.
        var conflict = new Model();
        conflict.Region.RebarZones.Add(BottomZone());
        conflict.Region.RebarZones.Add(BottomZone(name: "Вторая"));
        conflict.Quad(1, 0.5, 0.5);
        Assert.Contains("одинаковым приоритетом", conflict.Resolver().Resolve("1").Reason);

        // Причина доходит до проверки как «нет армирования».
        var section = new LayoutPlateSectionSource(conflict.Section, conflict.Resolver()).Resolve(conflict.Scope(conflict.Elements[0]));
        Assert.Null(section.Section);
        Assert.Contains("одинаковым приоритетом", section.Reason);
    }

    // ── Оси выдачи усилий ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ось выдачи усилий — узловая ось, повёрнутая на угол согласования. Совпала с осью x элемента
    /// (или противоположна ей) — поворот не нужен; иначе усилия поворачиваются на угол между осями.
    /// </summary>
    [Fact]
    public void OutputAxis_IsComparedWithLayoutAxis()
    {
        var m = new Model();
        m.Quad(1, 0, 0, axisAngle: 0);             // узловая ось вдоль X, согласования нет
        m.QuadAlongY(2, 0, 0, axisAngle: -90);     // узловая ось вдоль Y, согласована на X
        m.QuadAlongY(3, 0, 0, axisAngle: 0);       // узловая ось вдоль Y, не согласована
        m.Quad(4, 0, 0, axisAngle: 180);           // ось выдачи против X — для усилий то же самое
        m.Quad(5, 0, 0, axisAngle: 30);
        var resolver = m.Resolver();

        Assert.True(resolver.Resolve("1").AxesKnown);
        Assert.Equal(0, resolver.Resolve("1").ForceAngleDeg);
        Assert.Equal(0, resolver.Resolve("2").ForceAngleDeg);
        Assert.Equal(90, resolver.Resolve("3").ForceAngleDeg, 9);
        Assert.Equal(0, resolver.Resolve("4").ForceAngleDeg);
        Assert.Equal(30, resolver.Resolve("5").ForceAngleDeg, 9);

        var source = new LayoutPlateSectionSource(m.Section, resolver);
        Assert.Equal(90, source.Resolve(m.Scope(m.Elements[2])).ForceAngleDeg, 9);
        var warnings = source.Warnings(m.Elements.Select(m.Scope).ToList(), null, null);
        Assert.Contains(warnings, w => w.Contains("У 2 КЭ оси выдачи усилий не совпадают") && w.Contains("90"));
        Assert.DoesNotContain(warnings, w => w.Contains("неизвестны"));
    }

    [Fact]
    public void UnknownOutputAxis_IsReported()
    {
        var m = new Model();
        m.QuadAlongY(1, 0, 0, axisAngle: null);
        var source = new LayoutPlateSectionSource(m.Section, m.Resolver());

        Assert.Equal(0, source.Resolve(m.Scope(m.Elements[0])).ForceAngleDeg);
        Assert.Contains(source.Warnings([m.Scope(m.Elements[0])], null, null), w => w.Contains("У 1 КЭ оси выдачи усилий неизвестны"));
    }

    /// <summary>У КЭ с перевёрнутой нормалью ось y проверки направлена против оси y элемента.</summary>
    [Fact]
    public void OutputAxis_OfFlippedElement()
    {
        var m = new Model();
        // Нормаль −Z, узловая ось вдоль +Y; поворот на +90° вокруг нормали (−Z) переводит +Y в +X.
        m.QuadFlipped(1, 0, 0).LocalAxisAngleDeg = 90;
        m.QuadFlipped(2, 0, 0).LocalAxisAngleDeg = 0;
        var resolver = m.Resolver();

        Assert.Equal(0, resolver.Resolve("1").ForceAngleDeg);
        Assert.Equal(90, Math.Abs(resolver.Resolve("2").ForceAngleDeg), 9);
    }

    [Fact]
    public void ForceTransform_RotatesTensorsAndShear()
    {
        var item = new ShellLoadItem
        {
            Label = "э.7", Nx = 100, Ny = -40, Nxy = 15, Mx = 30, My = -10, Mxy = 4, Qx = 12, Qy = -5,
            SigmaX = 500, SigmaY = -200, TauXY = 75, SourceElementNum = 7, SourceSectionNum = 1,
        };

        // Ось x усилий направлена вдоль оси Y новых осей: x → Y, y → −X.
        var r = ShellForceTransform.Rotate(item, 90);
        Assert.Equal((-40.0, 100.0, -15.0), (Math.Round(r.Nx, 9), Math.Round(r.Ny, 9), Math.Round(r.Nxy, 9)));
        Assert.Equal((-10.0, 30.0, -4.0), (Math.Round(r.Mx, 9), Math.Round(r.My, 9), Math.Round(r.Mxy, 9)));
        Assert.Equal((5.0, 12.0), (Math.Round(r.Qx, 9), Math.Round(r.Qy, 9)));
        Assert.Equal((-200.0, 500.0, -75.0), (Math.Round(r.SigmaX!.Value, 9), Math.Round(r.SigmaY!.Value, 9), Math.Round(r.TauXY!.Value, 9)));
        Assert.Equal(("э.7", 7, 1), (r.Label, r.SourceElementNum, r.SourceSectionNum));

        // Инварианты тензора и обратный поворот.
        var a = ShellForceTransform.Rotate(item, 37);
        Assert.Equal(item.Mx + item.My, a.Mx + a.My, 9);
        Assert.Equal(item.Mx * item.My - item.Mxy * item.Mxy, a.Mx * a.My - a.Mxy * a.Mxy, 9);
        Assert.Equal(item.Qx * item.Qx + item.Qy * item.Qy, a.Qx * a.Qx + a.Qy * a.Qy, 9);
        var back = ShellForceTransform.Rotate(a, -37);
        Assert.Equal(item.Nxy, back.Nxy, 9);
        Assert.Equal(item.Mx, back.Mx, 9);
        Assert.Equal(item.Qy, back.Qy, 9);

        // Одноосное растяжение вдоль x, повёрнутой на 45°: поровну по осям и сдвиг.
        var diag = ShellForceTransform.Rotate(new ShellLoadItem { Nx = 100 }, 45);
        Assert.Equal((50.0, 50.0, 50.0), (Math.Round(diag.Nx, 9), Math.Round(diag.Ny, 9), Math.Round(diag.Nxy, 9)));
        Assert.Null(diag.SigmaX);
    }

    // ── Проверка по КЭ целиком ───────────────────────────────────────────────────────────────

    static readonly FemCheck LayeredUls = new()
    {
        NormCode = "rc_plate_check", Tag = "плита",
        ParamsJson = new PlateCheckParams { Kind = "shell_layered", CheckGroup = "uls", RebarSources = [FemCheckRebarSource.Layout] }.ToJson(),
    };

    static JsonDocument Run(Model m, params ShellLoadItem[] rows)
    {
        var scope = new FemCheckScope([m.Member], m.Elements.Select(m.Scope).ToList(), RefersToMeshElements: false);
        var result = FemCheckRunner.RunPerElement(LayeredUls, m.Member, scope,
            [new ForceSet { Id = 1, Kind = "shell", Tag = "РСН (C)", ShellItems = [.. rows] }],
            new FemPerElementInputs
            {
                PlateTemplate = m.Section, ConcreteMat = Concrete(), RebarMat = Rebar(),
                PlateSources = [new LayoutPlateSectionSource(m.Section, m.Resolver())],
            },
            (_, _, _) => throw new InvalidOperationException("стержневой исполнитель не нужен"));
        return JsonDocument.Parse(result.DataJson);
    }

    static JsonElement Element(JsonDocument doc, int num) =>
        doc.RootElement.GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("elemNum").GetInt32() == num);

    static ShellLoadItem Row(int elem, double mx = 0, double my = 0) =>
        new() { Label = $"э.{elem}", Mx = mx, My = my, SourceElementNum = elem };

    /// <summary>Момент, растягивающий низ: фоновой арматуры не хватает, в зоне усиления — хватает.</summary>
    [Fact]
    public void Check_UsesZoneReinforcementOfEachElement()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone());
        m.Quad(1, 0.5, 0.5, axisAngle: 0);   // в зоне
        m.Quad(2, 5, 5, axisAngle: 0);       // вне зоны

        using var doc = Run(m, Row(1, mx: -60), Row(2, mx: -60));

        Assert.Equal(["layout"], doc.RootElement.GetProperty("rebarSources").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal("ok", Element(doc, 1).GetProperty("status").GetString());
        Assert.Equal("Раскладка: фон + Усиление низа", Element(doc, 1).GetProperty("sectionLabel").GetString());
        Assert.Equal("failed", Element(doc, 2).GetProperty("status").GetString());
        Assert.Equal("Раскладка: фон", Element(doc, 2).GetProperty("sectionLabel").GetString());
    }

    /// <summary>
    /// Зона усилена только вдоль x элемента. Момент Mx в осях выдачи КЭ: у КЭ с осью выдачи вдоль x элемента
    /// он ложится на усиленное направление, у КЭ с осью выдачи вдоль y — на неусиленное (после поворота
    /// усилий это My в осях раскладки).
    /// </summary>
    [Fact]
    public void Check_RotatesForcesIntoLayoutAxes()
    {
        var m = new Model();
        m.Region.RebarZones.Add(BottomZone(asy: 0));
        m.Quad(1, 0.2, 0.2, axisAngle: 0);           // ось выдачи вдоль x элемента
        m.QuadAlongY(2, 0.4, 0.4, axisAngle: 0);     // ось выдачи вдоль y элемента
        m.QuadAlongY(3, 0.6, 0.6, axisAngle: -90);   // узловая ось вдоль y, согласована на x

        using var doc = Run(m, Row(1, mx: -60), Row(2, mx: -60), Row(3, mx: -60), Row(2, my: -60));

        Assert.Equal("ok", Element(doc, 1).GetProperty("status").GetString());
        Assert.Equal("ok", Element(doc, 3).GetProperty("status").GetString());
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("elemNum").GetInt32() == 2).ToList();
        Assert.False(rows[0].GetProperty("passed").GetBoolean());   // Mx выдачи → My раскладки: усиления нет
        Assert.True(rows[1].GetProperty("passed").GetBoolean());    // My выдачи → Mx раскладки: усиление есть
        Assert.Contains(doc.RootElement.GetProperty("summary").GetProperty("warnings").EnumerateArray(),
            w => w.GetString()!.Contains("У 1 КЭ оси выдачи усилий не совпадают"));
    }
}

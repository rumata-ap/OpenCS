using System.Globalization;
using CScore;

// Тест сходимости StrainSolver
// Прямоугольное ЖБ сечение 0.3×0.6 м, бетон B30, без сеточных фибр

static MaterialChars MakeChars(CalcType ct)
{
    // Fc < 0 — сжатие отрицательное (как в CSV: B30 Fc ≈ -17000 кН/м²)
    double Fc = -17000; double Ft = 1200; double E = 32_500_000;
    return new MaterialChars
    {
        Type = MatType.Concrete, TypeCalc = ct,
        Fc = Fc, Ft = Ft, E = E,
        Ec2 = -0.0035, Ec1Red = Fc / E,   // = -17000/32500000 = -0.000523
        Et1Red = Ft / E, Et2 = 0.00015
    };
}

var conc = new Material { Id = 1, Tag = "B30", Type = MatType.Concrete, E = 32_500_000 };
conc.MaterialChars = [MakeChars(CalcType.C), MakeChars(CalcType.CL),
                      MakeChars(CalcType.N), MakeChars(CalcType.NL)];

// Прямоугольник 0.3×0.6, центрированный
double b = 0.3, h = 0.6;
double x0 = -b/2, x1 = b/2, y0 = -h/2, y1 = h/2;

var xList = new List<double> { x0, x1, x1, x0, x0 };
var yList = new List<double> { y0, y0, y1, y1, y0 };

var hull = new Contour(xList, yList, "hull") { Type = ContourType.Hull };

var area = new MaterialArea
{
    Id = 1, Tag = "Бетон", Material = conc, MaterialId = 1,
    DiagrammType = DiagrammType.L2,
    WKT = $"POLYGON (({string.Join(", ",
        xList.Zip(yList, (x, y) => $"{x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)}")
    )}))"
};
area.Contours.Add(hull);
area.ResolveAndBuildDiagramms();

// Генерируем сетку 6×12 (фибровый путь)
area.SliceXY(6, 12);

var section = new CrossSection { Id = 1, Tag = "Тест 0.3×0.6 + 4Ø20" };
section.Areas.Add(area);

// Рабочая арматура A400, 4Ø20 в двух рядах
static MaterialChars MakeRebarChars(CalcType ct)
{
    double Ry = 350000; double E = 200_000_000;
    return new MaterialChars
    {
        Type = MatType.ReSteelF, TypeCalc = ct,
        Fc = -Ry, Ft = Ry, E = E,
        Ec2 = -0.025, Et2 = 0.025
    };
}
var rebar = new Material { Id = 2, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000 };
rebar.MaterialChars = [MakeRebarChars(CalcType.C), MakeRebarChars(CalcType.CL),
                       MakeRebarChars(CalcType.N), MakeRebarChars(CalcType.NL)];

double dA20 = Math.PI * 0.020 * 0.020 / 4;
var rebarArea = new MaterialArea
{
    Id = 2, Tag = "Арм.", Material = rebar, MaterialId = 2,
    DiagrammType = DiagrammType.L2,
    HostArea = area, HostAreaId = area.Id
};
rebarArea.Fibers.AddRange(new[]
{
    new Fiber(1,"as_bot","") { X=-0.075, Y=-0.25, Area=dA20, TypeFiber=FiberType.point },
    new Fiber(2,"as_bot","") { X= 0.075, Y=-0.25, Area=dA20, TypeFiber=FiberType.point },
    new Fiber(3,"as_top","") { X=-0.075, Y= 0.25, Area=dA20, TypeFiber=FiberType.point },
    new Fiber(4,"as_top","") { X= 0.075, Y= 0.25, Area=dA20, TypeFiber=FiberType.point },
});
rebarArea.ResolveAndBuildDiagramms();
section.Areas.Add(rebarArea);

// Диагностика
{
    Console.WriteLine($"Фибр в сечении: {area.Fibers.Count} (не-точечных: {area.Fibers.Count(f => f.TypeFiber != FiberType.point)})");
    var gp = new GeoProps(area);
    Console.WriteLine($"GeoProps(area): EA={gp.EA:G5}, EIx={gp.EIx:G5}, EIy={gp.EIy:G5}");
    if (area.Hull != null)
    {
        var gpHull = new GeoProps(area.Hull, conc.E);
        Console.WriteLine($"GeoProps(hull): EA={gpHull.EA:G5}, EIx={gpHull.EIx:G5}, EIy={gpHull.EIy:G5}");
    }
    Console.WriteLine();
}

// Быстрая проверка Guess
{
    var g = section.Guess(new Load { N = 0, Mx = 100, My = 0 });
    Console.WriteLine($"Guess для Mx=100: e0={g.e0:G4}, ky={g.ky:G4}, kz={g.kz:G4}");
    double EIx_expected = conc.E * (b * h * h * h / 12);
    Console.WriteLine($"EIx теоретический = {EIx_expected:F0} кН·м²  (ожидаем ky ≈ {100.0/EIx_expected:G4})");
    Console.WriteLine();
}

// Попробуем N > 0 = сжатие (нетипичная, но возможная конвенция в коде)
var cases = new[]
{
    (desc: "Чистый изгиб          Mx=100",              N:    0.0, Mx:  100.0, My:   0.0),
    (desc: "Сжатие N=+500, Mx=100  (N>0=сжатие)",       N:  500.0, Mx:  100.0, My:   0.0),
    (desc: "Центр. сжатие N=+1500 (N>0=сжатие)",        N: 1500.0, Mx:    0.0, My:   0.0),
    (desc: "Сжатие N=-500, Mx=100  (N<0=сжатие)",       N: -500.0, Mx:  100.0, My:   0.0),
    (desc: "Центр. сжатие N=-1500 (N<0=сжатие)",        N:-1500.0, Mx:    0.0, My:   0.0),
};

Console.WriteLine($"{"Случай",-44} {"iter",4} {"невязка",9}  {"статус",13}   ε₀          κy          κz");
Console.WriteLine(new string('-', 115));

foreach (var (desc, N, Mx, My) in cases)
{
    var solver = new StrainSolver(section, CalcType.C, tol: 0.1, maxIter: 60);
    var k = solver.Solve(N, Mx, My);
    var res = section.Integral(k, CalcType.C);
    string status = solver.Converged ? "✓ сошлось" : "✗ не сошлось";
    Console.WriteLine($"{desc,-44} {solver.Iterations,4} {solver.Residual,9:F4}  {status,-13}   {k.e0,10:G5} {k.ky,10:G5} {k.kz,10:G5}");
    Console.WriteLine($"  N: {N,8:F1} / {res.N,8:F1}  кН     Mx: {Mx,7:F1} / {res.Mx,7:F1}  кН·м     My: {My,6:F1} / {res.My,6:F1}  кН·м");
    Console.WriteLine();
}

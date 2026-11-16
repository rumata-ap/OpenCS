using System.Globalization;
using CScore.Fem;
using CScore.Planar;
using CScore.Sp63Punching.Fem;

namespace CScore.Tests.Sp63Punching.Fem;

/// <summary>Синтетическая сетка для тестов продавливания по МКЭ: плиты из квадратов, стержни, тела АЖТ.</summary>
sealed class PunchingFemTestMesh
{
    public Dictionary<string, FemMeshNode> Nodes { get; } = new(StringComparer.Ordinal);
    public List<FemElement> Elements { get; } = [];
    public List<FemRigidBody> Bodies { get; } = [];
    readonly Dictionary<(long, long, long), string> _byPos = [];
    int _node = 1, _elem = 1;

    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);

    public string Node(double x, double y, double z)
    {
        var key = ((long)Math.Round(x * 1e4), (long)Math.Round(y * 1e4), (long)Math.Round(z * 1e4));
        if (_byPos.TryGetValue(key, out var t)) return t;
        t = S(_node++);
        _byPos[key] = t;
        Nodes[t] = new FemMeshNode { NodeTag = t, X = x, Y = y, Z = z };
        return t;
    }

    public FemMeshNode At(double x, double y, double z) => Nodes[Node(x, y, z)];

    /// <summary>Пластина по углам против часовой стрелки (хранение «1 2 4 3»).</summary>
    public FemElement Quad(string a, string b, string c, string d, double h)
    {
        var e = new FemElement
        {
            ElemTag = S(_elem++), ElemType = "shell", NodeIdsJson = $"[{a},{b},{d},{c}]", ThicknessM = h,
        };
        Elements.Add(e);
        return e;
    }

    /// <summary>Горизонтальная плита сеткой квадратов; <paramref name="skip"/> — пропустить ячейку по её центру (отверстие).</summary>
    public void Slab(double x0, double y0, double x1, double y1, double z, double step, double h = 0.2,
        Func<double, double, bool>? skip = null, Func<double, double, double>? thickness = null)
    {
        int nx = (int)Math.Round((x1 - x0) / step), ny = (int)Math.Round((y1 - y0) / step);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                double xa = x0 + i * step, ya = y0 + j * step, xb = xa + step, yb = ya + step;
                double cx = (xa + xb) / 2, cy = (ya + yb) / 2;
                if (skip?.Invoke(cx, cy) == true) continue;
                Quad(Node(xa, ya, z), Node(xb, ya, z), Node(xb, yb, z), Node(xa, yb, z), thickness?.Invoke(cx, cy) ?? h);
            }
    }

    /// <summary>Вертикальная стенка из квадратов в плоскости XZ (y = const) от z0 до z1.</summary>
    public void WallXZ(double x0, double x1, double y, double z0, double z1, double step, double h = 0.2)
    {
        int nx = (int)Math.Round((x1 - x0) / step), nz = (int)Math.Round((z1 - z0) / step);
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                double xa = x0 + i * step, za = z0 + k * step;
                Quad(Node(xa, y, za), Node(xa + step, y, za), Node(xa + step, y, za + step), Node(xa, y, za + step), h);
            }
    }

    /// <summary>Вертикальная стенка из квадратов в плоскости YZ (x = const) от z0 до z1.</summary>
    public void WallYZ(double x, double y0, double y1, double z0, double z1, double step, double h = 0.2)
    {
        int ny = (int)Math.Round((y1 - y0) / step), nz = (int)Math.Round((z1 - z0) / step);
        for (int j = 0; j < ny; j++)
            for (int k = 0; k < nz; k++)
            {
                double ya = y0 + j * step, za = z0 + k * step;
                Quad(Node(x, ya, za), Node(x, ya + step, za), Node(x, ya + step, za + step), Node(x, ya, za + step), h);
            }
    }

    public FemElement Bar(string a, string b, double rotationDeg = 0)
    {
        var e = new FemElement { ElemTag = S(_elem++), ElemType = "beam", NodeIdsJson = $"[{a},{b}]", BeamRotationDeg = rotationDeg };
        Elements.Add(e);
        return e;
    }

    public FemRigidBody Body(string master, IEnumerable<string> slaves)
    {
        var b = new FemRigidBody { Id = Bodies.Count + 1, MasterNodeTag = master };
        b.SetSlaveNodeTags(slaves);
        Bodies.Add(b);
        return b;
    }

    /// <summary>Узлы плиты на отметке z в прямоугольнике (с допуском).</summary>
    public IEnumerable<string> NodesIn(double x0, double y0, double x1, double y1, double z) =>
        Nodes.Values.Where(n => Math.Abs(n.Z - z) < 1e-6 && n.X >= x0 - 1e-6 && n.X <= x1 + 1e-6 && n.Y >= y0 - 1e-6 && n.Y <= y1 + 1e-6)
            .Select(n => n.NodeTag);

    public static PunchingFemBarSection Rect(double by, double bz) =>
        new([(-by / 2, -bz / 2), (by / 2, -bz / 2), (by / 2, bz / 2), (-by / 2, bz / 2)], $"{by * 1000:0}×{bz * 1000:0}");

    public static PunchingFemBarSection Round(double d, int n = 32) =>
        new(Enumerable.Range(0, n).Select(i => (d / 2 * Math.Cos(2 * Math.PI * i / n), d / 2 * Math.Sin(2 * Math.PI * i / n))).ToList(), $"Ø{d * 1000:0}");

    public static readonly PunchingFemCover Cover = new(0.035, 0.045);

    public PunchingFemModel Model(PunchingFemBarSection? section = null,
        Func<FemElement, PunchingFemBarSection?>? sections = null, Func<FemElement, PunchingFemShellProps?>? props = null) => new()
    {
        Nodes = Nodes, Elements = Elements, RigidBodies = Bodies,
        BarSection = sections ?? (_ => section ?? Rect(0.4, 0.4)),
        ShellProps = props ?? (e => e.ThicknessM is double h
            ? new PunchingFemShellProps("h" + h.ToString(CultureInfo.InvariantCulture), h, 1, Cover, Cover, $"h = {h * 1000:0}")
            : null),
    };

    public static PlanarVector3 P(double x, double y, double z) => new(x, y, z);
}

using System.Globalization;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка таблицы узлов сетки: узел и его ГУ сеточного уровня (закрепление, пружина, происхождение).</summary>
public sealed class FemMeshNodeRow
{
    public FemMeshNodeRow(FemMeshNode node) => Node = node;

    public FemMeshNode Node { get; }
    public string NodeTag => Node.NodeTag;
    public double X => Node.X;
    public double Y => Node.Y;
    public double Z => Node.Z;
    public string? SourceNodeTag => Node.SourceNodeTag;
    public string? SourceMemberTag => Node.SourceMemberTag;

    /// <summary>Маска закреплённых DOF (биты 0–5: X Y Z UX UY UZ).</summary>
    public int SupportMask { get; private set; }

    /// <summary>Жёсткости пружины X Y Z UX UY UZ, Н/м и Н·м/рад (нули — пружины нет).</summary>
    public double[] Stiffnesses { get; private set; } = new double[6];

    /// <summary>Происхождения записей ГУ узла («manual», «import:scad»…).</summary>
    public IReadOnlyList<string> Origins { get; private set; } = [];

    public bool HasBoundary => SupportMask != 0 || Stiffnesses.Any(k => k != 0);

    public string SupportText => SupportMask == 0 ? "" : FemBoundaryDofs.Describe(SupportMask);

    /// <summary>Пружины в кН/м и кН·м/рад: «Z 1 250; UZ 40».</summary>
    public string SpringText
    {
        get
        {
            string[] names = ["X", "Y", "Z", "UX", "UY", "UZ"];
            return string.Join("; ", Enumerable.Range(0, 6).Where(i => Stiffnesses[i] != 0)
                .Select(i => $"{names[i]} {(Stiffnesses[i] / 1e3).ToString("#,0.###", CultureInfo.CurrentCulture)}"));
        }
    }

    public string OriginText => string.Join(", ", Origins.Select(DescribeOrigin));

    /// <summary>Подпись происхождения: «вручную», «SCAD», «ЛИРА».</summary>
    public static string DescribeOrigin(string origin) => origin switch
    {
        FemLoadOrigin.Manual => Loc.S("FemBoundaryOriginManual"),
        "import:scad" => "SCAD",
        "import:lira" => Loc.S("FemBoundaryOriginLira"),
        _ => origin,
    };

    /// <summary>Задаёт ГУ строки по записям узла.</summary>
    public void Apply(IEnumerable<FemMeshNodeSupport> supports, IEnumerable<FemSpring> springs)
    {
        var s = supports.ToList();
        var k = new double[6];
        var origins = s.Select(x => x.Origin).ToList();
        foreach (var spring in springs)
        {
            var add = spring.Stiffnesses;
            for (int i = 0; i < 6; i++) k[i] += add[i];
            origins.Add(spring.Origin);
        }
        SupportMask = s.Aggregate(0, (m, x) => m | (x.Mask & FemBoundaryDofs.All));
        Stiffnesses = k;
        Origins = origins.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>ГУ после ручной правки.</summary>
    public void SetManual(int mask, IReadOnlyList<double> stiffnesses)
    {
        SupportMask = mask & FemBoundaryDofs.All;
        Stiffnesses = [.. stiffnesses];
        Origins = HasBoundary ? [FemLoadOrigin.Manual] : [];
    }
}

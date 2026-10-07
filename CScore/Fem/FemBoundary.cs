using System.Text.Json;

namespace CScore.Fem;

/// <summary>
/// Закрепление узла сетки (сеточный уровень схемы). Конструктивный уровень хранит закрепление в
/// <see cref="FemNode.DofMask"/>. Повторное чтение источника заменяет записи своего <see cref="Origin"/>,
/// ручные (<see cref="FemLoadOrigin.Manual"/>) не трогаются.
/// </summary>
public sealed class FemMeshNodeSupport
{
    public int Id { get; set; }
    public int SchemaId { get; set; }

    /// <summary>Тег узла сетки (<see cref="FemMeshNode.NodeTag"/>).</summary>
    public string NodeTag { get; set; } = "";

    /// <summary>Маска закреплённых DOF в общих осях (биты 0–5: X Y Z UX UY UZ), как <see cref="FemNode.DofMask"/>.</summary>
    public int Mask { get; set; }

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;
}

/// <summary>
/// Упругая связь узла с землёй в общих осях (КЭ 51 SCAD): одна запись на узел, нулевая жёсткость — связи по DOF нет.
/// </summary>
public sealed class FemSpring
{
    public int Id { get; set; }
    public int SchemaId { get; set; }

    /// <summary>Вид узла: <see cref="FemSpringTargetKinds.Node"/> (узел конструктивного уровня) или
    /// <see cref="FemSpringTargetKinds.MeshNode"/> (узел сетки).</summary>
    public string TargetKind { get; set; } = FemSpringTargetKinds.MeshNode;

    /// <summary>Тег узла (<see cref="FemNode.NodeTag"/> или <see cref="FemMeshNode.NodeTag"/> по <see cref="TargetKind"/>).</summary>
    public string NodeTag { get; set; } = "";

    /// <summary>Поступательные жёсткости, Н/м.</summary>
    public double Kx { get; set; }
    public double Ky { get; set; }
    public double Kz { get; set; }

    /// <summary>Поворотные жёсткости, Н·м/рад.</summary>
    public double Kux { get; set; }
    public double Kuy { get; set; }
    public double Kuz { get; set; }

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    /// <summary>Номер КЭ пружины в источнике (КЭ 51 SCAD); null — задана вручную.</summary>
    public string? SourceElemTag { get; set; }

    /// <summary>Жёсткости по DOF 0–5 (X Y Z UX UY UZ).</summary>
    public double[] Stiffnesses => [Kx, Ky, Kz, Kux, Kuy, Kuz];

    /// <summary>Задаёт жёсткости по DOF 0–5.</summary>
    public void SetStiffnesses(IReadOnlyList<double> k)
    {
        if (k.Count != 6) throw new ArgumentException("Нужно 6 жёсткостей: X Y Z UX UY UZ.", nameof(k));
        (Kx, Ky, Kz, Kux, Kuy, Kuz) = (k[0], k[1], k[2], k[3], k[4], k[5]);
    }

    /// <summary>Маска DOF с ненулевой жёсткостью.</summary>
    public int ActiveMask
    {
        get
        {
            var k = Stiffnesses;
            int mask = 0;
            for (int i = 0; i < 6; i++)
                if (k[i] != 0) mask |= 1 << i;
            return mask;
        }
    }
}

/// <summary>Виды узла пружины.</summary>
public static class FemSpringTargetKinds
{
    /// <summary>Узел конструктивного уровня (<see cref="FemNode"/>).</summary>
    public const string Node = "node";
    /// <summary>Узел сетки (<see cref="FemMeshNode"/>).</summary>
    public const string MeshNode = "mesh_node";
}

/// <summary>
/// Жёсткое тело (КЭ 100 SCAD, АЖТ ЛИРЫ) на узлах сетки: ведомые узлы повторяют перемещения ведущего по DOF маски.
/// Только сеточный уровень.
/// </summary>
public sealed class FemRigidBody
{
    IReadOnlyList<string>? _slaves;
    string _slavesJson = "[]";

    public int Id { get; set; }
    public int SchemaId { get; set; }

    /// <summary>Тег ведущего узла сетки.</summary>
    public string MasterNodeTag { get; set; } = "";

    /// <summary>JSON-массив тегов ведомых узлов сетки. Писать — через <see cref="SetSlaveNodeTags"/>.</summary>
    public string SlaveNodeTagsJson
    {
        get => _slavesJson;
        set { _slavesJson = value; _slaves = null; }
    }

    /// <summary>Маска объединяемых DOF в общих осях (биты 0–5: X Y Z UX UY UZ).</summary>
    public int Mask { get; set; } = FemBoundaryDofs.All;

    /// <summary>Происхождение: <see cref="FemLoadOrigin.Manual"/> или «import:&lt;источник&gt;».</summary>
    public string Origin { get; set; } = FemLoadOrigin.Manual;

    /// <summary>Номер КЭ жёсткого тела в источнике (КЭ 100 SCAD, номер АЖТ ЛИРЫ); null — задано вручную.</summary>
    public string? SourceElemTag { get; set; }

    /// <summary>Теги ведомых узлов в порядке хранения.</summary>
    public IReadOnlyList<string> SlaveNodeTags => _slaves ??= FemMemberGroup.ParseTags(_slavesJson);

    /// <summary>Заменяет ведомые узлы: без пустых и повторов, в порядке первого появления.</summary>
    public void SetSlaveNodeTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = tags.Select(t => t.Trim()).Where(t => t.Length > 0 && seen.Add(t)).ToList();
        _slavesJson = JsonSerializer.Serialize(list);
        _slaves = list;
    }
}

/// <summary>Маски степеней свободы граничных условий.</summary>
public static class FemBoundaryDofs
{
    /// <summary>Все шесть DOF: X Y Z UX UY UZ (для шарниров стержня — N Qy Qz T My Mz в местных осях).</summary>
    public const int All = 0b11_1111;

    /// <summary>Подпись маски «X Y UZ» (общие оси) — для отчётов.</summary>
    public static string Describe(int mask)
    {
        string[] names = ["X", "Y", "Z", "UX", "UY", "UZ"];
        var parts = Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0).Select(i => names[i]).ToList();
        return parts.Count == 0 ? "—" : string.Join(" ", parts);
    }
}

/// <summary>ГУ, принадлежащие КЭ сетки: освобождения концов стержня и C1 пластины (см. <see cref="FemElement"/>).</summary>
public readonly record struct FemElementBoundaryProps(int? ReleaseI, int? ReleaseJ, double? FoundationC1);

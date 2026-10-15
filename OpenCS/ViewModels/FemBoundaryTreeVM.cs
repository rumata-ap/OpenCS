using System.ComponentModel;
using System.Globalization;
using CScore.Fem;
using CScore.Planar;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Вид ветки ГУ дерева схемы.</summary>
public enum FemBoundaryKind { NodalSupports, LineSupports, Foundation, Releases, RigidBodies }

/// <summary>Объекты схемы, на которых заданы ГУ группы: узлы конструктивного уровня, узлы и КЭ сетки, грани пластин.</summary>
public sealed record FemBoundaryTargets(
    IReadOnlyList<string> NodeTags, IReadOnlyList<string> MeshNodeTags, IReadOnlyList<string> MeshElemTags,
    IReadOnlyList<FemPlateEdgeKey>? PlateEdgeKeys = null)
{
    public static readonly FemBoundaryTargets Empty = new([], [], []);

    /// <summary>Грани пластинчатых КонЭ (линейные связи).</summary>
    public IReadOnlyList<FemPlateEdgeKey> PlateEdges => PlateEdgeKeys ?? [];
}

/// <summary>
/// Тип ГУ в дереве схемы — как тип опоры в навигаторе RFEM: узловая связь с одними параметрами («Заделка — 12»,
/// «Kz = 5000 кН/м — 4»), набор шарниров, жёсткое тело. Узловые связи с переменной жёсткостью (основание импорта —
/// у каждого узла своя) — одна строка-мозаика без списка значений. Объекты читаются по требованию.
/// </summary>
public sealed class FemBoundaryGroupItem
{
    readonly Func<FemBoundaryTargets> _targets;

    public FemBoundaryKind Kind { get; }
    public FemSchema Schema { get; }
    /// <summary>Подпись группы без счётчика.</summary>
    public string Label { get; }
    /// <summary>Число объектов (узлов или КЭ); у жёсткого тела — 0, счётчик не показывается.</summary>
    public int Count { get; }
    /// <summary>Происхождение для подписи: «вручную», «SCAD», «ЛИРА».</summary>
    public string? OriginText { get; }
    /// <summary>Узловые связи с переменной жёсткостью: значения показывает мозаика, правка группой недоступна.</summary>
    public bool IsMosaic { get; init; }
    /// <summary>Закреплённые DOF узловой связи (биты 0–5: X Y Z UX UY UZ).</summary>
    public int Mask { get; init; }
    /// <summary>Жёсткости пружин узловой связи X…UZ, Н/м и Н·м/рад.</summary>
    public IReadOnlyList<double> Stiffnesses { get; init; } = new double[6];

    /// <summary>Параметры линейной связи типа (маска, поворот кромки, погонные жёсткости); null — не линейная связь.</summary>
    public FemLineSupport? LineSupport { get; init; }
    /// <summary>Линейные связи, потерявшие грань (контур изменён или КонЭ удалён): их можно только удалить.</summary>
    public bool IsLost { get; init; }
    /// <summary>Общая длина граней линейной связи, м.</summary>
    public double LengthM { get; init; }

    /// <summary>Параметры можно изменить диалогом сразу для всех узлов или граней группы.</summary>
    public bool IsEditable => Kind == FemBoundaryKind.NodalSupports && !IsMosaic || Kind == FemBoundaryKind.LineSupports && !IsLost;

    public string CountText => Kind == FemBoundaryKind.LineSupports && !IsLost
        ? string.Format(CultureInfo.CurrentCulture, Loc.S("FemBoundaryLineCount"), Count, LengthM)
        : Count > 0 ? $" — {Count}" : "";

    internal FemBoundaryGroupItem(FemBoundaryKind kind, FemSchema schema, string label, int count, string? originText,
        Func<FemBoundaryTargets> targets)
    {
        Kind = kind;
        Schema = schema;
        Label = label;
        Count = count;
        OriginText = originText;
        _targets = targets;
    }

    /// <summary>Узлы и КЭ, на которых заданы ГУ группы.</summary>
    public FemBoundaryTargets Targets() => _targets();
}

/// <summary>Ветка одного вида ГУ («Узловые связи», «Шарниры стержней»…) с типами; пустая скрыта.</summary>
public sealed class FemBoundaryCategoryNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemBoundaryKind Kind { get; }
    public FemSchema Schema { get; }
    public FemTreeItems<FemBoundaryGroupItem> Groups { get; } = [];

    int _count;
    /// <summary>Число объектов (узлов, КЭ, жёстких тел).</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (_count == value) return;
            _count = value;
            Notify(nameof(Count));
            Notify(nameof(IsVisible));
        }
    }

    public override bool IsVisible => _count > 0;

    internal FemBoundaryCategoryNode(FemBoundaryKind kind, FemSchema schema)
    {
        Kind = kind;
        Schema = schema;
    }

    /// <param name="count">Число объектов; null — сумма по группам (жёсткое тело без счётчика — одно).</param>
    internal void Reset(IReadOnlyList<FemBoundaryGroupItem> groups, int? count = null)
    {
        Groups.Reset(groups);
        Count = count ?? groups.Sum(g => Kind == FemBoundaryKind.RigidBodies ? Math.Max(g.Count, 1) : g.Count);
    }

    /// <summary>Объекты всех групп ветки.</summary>
    public FemBoundaryTargets Targets() => FemBoundaryTree.Merge(Groups.Select(g => g.Targets()));

    void Notify(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>
/// ГУ схемы в дереве. Подузел «Граничные условия» — узловые связи (жёсткие и упругие, уровня КонЭ и сетки) и упругое
/// основание пластин (строка-мозаика); шарниры стержней и жёсткие тела — отдельные ветки схемы (<see cref="Releases"/>,
/// <see cref="RigidBodies"/>), перечитываются вместе. Пустые скрыты.
/// </summary>
public sealed class FemBoundarySubNode : FemSubNode, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FemSchema Schema { get; }
    public FemBoundaryCategoryNode NodalSupports { get; }
    /// <summary>Линейные связи на гранях пластинчатых КонЭ; Count — число граней.</summary>
    public FemBoundaryCategoryNode LineSupports { get; }
    /// <summary>Упругое основание пластин: без дочерних, Count — число КЭ с C1; «Показать на схеме» — мозаика C1.</summary>
    public FemBoundaryCategoryNode Foundation { get; }
    public FemBoundaryCategoryNode Releases { get; }
    public FemBoundaryCategoryNode RigidBodies { get; }
    public FemBoundaryCategoryNode[] Children { get; }

    bool _visible;
    public override bool IsVisible => _visible;

    internal FemBoundarySubNode(FemSchema schema)
    {
        Schema = schema;
        NodalSupports = new(FemBoundaryKind.NodalSupports, schema);
        LineSupports = new(FemBoundaryKind.LineSupports, schema);
        Foundation = new(FemBoundaryKind.Foundation, schema);
        Releases = new(FemBoundaryKind.Releases, schema);
        RigidBodies = new(FemBoundaryKind.RigidBodies, schema);
        Children = [NodalSupports, LineSupports, Foundation];
    }

    // ГУ сетки (пишутся в БД сразу) — для обновления по сессии редактора без повторного чтения тысяч пружин.
    List<FemMeshNodeSupport> _meshSupports = [];
    List<FemSpring> _springs = [];

    /// <summary>Перечитывает ГУ схемы из БД.</summary>
    internal void Reload(DatabaseService db, IReadOnlyList<FemNode> nodes)
    {
        int id = Schema.Id;
        _meshSupports = db.GetFemMeshNodeSupports(id);
        _springs = db.GetFemSprings(id);
        ResetSupports(db, nodes, db.GetFemMembers(id), db.GetFemLineSupports(id));
        Foundation.Reset([], db.GetFemFoundationElementCount(id));
        Releases.Reset(db.GetFemElementReleaseGroups(id).Select(g => FemBoundaryTree.ReleaseGroup(Schema, g.ReleaseI, g.ReleaseJ,
            g.Count, () => new([], [], db.GetFemElementTagsByReleases(id, g.ReleaseI, g.ReleaseJ)))).ToList());
        RigidBodies.Reset(FemBoundaryTree.RigidBodyItems(Schema, db.GetFemRigidBodies(id)));
        UpdateVisible();
    }

    /// <summary>
    /// Узловые и линейные связи по несохранённой сессии редактора схемы — тип появляется и меняется в дереве сразу после
    /// команды; ГУ сетки — из последнего чтения БД.
    /// </summary>
    internal void ReloadSupports(DatabaseService db, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        IReadOnlyList<FemLineSupport> lineSupports)
    {
        ResetSupports(db, nodes, members, lineSupports);
        UpdateVisible();
    }

    void ResetSupports(DatabaseService db, IReadOnlyList<FemNode> nodes, IReadOnlyList<FemMember> members,
        IReadOnlyList<FemLineSupport> lineSupports)
    {
        NodalSupports.Reset(FemBoundaryTree.NodalGroups(Schema, nodes, _meshSupports, _springs));
        LineSupports.Reset(lineSupports.Count == 0 ? [] : FemBoundaryTree.LineGroups(Schema, lineSupports,
            FemLineSupportGeometry.EdgesByMember(members, db.GetPlanarRegions(Schema.Id))));
    }

    void UpdateVisible()
    {
        bool visible = Children.Any(c => c.IsVisible);
        if (visible == _visible) return;
        _visible = visible;
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
    }
}

/// <summary>Группировка и подписи ГУ для дерева схемы.</summary>
public static class FemBoundaryTree
{
    /// <summary>Больше различных наборов жёсткостей с одними масками — узловые связи показываются одной строкой-мозаикой
    /// (переменное основание импорта: у каждого узла своя жёсткость — тысячи строк дерева).</summary>
    public const int MaxExactGroups = 12;
    /// <summary>Больше жёстких тел — они группируются по виду, маске и происхождению.</summary>
    public const int MaxRigidBodyItems = 50;

    static readonly string[] ReleaseNames = ["N", "Qy", "Qz", "T", "My", "Mz"];
    static readonly string[] SpringNames = ["Kx", "Ky", "Kz", "Kux", "Kuy", "Kuz"];

    /// <summary>Узловая связь одного узла: закрепление и пружины.</summary>
    sealed class NodalEntry(string tag, bool mesh, string origin)
    {
        public string Tag { get; } = tag;
        public bool Mesh { get; } = mesh;
        public string Origin { get; set; } = origin;
        public int Mask { get; set; }
        public double[] K { get; } = new double[6];

        public int SpringMask => Enumerable.Range(0, 6).Where(i => K[i] != 0).Aggregate(0, (m, i) => m | 1 << i);

        public (double, double, double, double, double, double) Stiffness => (K[0], K[1], K[2], K[3], K[4], K[5]);
    }

    /// <summary>
    /// Узловые связи: закрепления (<see cref="FemNode.DofMask"/>, <see cref="FemMeshNodeSupport"/>) и пружины узла —
    /// одна связь на узел. Типы — по маске, жёсткостям и происхождению; если наборов жёсткостей с одними масками больше
    /// <see cref="MaxExactGroups"/>, они сводятся в строку-мозаику. Записи с нулевой маской без пружин (отметка ручного
    /// узла сетки) не показываются.
    /// </summary>
    public static List<FemBoundaryGroupItem> NodalGroups(FemSchema schema, IEnumerable<FemNode> nodes,
        IEnumerable<FemMeshNodeSupport> meshSupports, IEnumerable<FemSpring> springs)
    {
        var entries = new Dictionary<(bool Mesh, string Tag), NodalEntry>();
        NodalEntry Get(bool mesh, string tag, string origin)
        {
            if (!entries.TryGetValue((mesh, tag), out var e)) entries[(mesh, tag)] = e = new NodalEntry(tag, mesh, origin);
            return e;
        }
        foreach (var n in nodes.Where(n => n.DofMask != 0))
            Get(false, n.NodeTag, FemLoadOrigin.Manual).Mask = n.DofMask & FemBoundaryDofs.All;
        foreach (var s in meshSupports.Where(s => (s.Mask & FemBoundaryDofs.All) != 0))
            Get(true, s.NodeTag, s.Origin).Mask = s.Mask & FemBoundaryDofs.All;
        foreach (var s in springs.Where(s => s.ActiveMask != 0))
        {
            var e = Get(s.TargetKind == FemSpringTargetKinds.MeshNode, s.NodeTag, s.Origin);
            var k = s.Stiffnesses;
            for (int i = 0; i < 6; i++)
                if (k[i] != 0) e.K[i] = k[i];
        }

        var result = new List<FemBoundaryGroupItem>();
        var byMasks = entries.Values
            .GroupBy(e => (e.Mask, e.SpringMask, e.Origin))
            .OrderBy(g => g.Key.SpringMask != 0).ThenBy(g => SupportOrder(g.Key.Mask)).ThenBy(g => g.Key.Mask)
            .ThenBy(g => g.Key.SpringMask).ThenBy(g => OriginOrder(g.Key.Origin));
        foreach (var g in byMasks)
        {
            string origin = FemMeshNodeRow.DescribeOrigin(g.Key.Origin);
            var sets = g.GroupBy(e => e.Stiffness).OrderBy(x => x.Key).ToList();
            if (sets.Count > MaxExactGroups)
            {
                var all = g.ToList();
                string label = string.Format(Loc.S("FemBoundaryVariableSprings"),
                    string.Join(" ", SpringNames.Where((_, i) => (g.Key.SpringMask & (1 << i)) != 0)));
                if (g.Key.Mask != 0) label = SupportLabel(g.Key.Mask) + "; " + label;
                result.Add(new FemBoundaryGroupItem(FemBoundaryKind.NodalSupports, schema, label, all.Count, origin,
                    () => Targets(all)) { IsMosaic = true, Mask = g.Key.Mask });
                continue;
            }
            foreach (var set in sets)
            {
                var list = set.ToList();
                var k = list[0].K;
                result.Add(new FemBoundaryGroupItem(FemBoundaryKind.NodalSupports, schema, NodalLabel(g.Key.Mask, k),
                    list.Count, origin, () => Targets(list)) { Mask = g.Key.Mask, Stiffnesses = k });
            }
        }
        return result;
    }

    static FemBoundaryTargets Targets(List<NodalEntry> list) => new(
        list.Where(e => !e.Mesh).Select(e => e.Tag).ToList(), list.Where(e => e.Mesh).Select(e => e.Tag).ToList(), []);

    /// <summary>
    /// Линейные связи: типы по маске, повороту кромки, погонным жёсткостям и происхождению (как типы линейных опор
    /// RFEM), счётчик — граней и их длина; связи, потерявшие грань, — отдельной строкой.
    /// </summary>
    public static List<FemBoundaryGroupItem> LineGroups(FemSchema schema, IEnumerable<FemLineSupport> supports,
        IReadOnlyDictionary<string, List<FemPlateEdge>> edgesByMember)
    {
        var matched = new List<(FemLineSupport Support, FemPlateEdge Edge)>();
        var lost = new List<FemLineSupport>();
        foreach (var s in supports.Where(s => s.IsActive))
        {
            if (FemLineSupportGeometry.Match(s, edgesByMember.GetValueOrDefault(s.MemberTag)) is { } edge) matched.Add((s, edge));
            else lost.Add(s);
        }
        var result = matched
            .GroupBy(x => (x.Support.Mask & FemBoundaryDofs.All, x.Support.FixEdgeRotation, x.Support.Kx, x.Support.Ky,
                x.Support.Kz, x.Support.Kux, x.Support.Kuy, x.Support.Kuz, x.Support.Origin))
            .OrderBy(g => g.Key.Item1 == 0).ThenBy(g => SupportOrder(g.Key.Item1)).ThenBy(g => g.Key.Item1)
            .ThenByDescending(g => g.Key.FixEdgeRotation).ThenBy(g => OriginOrder(g.Key.Origin))
            .Select(g =>
            {
                var list = g.ToList();
                var template = list[0].Support;
                return new FemBoundaryGroupItem(FemBoundaryKind.LineSupports, schema, LineLabel(template), list.Count,
                    FemMeshNodeRow.DescribeOrigin(g.Key.Origin), () => new([], [], [], list.Select(x => x.Edge.Key).ToList()))
                {
                    LineSupport = template, LengthM = list.Sum(x => x.Edge.Length), Mask = template.Mask,
                    Stiffnesses = template.Stiffnesses,
                };
            }).ToList();
        if (lost.Count > 0)
            result.Add(new FemBoundaryGroupItem(FemBoundaryKind.LineSupports, schema, Loc.S("FemBoundaryLineLost"), lost.Count,
                null, () => new([], [], [], lost.Select(s => s.Key).ToList())) { IsLost = true });
        return result;
    }

    /// <summary>«Шарнирное опирание», «Tz; поворот кромки», «Kz′ = 50000 кН/м²».</summary>
    public static string LineLabel(FemLineSupport s)
    {
        int mask = s.Mask & FemBoundaryDofs.All;
        string support = mask == 0b111 && s.FixEdgeRotation
            ? Loc.S("FemLineSupportPresetPlate")
            : string.Join("; ", new[] { SupportLabel(mask), s.FixEdgeRotation ? Loc.S("FemBoundaryLineEdgeRotation") : "" }
                .Where(x => x.Length > 0));
        var k = s.Stiffnesses;
        string springs = string.Join("; ", Enumerable.Range(0, 6).Where(i => k[i] != 0)
            .Select(i => $"{SpringNames[i]}′ = {Number(k[i] / 1000)} {Loc.S(i < 3 ? "FemBoundaryUnitKLine" : "FemBoundaryUnitKrLine")}"));
        return string.Join("; ", new[] { support, springs }.Where(x => x.Length > 0));
    }

    /// <summary>«Заделка», «Tz; Kx = 100 кН/м», «Kz = 5000 кН/м».</summary>
    public static string NodalLabel(int mask, IReadOnlyList<double> k)
    {
        string springs = SpringLabel(k);
        if (mask == 0) return springs;
        return springs.Length == 0 ? SupportLabel(mask) : SupportLabel(mask) + "; " + springs;
    }

    /// <summary>Группа КЭ с одинаковыми освобождениями концов (ГУ импортных КЭ — источник схемы).</summary>
    public static FemBoundaryGroupItem ReleaseGroup(FemSchema schema, int releaseI, int releaseJ, int count,
        Func<FemBoundaryTargets> targets) =>
        new(FemBoundaryKind.Releases, schema, ReleaseLabel(releaseI, releaseJ), count, SchemaOrigin(schema), targets);

    /// <summary>Жёсткие тела и объединения — по одному; больше <see cref="MaxRigidBodyItems"/> — группами по виду,
    /// маске и происхождению.</summary>
    public static List<FemBoundaryGroupItem> RigidBodyItems(FemSchema schema, IEnumerable<FemRigidBody> bodies)
    {
        var list = bodies.ToList();
        if (list.Count <= MaxRigidBodyItems) return list.Select(b => RigidBodyItem(schema, b)).ToList();
        return list.GroupBy(b => (b.IsCoupled, b.Mask, b.Origin))
            .OrderBy(g => g.Key.IsCoupled).ThenByDescending(g => g.Key.Mask).ThenBy(g => OriginOrder(g.Key.Origin))
            .Select(g =>
            {
                var group = g.ToList();
                string label = string.Format(Loc.S("FemBoundaryRigidBodyGroupLabel"),
                    Loc.S(g.Key.IsCoupled ? "FemBoundaryCoupledPlural" : "FemBoundaryRigidBodyPlural"),
                    FemBoundaryDofs.Describe(g.Key.Mask));
                return new FemBoundaryGroupItem(FemBoundaryKind.RigidBodies, schema, label, group.Count,
                    FemMeshNodeRow.DescribeOrigin(g.Key.Origin), () => new([],
                        group.SelectMany(b => b.SlaveNodeTags.Prepend(b.MasterNodeTag)).Distinct(StringComparer.Ordinal).ToList(), []));
            }).ToList();
    }

    static FemBoundaryGroupItem RigidBodyItem(FemSchema schema, FemRigidBody b)
    {
        string name = Loc.S(b.IsCoupled ? "FemBoundaryCoupled" : "FemBoundaryRigidBody");
        string number = b.SourceElemTag ?? b.Id.ToString(CultureInfo.InvariantCulture);
        string label = string.Format(Loc.S("FemBoundaryRigidBodyLabel"), name, number, b.MasterNodeTag,
            b.SlaveNodeTags.Count, FemBoundaryDofs.Describe(b.Mask));
        FemBoundaryTargets targets = new([], [b.MasterNodeTag, .. b.SlaveNodeTags], []);
        return new FemBoundaryGroupItem(FemBoundaryKind.RigidBodies, schema, label, 0,
            FemMeshNodeRow.DescribeOrigin(b.Origin), () => targets);
    }

    /// <summary>Объединение объектов нескольких групп без повторов.</summary>
    public static FemBoundaryTargets Merge(IEnumerable<FemBoundaryTargets> parts)
    {
        var list = parts.ToList();
        return new(
            list.SelectMany(t => t.NodeTags).Distinct(StringComparer.Ordinal).ToList(),
            list.SelectMany(t => t.MeshNodeTags).Distinct(StringComparer.Ordinal).ToList(),
            list.SelectMany(t => t.MeshElemTags).Distinct(StringComparer.Ordinal).ToList(),
            list.SelectMany(t => t.PlateEdges).Distinct().ToList());
    }

    /// <summary>«Заделка», «Шарнир» или перечень DOF (Tz, Tx Ty Tz Rz).</summary>
    public static string SupportLabel(int mask)
    {
        string text = FemNodeTreeItem.SupportsText(mask);
        return text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }

    /// <summary>Ненулевые жёсткости: «Kz = 5000 кН/м; Kux = 20 кН·м/рад».</summary>
    public static string SpringLabel(IReadOnlyList<double> k) =>
        string.Join("; ", Enumerable.Range(0, 6).Where(i => k[i] != 0)
            .Select(i => $"{SpringNames[i]} = {Number(k[i] / 1000)} {Loc.S(i < 3 ? "FemBoundaryUnitK" : "FemBoundaryUnitKr")}"));

    /// <summary>«I: My Mz; J: My Mz» в местных осях стержня; жёсткий конец — «—».</summary>
    public static string ReleaseLabel(int releaseI, int releaseJ) => $"I: {ReleaseText(releaseI)}; J: {ReleaseText(releaseJ)}";

    static string ReleaseText(int mask) => mask == 0
        ? "—"
        : string.Join(" ", ReleaseNames.Where((_, i) => (mask & (1 << i)) != 0));

    /// <summary>Ручные — первыми.</summary>
    static int OriginOrder(string origin) => origin == FemLoadOrigin.Manual ? 0 : 1;

    static string? SchemaOrigin(FemSchema schema) => schema.SourceType is "scad" or "lira"
        ? FemMeshNodeRow.DescribeOrigin(FemLoadOrigin.Import(schema.SourceType)) : null;

    /// <summary>Заделки, затем шарниры, затем прочие маски.</summary>
    static int SupportOrder(int mask) => mask switch { FemBoundaryDofs.All => 0, 7 => 1, _ => 2 };

    static string Number(double value) => value.ToString("G6", CultureInfo.CurrentCulture);
}

/// <summary>
/// Списки объектов типа ГУ для правки текстом, как поле «Узлы №» RFEM: узлы — «1-4 7 12» (числовые теги сворачиваются
/// в диапазоны), грани — «КонЭ/сторона» и «КонЭ/о отверстие/сторона» («12/3 12/о1/2», стороны и отверстия с 1).
/// </summary>
public static class FemBoundaryObjectList
{
    static readonly char[] Separators = [' ', ',', ';', '\t', '\r', '\n'];

    /// <summary>Теги узлов: числовые по возрастанию с диапазонами, затем прочие.</summary>
    public static string FormatTags(IEnumerable<string> tags)
    {
        var list = tags.Distinct(StringComparer.Ordinal).ToList();
        var numbers = list.Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : (int?)null)
            .Where(n => n != null).Select(n => n!.Value).Distinct().Order().ToList();
        var parts = new List<string>();
        for (int i = 0; i < numbers.Count;)
        {
            int j = i;
            while (j + 1 < numbers.Count && numbers[j + 1] == numbers[j] + 1) j++;
            parts.Add(j - i >= 2 ? $"{numbers[i]}-{numbers[j]}"
                : string.Join(" ", numbers.Skip(i).Take(j - i + 1)));
            i = j + 1;
        }
        parts.AddRange(list.Where(t => !int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Order(StringComparer.Ordinal));
        return string.Join(" ", parts);
    }

    /// <summary>Теги узлов из текста; диапазон «a-b» — числовые теги от a до b. null и <paramref name="error"/> — ошибка.</summary>
    public static List<string>? ParseTags(string text, out string? error)
    {
        error = null;
        var result = new List<string>();
        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            int dash = token.IndexOf('-', 1);
            if (dash > 0)
            {
                if (!int.TryParse(token[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out int a) ||
                    !int.TryParse(token[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int b) || b < a ||
                    b - a > 1_000_000)
                {
                    error = string.Format(Loc.S("FemBoundaryObjectsBadRange"), token);
                    return null;
                }
                for (int n = a; n <= b; n++) result.Add(n.ToString(CultureInfo.InvariantCulture));
            }
            else result.Add(token);
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>«12/3» — сторона 3 внешнего контура КонЭ 12, «12/о1/2» — сторона 2 отверстия 1.</summary>
    public static string FormatEdge(FemPlateEdgeKey key) => key.Loop == BoundaryLoop.Outer
        ? $"{key.MemberTag}/{key.StartVertex + 1}"
        : $"{key.MemberTag}/о{key.HoleIndex + 1}/{key.StartVertex + 1}";

    public static string FormatEdges(IEnumerable<FemPlateEdgeKey> keys) => string.Join(" ", keys.Distinct()
        .OrderBy(k => int.TryParse(k.MemberTag, out int n) ? n : int.MaxValue).ThenBy(k => k.MemberTag, StringComparer.Ordinal)
        .ThenBy(k => k.Loop).ThenBy(k => k.HoleIndex).ThenBy(k => k.StartVertex).Select(FormatEdge));

    /// <summary>Грани из текста (отверстие — «о», «o» или «h» с номером). null и <paramref name="error"/> — ошибка.</summary>
    public static List<FemPlateEdgeKey>? ParseEdges(string text, out string? error)
    {
        error = null;
        var result = new List<FemPlateEdgeKey>();
        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split('/');
            FemPlateEdgeKey? key = parts.Length switch
            {
                2 when Positive(parts[1]) is int side => new(parts[0], BoundaryLoop.Outer, 0, side - 1),
                3 when parts[1].Length > 1 && "оOoHhО".Contains(parts[1][0]) && Positive(parts[1][1..]) is int hole &&
                       Positive(parts[2]) is int side => new(parts[0], BoundaryLoop.Hole, hole - 1, side - 1),
                _ => null,
            };
            if (key == null || parts[0].Length == 0)
            {
                error = string.Format(Loc.S("FemBoundaryObjectsBadEdge"), token);
                return null;
            }
            result.Add(key.Value);
        }
        return result.Distinct().ToList();
    }

    static int? Positive(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;
}

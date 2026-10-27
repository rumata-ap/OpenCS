namespace CScore.Fem.Fragment;

/// <summary>
/// Значения нагрузки-поля на одном КЭ опорной сетки; формат — как <see cref="FemElementLoad.Values"/> вида поля:
/// <c>uniform</c> [q], <c>nodal</c> [q1…qn] по узлам КЭ опорной сетки, <c>edge</c> [i, j, q1, q2] (номера узлов КЭ
/// опорной сетки с 1).
/// </summary>
public sealed record FragmentFieldEntry(string RefTag, IReadOnlyList<double> Values);

/// <summary>
/// Нагрузка-поле фрагмента (спека разд. 4): загружение (Id родителя), вид (<c>uniform</c>/<c>nodal</c>/<c>edge</c>),
/// ось глобальной системы координат и значения по тегам опорной сетки. Переживает пересетку: новый КЭ берёт значения
/// своего КЭ родителя (<see cref="FragmentFieldLoadResolver"/>).
/// </summary>
public sealed record FragmentFieldLoad(int LoadCaseId, string LoadKind, string Axis, IReadOnlyList<FragmentFieldEntry> Entries)
{
    /// <summary>Система координат значений — всегда глобальная.</summary>
    public string CoordinateSystem => "global";
}

/// <summary>Нагрузка КонЭ-стержня фрагмента: тег КонЭ фрагмента; <c>LoadCaseId</c> — Id загружения родителя.</summary>
public sealed record FragmentMemberLoad(string MemberTag, FemMemberLoad Load);

/// <summary>Узловая нагрузка фрагмента: тег узла схемы фрагмента; <c>LoadCaseId</c> — Id загружения родителя.</summary>
public sealed record FragmentNodeLoad(string NodeTag, FemNodeLoad Load);

/// <summary>Заданное перемещение узла фрагмента: тег узла схемы фрагмента; <c>LoadCaseId</c> — Id загружения родителя.</summary>
public sealed record FragmentKinematicLoad(string NodeTag, FemKinematicLoad Load);

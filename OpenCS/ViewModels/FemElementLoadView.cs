using System.Globalization;
using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка списка нагрузок на КЭ загружения: вид, значение, направление, цель, происхождение.</summary>
public sealed class FemElementLoadView(FemElementLoad load, IReadOnlyList<FemMemberGroup> groups)
{
    public FemElementLoad Load { get; } = load;

    /// <summary>Нагрузка задана вручную (импортную можно только удалить).</summary>
    public bool IsManual => Load.Origin == FemLoadOrigin.Manual;

    public string Text
    {
        get
        {
            var v = Load.Values;
            string value = Load.LoadKind switch
            {
                FemElementLoadKinds.SelfWeight => v.Count > 0 ? $"× {Num(v[0])}" : "",
                FemElementLoadKinds.Point => v.Count > 0 ? $"{Num(v[0] / 1e3)} {Loc.S("FemUnitKN")}" : "",
                FemElementLoadKinds.Nodal => v.Count > 0 ? $"{Num(v.Min() / 1e3)}…{Num(v.Max() / 1e3)}" : "",
                _ => v.Count > 0 ? Num(v[0] / 1e3) : "",
            };
            string direction = Load.LoadKind == FemElementLoadKinds.SelfWeight ? ""
                : Load.IsLocal && Load.Axis == "z" ? " " + Loc.S("FemAreaLoadDirNormal")
                : $" {(Load.IsLocal ? Load.Axis.ToLowerInvariant() : Load.Axis.ToUpperInvariant())}";
            string target = Load.TargetKind switch
            {
                FemLoadTargetKinds.Group => string.Format(Loc.S("FemAreaLoadTargetGroup"),
                    groups.FirstOrDefault(g => g.Id == Load.GroupId)?.Tag ?? $"#{Load.GroupId}"),
                FemLoadTargetKinds.Members => string.Format(Loc.S("FemAreaLoadTargetMembers"), Sample(Load.TargetTags)),
                _ => string.Format(Loc.S("FemAreaLoadTargetElements"), Sample(Load.TargetTags)),
            };
            string origin = IsManual ? "" : $" · {Load.Origin.Replace(FemLoadOrigin.ImportPrefix, "")}";
            return $"{Loc.S("FemElementLoadKind_" + Load.LoadKind)} {value}{direction} · {target}{origin}";
        }
    }

    public override string ToString() => Text;

    static string Num(double x) => x.ToString("0.###", CultureInfo.CurrentCulture);

    static string Sample(IReadOnlyList<string> tags) =>
        tags.Count <= 4 ? string.Join(", ", tags) : $"{string.Join(", ", tags.Take(4))} … ({tags.Count})";
}

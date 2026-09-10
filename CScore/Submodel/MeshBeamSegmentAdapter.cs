using System.Text.Json;
using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

public sealed record MeshBeamSegmentAdapterResult(IReadOnlyList<BeamSegmentInput> Segments, IReadOnlyList<EnvironmentElement> Environment, PlanarVector3? PreferredDirection, IReadOnlyList<FemValidationDiagnostic> Diagnostics);

/// <summary>Разворачивает mesh-снимок в нормализованные сегменты; единственный тип ядра, знающий FEM-модель.</summary>
public static class MeshBeamSegmentAdapter
{
    public static MeshBeamSegmentAdapterResult Build(IReadOnlyCollection<string> selectedElemTags,
        IReadOnlyList<FemElement> meshElements, IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemMember> members)
    {
        var diagnostics=new List<FemValidationDiagnostic>(); var points=meshNodes.ToDictionary(n=>n.NodeTag,n=>new PlanarVector3(n.X,n.Y,n.Z),StringComparer.Ordinal);
        var requested=new HashSet<string>(selectedElemTags,StringComparer.Ordinal);
        if(requested.Count!=selectedElemTags.Count)diagnostics.Add(new("chain_adapter_skipped",$"В выборе повторяются теги элементов; повторы объединены ({selectedElemTags.Count} → {requested.Count}).",false,selectedElemTags.Distinct(StringComparer.Ordinal).ToList()));
        var expanded=requested.SelectMany(tag=>meshElements.Where(e=>e.SourceMemberTag==tag).Select(e=>e.ElemTag)).ToHashSet(StringComparer.Ordinal);
        var selected=expanded.Count>0?expanded:requested;
        if(expanded.Count>0)diagnostics.Add(new("chain_selection_expanded",$"Выбранные конструктивные элементы развёрнуты в {expanded.Count} элементов расчётной сетки.",false,requested.ToList()));
        var known=meshElements.Select(e=>e.ElemTag).ToHashSet(StringComparer.Ordinal); foreach(var tag in selected.Where(t=>!known.Contains(t)))diagnostics.Add(new("chain_adapter_skipped",$"Выбранный элемент {tag} отсутствует в расчётной сетке схемы.",false,[tag]));
        var memberByTag=members.Where(m=>!string.IsNullOrEmpty(m.ElemTag)).GroupBy(m=>m.ElemTag,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        var segments=new List<BeamSegmentInput>(); var environment=new List<EnvironmentElement>();
        foreach(var element in meshElements)
        {
            var chosen=selected.Contains(element.ElemTag);
            if(!TryTags(element.NodeIdsJson,out var tags)){diagnostics.Add(new("chain_input_invalid",$"Элемент {element.ElemTag}: список узлов повреждён и не может быть прочитан.",true,[element.ElemTag]));continue;}
            var resolved=tags.Select(tag=>points.TryGetValue(tag.ToString(),out var point)?(true,point):(false,default(PlanarVector3))).ToList();
            if(resolved.Any(p=>!p.Item1)){if(chosen)diagnostics.Add(new("chain_adapter_skipped",$"Элемент {element.ElemTag} ссылается на узел, отсутствующий в сетке.",false,[element.ElemTag]));continue;}
            var nodePoints=resolved.Select(p=>p.Item2).ToList();
            if(!chosen){if(nodePoints.Count>=2)environment.Add(new(element.ElemTag,element.ElemType=="shell"?EnvironmentElementKind.Shell:EnvironmentElementKind.Beam,nodePoints));continue;}
            if(element.ElemType!="beam"||nodePoints.Count!=2){diagnostics.Add(new("chain_adapter_skipped",$"Элемент {element.ElemTag} не является двухузловым стержнем и в анализе не участвует.",false,[element.ElemTag]));continue;}
            var beta=element.SourceMemberTag is { } memberTag&&memberByTag.TryGetValue(memberTag,out var member)?(member.RotationDeg,BetaSource.Member):(0d,BetaSource.Absent);
            segments.Add(new(element.ElemTag,nodePoints[0],nodePoints[1],beta.Item1,beta.Item2,element.SourceMemberTag));
        }
        return new(segments,environment,Preferred(segments,memberByTag,points),diagnostics);
    }
    static bool TryTags(string? json,out IReadOnlyList<int> tags){tags=[];if(string.IsNullOrWhiteSpace(json))return false;try{tags=JsonSerializer.Deserialize<int[]>(json)??[];return true;}catch(Exception e)when(e is JsonException or ArgumentNullException or NotSupportedException){return false;}}
    static PlanarVector3? Preferred(IReadOnlyList<BeamSegmentInput> segments,IReadOnlyDictionary<string,FemMember> members,IReadOnlyDictionary<string,PlanarVector3> points)
    {
        var tags=segments.Select(s=>s.SourceMemberTag).Distinct().ToList(); if(tags.Count!=1||tags[0] is not { } tag||!members.TryGetValue(tag,out var member)||!TryTags(member.NodeIdsJson,out var ids)||ids.Count<2||!points.TryGetValue(ids[0].ToString(),out var a)||!points.TryGetValue(ids[1].ToString(),out var b))return null; var d=b-a;return d.Length>0?d.Normalize():null;
    }
}

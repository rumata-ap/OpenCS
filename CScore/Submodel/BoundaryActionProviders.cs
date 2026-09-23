using CScore.Fem;
using CScore.Planar;

namespace CScore.Submodel;

/// <summary>Перевод локальных концевых усилий стержня в глобальную систему по конвенции OpenSees.</summary>
public static class EndForceTransform
{
    /// <summary>
    /// Кадр — как у генератора линейной модели: x от узла i к узлу j mesh-элемента, β — угол исходного
    /// стержня (<see cref="BeamLocalAxisConvention"/>; локальная y OpenSees = vecxz × x совпадает с Y).
    /// Возвращает <c>F = x·N + y·Qy + z·Qz</c>, <c>M = x·Mx + y·My + z·Mz</c> нужного конца
    /// без смены знака — конвенция <c>localForce</c> сохраняется.
    /// </summary>
    public static Dof6 ToGlobal(BeamEndForces forces, bool atJ, PlanarVector3 nodeI, PlanarVector3 nodeJ, double betaDeg)
    {
        var (x, y, z) = BeamLocalAxisConvention.Frame(nodeI, nodeJ, betaDeg);
        var local = atJ ? forces.J : forces.I;
        return Dof6.FromParts(
            x * local.X + y * local.Y + z * local.Z,
            x * local.Rx + y * local.Ry + z * local.Rz);
    }
}

/// <summary>Действия на одном конце цепочки, собранные провайдерами.</summary>
/// <param name="BoundaryVector">Граничный вектор <c>P_e − Σ p_d</c>; null — силовой вклад не определён.</param>
/// <param name="RetainedResistance">Контрольный остаток <c>+Σ p_r</c> выбранных КЭ; null — усилий нет.</param>
public sealed record EndActions(
    bool AtStart,
    string ParentNodeTag,
    Dof6? BoundaryVector,
    IReadOnlyList<InterfaceActionContribution> Contributions,
    Dof6? RetainedResistance,
    Dof6? Reaction,
    Dof6? Displacement,
    IReadOnlyList<string> UnsupportedJunctionTags,
    IReadOnlyList<string> MissingEndForceTags);

/// <summary>
/// Провайдеры граничного действия: концевые усилия отброшенных стержней (<c>−p_d</c>),
/// boundary-узловые нагрузки, реакции и перемещения родителя; плюс контрольное сопротивление
/// выбранных КЭ. Shell и прочие неподдержанные примыкания делают силовой вклад неопределённым.
/// </summary>
public static class BoundaryActionProviders
{
    public static EndActions Collect(
        bool atStart,
        SubmodelChainTopology chain,
        IReadOnlyList<FemMember> parentMembers,
        IReadOnlyList<FemMeshNode> parentMeshNodes,
        IReadOnlyList<FemElement> parentMeshElements,
        IReadOnlyList<BoundaryNodalLoad> boundaryNodal,
        IParentLinearResult result,
        List<FemValidationDiagnostic> diagnostics)
    {
        string endTag = atStart ? chain.StartParentNodeTag : chain.EndParentNodeTag;
        var meshNodeByTag = new Dictionary<string, FemMeshNode>(StringComparer.Ordinal);
        foreach (var node in parentMeshNodes)
            if (FemMeshTopology.CanonicalNodeTag(node.NodeTag) is string tag) meshNodeByTag.TryAdd(tag, node);
        var memberByTag = new Dictionary<string, FemMember>(StringComparer.Ordinal);
        foreach (var member in parentMembers) memberByTag.TryAdd(member.ElemTag, member);

        var contributions = new List<InterfaceActionContribution>();
        var unsupported = new List<string>();
        var missing = new List<string>();
        Dof6 boundary = Dof6.Zero;
        Dof6? retained = Dof6.Zero;

        foreach (var element in parentMeshElements)
        {
            var tags = FemMeshTopology.ReadNodeTags(element);
            if (tags is null)
            {
                diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                    $"Элемент {element.ElemTag} с нечитаемой связностью не рассматривается как примыкание.", false, [element.ElemTag]));
                continue;
            }
            if (!tags.Contains(endTag)) continue;

            bool selected = chain.SegmentByParentElement.ContainsKey(element.ElemTag);
            if (!selected && element.ElemType != "beam")
            {
                unsupported.Add(element.ElemTag);
                continue;
            }
            if (tags.Count != 2)
            {
                unsupported.Add(element.ElemTag);
                continue;
            }

            Dof6? global = TryGlobalEndForce(element, tags, endTag, meshNodeByTag, memberByTag, result, diagnostics);
            if (selected)
            {
                retained = global is null || retained is null ? null : retained + global;
                continue;
            }
            if (global is null)
            {
                missing.Add(element.ElemTag);
                continue;
            }
            boundary -= global;
            contributions.Add(new InterfaceActionContribution(-global, "beam_end", element.ElemTag,
                "−localForce отброшенного стержня", ConversionQuality.Exact));
        }

        foreach (var load in boundaryNodal.Where(l => l.ParentNodeTag == endTag))
        {
            boundary += load.Load;
            contributions.Add(new InterfaceActionContribution(load.Load, load.Source.Kind,
                load.Source.SourceTag ?? load.Source.SourceId.ToString(), "глобальная нагрузка", ConversionQuality.Exact));
        }

        if (unsupported.Count > 0)
            diagnostics.Add(new(BoundaryScenarioDiagnostics.UnsupportedJunction,
                $"К концу {endTag} примыкают неподдержанные объекты ({string.Join(", ", unsupported)}): силовой вклад не определён.", false, unsupported));
        if (missing.Count > 0)
            diagnostics.Add(new(BoundaryScenarioDiagnostics.MissingEndForces,
                $"Нет концевых усилий отброшенных стержней ({string.Join(", ", missing)}) у конца {endTag}: силовой вклад не определён.", false, missing));

        Dof6? reaction = result.TryGetReaction(endTag, out var r) ? r : null;
        Dof6? displacement = result.TryGetDisplacement(endTag, out var d) ? d : null;
        bool forceDefined = unsupported.Count == 0 && missing.Count == 0;
        return new EndActions(atStart, endTag, forceDefined ? boundary : null, contributions,
            retained, reaction, displacement, unsupported, missing);
    }

    static Dof6? TryGlobalEndForce(FemElement element, IReadOnlyList<string> tags, string endTag,
        Dictionary<string, FemMeshNode> meshNodeByTag, Dictionary<string, FemMember> memberByTag,
        IParentLinearResult result, List<FemValidationDiagnostic> diagnostics)
    {
        if (!result.TryGetEndForces(element.ElemTag, out var forces)) return null;
        if (!meshNodeByTag.TryGetValue(tags[0], out var i) || !meshNodeByTag.TryGetValue(tags[1], out var j)) return null;
        double beta = 0;
        if (element.SourceMemberTag is string memberTag && memberByTag.TryGetValue(memberTag, out var member))
            beta = member.RotationDeg;
        else
            diagnostics.Add(new(BoundaryScenarioDiagnostics.Info,
                $"Элемент {element.ElemTag} без исходного стержня: β принят 0.", false, [element.ElemTag]));
        return EndForceTransform.ToGlobal(forces, atJ: tags[1] == endTag,
            new PlanarVector3(i.X, i.Y, i.Z), new PlanarVector3(j.X, j.Y, j.Z), beta);
    }
}

using CScore.Submodel;

namespace CScore.Fem.Fragment.Masks;

/// <summary>
/// Узловые силы КЭ родителя для масок кромок пластин (спека 2026-11-05, разд. 5.1) — необязательное дополнение к
/// <see cref="IFragmentParentResult"/> с теми же ключами и тегами сетки родителя. Все величины — общие оси, момент
/// относительно узла.
/// </summary>
public interface IFragmentParentNodalForces
{
    /// <summary>
    /// f_int,e − f_нагр,e КЭ пластины или стержня — сила узла на КЭ (как записаны силы концов стержней CSfea) по тегам
    /// узлов КЭ; у пластины f_int,e = K_e·u_e с той же матрицей, что в системе (сверлильная жёсткость, K_σ). false — нет
    /// данных по КЭ или ключу.
    /// </summary>
    bool TryElementNodeForces(FragmentMaskKey key, string elemTag, out IReadOnlyDictionary<string, Dof6> forces);

    /// <summary>Согласованные узловые силы нагрузок КЭ (пластины или стержня) по тегам узлов; пусто — нагрузок нет.</summary>
    IReadOnlyDictionary<string, Dof6> ElementLoads(FragmentMaskKey key, string elemTag);

    /// <summary>Узловые нагрузки (сетки и узлов схемы, кроме нагрузок КЭ) по тегам узлов.</summary>
    IReadOnlyDictionary<string, Dof6> NodeLoads(FragmentMaskKey key);

    /// <summary>Реакции опор по тегам узлов.</summary>
    IReadOnlyDictionary<string, Dof6> Reactions(FragmentMaskKey key);

    /// <summary>
    /// Связи узлов помимо КЭ: АЖТ (ведущий, ведомый); <c>B = null</c> — пружина узла на землю (в т. ч. упругое
    /// основание). Узлы связей не проверяются самопроверкой; связь через ступеньку — предупреждение.
    /// </summary>
    IReadOnlyCollection<(string A, string? B)> NodeLinks { get; }
}

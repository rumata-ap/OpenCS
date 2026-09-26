namespace OpenCS.ViewModels;

/// <summary>
/// Группировка расчётных задач по предельным состояниям для дерева задач:
/// НДС / 1-я ГПС / 2-я ГПС / Огнестойкость / Прочие.
///
/// Это единственная точка классификации: вид задачи, не указанный здесь явно,
/// попадает в «Прочие». Поэтому новый вид задачи добавляется одновременно в
/// <see cref="OpenCS.Tasks.TaskRunner"/>, в список видов диалога задачи и сюда —
/// иначе он окажется в «Прочих» вопреки своей группе ГПС.
/// </summary>
public static class CalcTaskGroups
{
    /// <summary>Группа «НДС» — напряжённо-деформированное состояние.</summary>
    public const string Nds = "nds";

    /// <summary>Группа «1-я ГПС» — расчёт по прочности и несущей способности.</summary>
    public const string Uls = "uls";

    /// <summary>Группа «2-я ГПС» — расчёт по трещиностойкости и деформациям.</summary>
    public const string Sls = "sls";

    /// <summary>Группа «Огнестойкость».</summary>
    public const string Fire = "fire";

    /// <summary>Группа «Прочие» — задачи, не относящиеся к перечисленным группам.</summary>
    public const string Other = "other";

    /// <summary>Возвращает группу предельного состояния для вида расчётной задачи.</summary>
    public static string Classify(string kind) => kind switch
    {
        "strain_state"
            or "strain_state_batch"
            or "two_stage_strain"
            or "two_stage_strain_batch"
            or "shell_strain_state"
            or "shell_strain_state_batch"                             => Nds,

        "limit_force"          or "limit_force_batch"
            or "limit_moment"  or "limit_moment_batch"
            or "limit_axial"   or "limit_axial_batch"
            or "strength_ndm_batch" or "rc_check"
            or "shell_simpl_wa_uls"   or "shell_simpl_wa_uls_batch"
            or "shell_simpl_capri_uls" or "shell_simpl_capri_uls_batch"
            or "shell_layered_uls"     or "shell_layered_uls_batch"
            or "steel_check"
            or "steel_central_compression" or "steel_central_tension"
            or "steel_bending" or "steel_compression_bending"
            or "steel_tension_bending" or "steel_shear"
            or "steel_torsion"
            or "shear_inclined" or "shear_inclined_batch"
            or "sp63_normal"                                          => Uls,

        "shell_simpl_wa_sls"    or "shell_simpl_wa_sls_batch"
            or "shell_simpl_capri_sls" or "shell_simpl_capri_sls_batch"
            or "shell_layered_sls" or "shell_layered_sls_batch"
            or "cracking" or "cracking_batch"
            or "crack_width" or "crack_width_batch"
            or "sp63_crack_width"
            or "sp63_deflection"
            or "total_curvature" or "total_curvature_batch"           => Sls,

        _ when kind.StartsWith("fire_", System.StringComparison.Ordinal) => Fire,

        _                                                             => Other
    };
}

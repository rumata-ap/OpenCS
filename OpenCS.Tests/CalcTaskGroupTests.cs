using OpenCS.Tasks;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Группировка расчётных задач по предельным состояниям в дереве задач
/// (<see cref="CalcTaskGroups"/>): задача должна попадать в свою группу —
/// НДС / 1-я ГПС / 2-я ГПС / Огнестойкость, — а не в «Прочие» по недосмотру.
/// </summary>
public class CalcTaskGroupTests
{
    [Theory]
    [InlineData("strain_state", CalcTaskGroups.Nds)]
    [InlineData("shell_strain_state_batch", CalcTaskGroups.Nds)]
    [InlineData("limit_moment", CalcTaskGroups.Uls)]
    [InlineData("shear_inclined", CalcTaskGroups.Uls)]
    [InlineData("shear_inclined_batch", CalcTaskGroups.Uls)]
    [InlineData("sp63_normal", CalcTaskGroups.Uls)]
    [InlineData("crack_width", CalcTaskGroups.Sls)]
    [InlineData("sp63_crack_width", CalcTaskGroups.Sls)]
    [InlineData("shell_layered_sls", CalcTaskGroups.Sls)]
    [InlineData("shell_layered_sls_batch", CalcTaskGroups.Sls)]
    [InlineData("total_curvature", CalcTaskGroups.Sls)]
    [InlineData("fire_r_check", CalcTaskGroups.Fire)]
    [InlineData("fire_thermal_curvature", CalcTaskGroups.Fire)]
    public void Classify_AssignsExpectedGroup(string kind, string expected)
        => Assert.Equal(expected, CalcTaskGroups.Classify(kind));

    [Fact]
    public void Classify_UnknownKind_FallsBackToOther()
        => Assert.Equal(CalcTaskGroups.Other, CalcTaskGroups.Classify("no_such_kind"));

    /// <summary>
    /// Виды задач, для которых группа «Прочие» — осознанное решение:
    /// в диалоге задачи у них тоже GroupKey = "other".
    /// </summary>
    static readonly string[] IntentionallyOther =
    [
        "moment_curvature_biaxial",
        "torsion_bem",
        "torsion_fem",
        "prestress_loss",
        "steel_constructive",
        "opensees_section_moment_curvature",
        "opensees_section_interaction_nm",
        "opensees_section_interaction_n_mx_my",
    ];

    /// <summary>
    /// Каждый зарегистрированный вид задачи должен быть отнесён к конкретной группе:
    /// незаклассифицированный вид молча уезжает в «Прочие» — именно так произошло
    /// с sp63_normal и sp63_crack_width.
    /// </summary>
    [Fact]
    public void EveryRegisteredKindIsEitherClassifiedOrIntentionallyOther()
    {
        var unexplained = TaskRunner.KindList
            .Where(kind => CalcTaskGroups.Classify(kind) == CalcTaskGroups.Other)
            .Where(kind => !IntentionallyOther.Contains(kind, StringComparer.Ordinal))
            .OrderBy(kind => kind, StringComparer.Ordinal)
            .ToList();

        Assert.True(unexplained.Count == 0,
            "Виды задач без группы предельного состояния (попадут в «Прочие»): "
            + string.Join(", ", unexplained));
    }

    [Fact]
    public void IntentionallyOtherKindsAreRegisteredAndClassifiedAsOther()
    {
        foreach (string kind in IntentionallyOther)
        {
            Assert.Contains(kind, TaskRunner.KindList);
            Assert.Equal(CalcTaskGroups.Other, CalcTaskGroups.Classify(kind));
        }
    }
}

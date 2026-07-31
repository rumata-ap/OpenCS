using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tcl;

/// <summary>Эмитит Tcl-команду uniaxialMaterial для beam-fiber материала (Concrete01/02/04,
/// Steel01/02 или ElasticMultiLinear-фолбэк по огибающим). Общий helper для
/// FemNonlinearTclGenerator (чисто стержневые схемы) и ShellTclGenerator (смешанные
/// стержнево-оболочечные схемы) — устраняет дублирование switch-выражения.</summary>
public static class NativeMaterialTclEmitter
{
    public static string ToTcl(OpenSeesMaterialDefinition mat, Func<double, string> f) => mat.Native switch
    {
        Concrete01Spec c1 =>
            $"uniaxialMaterial Concrete01 {mat.Tag} {f(c1.Fpc)} {f(c1.Epsc0)} {f(c1.Fpcu)} {f(c1.EpsU)}",
        Concrete02Spec c2 =>
            $"uniaxialMaterial Concrete02 {mat.Tag} {f(c2.Fpc)} {f(c2.Epsc0)} {f(c2.Fpcu)} {f(c2.EpsU)} {f(c2.Lambda)} {f(c2.Ft)} {f(c2.Ets)}",
        Concrete04Spec c4 when c4.Fct is { } fct && c4.Et is { } et && c4.Beta is { } beta =>
            $"uniaxialMaterial Concrete04 {mat.Tag} {f(c4.Fc)} {f(c4.Ec0)} {f(c4.Ecu)} {f(c4.Ec)} {f(fct)} {f(et)} {f(beta)}",
        Concrete04Spec c4 =>
            $"uniaxialMaterial Concrete04 {mat.Tag} {f(c4.Fc)} {f(c4.Ec0)} {f(c4.Ecu)} {f(c4.Ec)}",
        Steel01Spec s1 =>
            $"uniaxialMaterial Steel01 {mat.Tag} {f(s1.Fy)} {f(s1.E0)} {f(s1.B)}",
        Steel02Spec s2 =>
            $"uniaxialMaterial Steel02 {mat.Tag} {f(s2.Fy)} {f(s2.E0)} {f(s2.B)} {f(s2.R0)} {f(s2.CR1)} {f(s2.CR2)}",
        null => BuildElasticMultiLinearCommand(mat, f),
        _ => throw new InvalidOperationException($"Неизвестный тип NativeMaterialSpec: {mat.Native.GetType().Name}.")
    };

    // Точки: отрицательная огибающая целиком + положительная без первой (общей) точки — тот же
    // приём, что и в SectionMomentCurvatureTclGenerator.
    private static string BuildElasticMultiLinearCommand(OpenSeesMaterialDefinition mat, Func<double, string> f)
    {
        var points = mat.NegativeEnvelope.Concat(mat.PositiveEnvelope.Skip(1)).ToList();
        var strains = string.Join(' ', points.Select(p => f(p.Strain)));
        var stresses = string.Join(' ', points.Select(p => f(p.StressPa)));
        return $"uniaxialMaterial ElasticMultiLinear {mat.Tag} -strain {strains} -stress {stresses}";
    }
}

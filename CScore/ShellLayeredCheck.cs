using System;

namespace CScore;

/// <summary>
/// Нелинейная проверка прочности плитного/оболочечного сечения.
/// Использует слоистую/интегральную модель интегрирования (PlateSection.PlateModel)
/// через ShellStrainSolver и деформационный критерий СП 63 п. 8.1.30.
/// </summary>
public static class ShellLayeredCheck
{
    /// <summary>
    /// Проверка прочности по СП 63 п. 8.1.30: предельные деформации сжатия бетона
    /// (εb2/εb0 с учётом одно/двузначной эпюры) и растяжения арматуры
    /// (εs,ult = 0.025 — физ. текучесть, 0.015 — условная).
    /// </summary>
    /// <param name="section">Плитное сечение (использует PlateModel, RebarLayers, H, ConcreteDiagramType).</param>
    /// <param name="shell">Усилия Nx,Ny,Nxy,Mx,My,Mxy (кН/м, кН·м/м).</param>
    /// <param name="concreteMat">Материал бетона.</param>
    /// <param name="rebarMat">Материал арматуры.</param>
    /// <param name="calcType">Вид расчёта (для ULS — C или CL).</param>
    /// <param name="concreteDiagType">Тип диаграммы бетона (обычно section.ConcreteDiagramType); резерв — L3.</param>
    /// <param name="strainState">Найденное НДС (для эпюр).</param>
    /// <param name="resultForces">Результирующие усилия R(ε*).</param>
    /// <param name="secant">Секансные/упругие жёсткости и φ.</param>
    public static Result CheckUls(
        PlateSection section,
        ShellLoadItem shell,
        Material concreteMat,
        Material rebarMat,
        CalcType calcType,
        DiagrammType concreteDiagType,
        out ShellStrainState strainState,
        out ShellResult resultForces,
        out ShellSecantStiffness secant,
        bool? tensionOverride = null)
    {
        var diagCalcType = calcType;   // ULS: C или CL

        var cDiag = concreteMat.GetDiagramms(concreteDiagType)?[diagCalcType]
            ?? concreteMat.GetDiagramms(DiagrammType.L3)?[diagCalcType]
            ?? throw new InvalidOperationException("Диаграмма бетона не построена");

        // Для арматуры с условным пределом текучести двухлинейной диаграммы не существует —
        // тип приводится к допустимому (L3), см. DiagrammCompatibility.
        var rDiag = rebarMat.GetDiagramms(
                DiagrammCompatibility.Coerce(rebarMat.Type, DiagrammType.L2))?[diagCalcType]
            ?? throw new InvalidOperationException("Диаграмма арматуры не построена");

        double[] target = [shell.Nx, shell.Ny, shell.Nxy, shell.Mx, shell.My, shell.Mxy];
        var (res, solved) = SolveWithFallback(section, cDiag, rDiag, target, concreteMat, rebarMat, calcType,
            tensionOverride);
        string refinedNote = RefinedNote(section, solved);
        section = solved;

        strainState  = res.StrainState;
        resultForces = res.Forces;
        secant       = section.ComputeSecant(res.StrainState, cDiag, rDiag, tensionOverride: tensionOverride);

        if (!res.Converged)
            return new Result(Passed: false, Utilization: 2.0,
                Formula: "НДС", Description: $"Нет сходимости за {res.Iterations} ит., Δ={res.Residual:G2}",
                Converged: false, Iterations: res.Iterations, Residual: res.Residual);

        // ── Деформационные параметры бетона по СП 63 п. 8.1.30 ───────────────
        concreteMat.chars.TryGetValue(calcType, out var cCh);
        double epsB0 = cCh?.Ec0 > 0 ? cCh.Ec0 : 0.002;   // εb0
        double epsB2 = cCh?.Ec2 > 0 ? cCh.Ec2 : 0.0035;  // εb2

        var st = res.StrainState;
        double h = section.H;

        double eps2T = MinPrincipalStrain(st.EpsX( h / 2), st.EpsY( h / 2), st.GammaXY( h / 2));
        double eps2B = MinPrincipalStrain(st.EpsX(-h / 2), st.EpsY(-h / 2), st.GammaXY(-h / 2));

        double eps2, eps1;
        if (Math.Abs(eps2T) >= Math.Abs(eps2B)) { eps2 = eps2T; eps1 = eps2B; }
        else                                    { eps2 = eps2B; eps1 = eps2T; }

        double epsBUlt; string epsBDesc;
        if (eps2 >= 0)
        {
            epsBUlt  = epsB2;
            epsBDesc = "нет сжатия";
        }
        else if (eps1 >= 0)
        {
            epsBUlt  = epsB2;
            epsBDesc = $"двузн., εb,ult={epsB2:G3}";
        }
        else
        {
            double ratio = Math.Abs(eps2) > 1e-12 ? Math.Clamp(eps1 / eps2, 0.0, 1.0) : 1.0;
            epsBUlt  = epsB2 - (epsB2 - epsB0) * ratio;
            epsBDesc = $"однозн., ε₁/ε₂={ratio:F2}, εb,ult={epsBUlt:G3}";
        }

        double utilC = eps2 < 0 ? Math.Abs(eps2) / epsBUlt : 0.0;

        // ── Арматура: εs,ult по типу текучести ───────────────────────────────
        double epsSUlt = rebarMat.Type == MatType.ReSteelF ? 0.025 : 0.015;
        double utilS   = 0;
        foreach (var layer in section.RebarLayers)
        {
            double epsX = st.EpsX(layer.Zsx);
            double epsY = st.EpsY(layer.Zsy);
            utilS = Math.Max(utilS, Math.Max(epsX, epsY) / epsSUlt);
        }

        double util = Math.Max(utilC, utilS);
        bool passed = util <= 1.0;

        if (utilC >= utilS)
            return new Result(passed, util, "п.8.1.30 бетон",
                epsBDesc + $", ε={Math.Abs(eps2):G3}" + refinedNote,
                true, res.Iterations, res.Residual);
        else
            return new Result(passed, util, "п.8.1.30 арм.",
                $"εs={utilS * epsSUlt:G3}, εs,ult={epsSUlt:G3}" + refinedNote,
                true, res.Iterations, res.Residual);
    }

    /// <summary>Число слоёв, на которое измельчается сечение, когда на заданном разбиении НДС не находится.</summary>
    public const int RefinedLayers = 40;

    /// <summary>
    /// Поиск НДС с запасными путями: обычный Ньютон → каскад <see cref="ShellStrainSolver.SolveRobust"/>
    /// → то же на сечении, измельчённом до <see cref="RefinedLayers"/> слоёв.
    /// Измельчение нужно слоистой модели при тонкой сжатой зоне: бетон слоя работает по деформации
    /// его середины, и пока сжатая зона тоньше половины слоя (10 слоёв при h = 200 мм — 10 мм, а
    /// у слабоармированной плиты x ≈ 8 мм), сжатого бетона в модели нет вовсе — равновесия
    /// с моментом больше Rsc·A's·(h0 − a') не существует, и итерации расходятся.
    /// </summary>
    /// <returns>Результат и сечение, на котором он получен (исходное или измельчённый клон).</returns>
    public static (ShellStrainSolverResult Result, PlateSection Section) SolveWithFallback(
        PlateSection section, Diagramm cDiag, Diagramm rDiag, double[] target,
        Material concreteMat, Material rebarMat, CalcType calcType, bool? tensionOverride = null)
    {
        var solver = new ShellStrainSolver(section, cDiag, rDiag, tensionOverride: tensionOverride);
        var res = solver.Solve(target);
        if (res.Converged) return (res, section);

        var robust = solver.SolveRobust(target, concreteMat, rebarMat, calcType);
        if (robust.Converged) return (robust, section);

        bool layered = section.PlateModel is not ("char1d_principal" or "char1d_axial");
        if (!layered || section.NLayers >= RefinedLayers) return (res, section);

        var refined = section.CloneForCalc();
        refined.NLayers = RefinedLayers;
        var refinedSolver = new ShellStrainSolver(refined, cDiag, rDiag, tensionOverride: tensionOverride);
        var fine = refinedSolver.Solve(target);
        if (!fine.Converged) fine = refinedSolver.SolveRobust(target, concreteMat, rebarMat, calcType);
        return fine.Converged ? (fine, refined) : (res, section);
    }

    /// <summary>Пометка к описанию результата, полученного на измельчённом сечении.</summary>
    public static string RefinedNote(PlateSection given, PlateSection solved) =>
        ReferenceEquals(given, solved) ? "" : $"; слоёв по толщине {solved.NLayers} (при {given.NLayers} решения нет)";

    static double MinPrincipalStrain(double ex, double ey, double gxy)
    {
        double avg = (ex + ey) / 2.0;
        double r   = Math.Sqrt((ex - ey) * (ex - ey) / 4.0 + gxy * gxy / 4.0);
        return avg - r;
    }

    static double MaxPrincipalStrain(double ex, double ey, double gxy)
    {
        double avg = (ex + ey) / 2.0;
        double r   = Math.Sqrt((ex - ey) * (ex - ey) / 4.0 + gxy * gxy / 4.0);
        return avg + r;
    }

    /// <summary>Результат проверки прочности.</summary>
    /// <param name="Passed">Прочность обеспечена (util ≤ 1.0).</param>
    /// <param name="Utilization">Коэффициент использования (для МКЭ-таблицы; UI простых задач не показывает).</param>
    /// <param name="Formula">Определяющая формула: "п.8.1.30 бетон" / "п.8.1.30 арм." / "НДС".</param>
    /// <param name="Description">Текстовое пояснение критического волокна/слоя.</param>
    /// <param name="Converged">Сошёлся ли решатель.</param>
    /// <param name="Iterations">Число итераций финальной попытки.</param>
    /// <param name="Residual">Норма невязки ‖R(ε*) − S‖.</param>
    public sealed record Result(
        bool   Passed,
        double Utilization,
        string Formula,
        string Description,
        bool   Converged,
        int    Iterations,
        double Residual);
}

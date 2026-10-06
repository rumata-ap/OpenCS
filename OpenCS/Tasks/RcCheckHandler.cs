using System;
using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Sp63;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик нормативной проверки «rc_check» стержня схемы МКЭ: прочность по НДМ
/// (п. 8.1.24, 8.1.30 СП 63.13330) для одной строки усилий. Коэффициент использования —
/// наибольшее из отношений ε_b/ε_b,ult и ε_s/ε_s,ult. Несошедшийся НДС — проверка
/// не пройдена (сечение не воспринимает усилия), коэффициент не определён.
/// Работа бетона на растяжение — по настройке режима (<see cref="CalcSettings.ResolveConcreteTension"/>),
/// но только до образования трещин: если с растянутым бетоном НДС не найден (после трещин касательная
/// жёсткость скачком падает, и метод Ньютона срывается), строка перерешивается без растяжения —
/// трещины образовались, растянутый бетон выключен. Отказ — только если НДС не найден и так.
/// Без этого нормативные наборы (N, NL) давали ложные «НДС не найден» у сечений с запасом (01.10.2026).
/// <para>
/// Продольный изгиб (<see cref="BarCheckParams.Eta"/>): моменты строки усиливаются коэффициентом η
/// (п. 8.1.15) до проверки НДМ — как в задачах НДС основного дерева (<see cref="RodEtaWiring"/>).
/// l0 = μ·l, l — вручную или длина КЭ между раскреплениями по сетке схемы; ψ — из длительного набора
/// строки или из параметров. Потеря устойчивости (|N| ≥ Ncr) — строка не пройдена.
/// </para>
/// </summary>
public sealed class RcCheckHandler : ITaskHandler
{
    public string Kind => "rc_check";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx?.Database?.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram, ekbEtaMin: settings.EkbDescEtaMin);

            var p = BarCheckParams.Parse(task.ParamsJson);
            double? lengthM = p.EtaEnabled ? p.ResolveLengthM() : null;
            if (p.EtaEnabled && lengthM is not > 0)
                return Result(task, created, "not_applicable", JsonSerializer.Serialize(new
                {
                    passed = false,
                    reason = "Продольный изгиб (п. 8.1.15): длина элемента не определена — "
                           + "у КЭ нет узлов в сетке схемы или строка не привязана к КЭ; задайте длину l вручную",
                }));

            bool ten = settings.ResolveConcreteTension(task.CalcType);
            var (eta, target) = ApplyEta(section, item, task.CalcType, ten, settings, p, lengthM);
            if (eta is { Stable: false })
                return Result(task, created, "not_passed", UnstableJson(eta.Value));
            var r = StrengthNDMBatchHandler.Evaluate(section, target, task.CalcType, ten, settings);
            if (!r.Converged && ten)
            {
                // Растянутый бетон работает до образования трещин: после них — без растяжения.
                ten = false;
                (eta, target) = ApplyEta(section, item, task.CalcType, ten, settings, p, lengthM);
                if (eta is { Stable: false })
                    return Result(task, created, "not_passed", UnstableJson(eta.Value));
                r = StrengthNDMBatchHandler.Evaluate(section, target, task.CalcType, ten, settings);
            }

            string etaNote = eta is { } e ? $"; ηx = {e.Wiring.X.Eta:F3}, ηy = {e.Wiring.Y.Eta:F3}" : "";
            object? etaJson = eta is { } ej ? EtaJson(ej) : null;
            string dataJson;
            string status;
            if (!r.Converged)
            {
                status = "not_converged";
                dataJson = JsonSerializer.Serialize(new
                {
                    passed = false,
                    reason = $"НДС не найден за {r.Iterations} ит. (невязка {r.Residual:G3}): "
                           + "сечение не воспринимает заданные усилия" + etaNote,
                    iterations = r.Iterations,
                    residual = Math.Round(r.Residual, 6),
                    eta = etaJson,
                });
            }
            else
            {
                double utilization = Math.Max(r.ConcreteRatio, r.RebarRatio);
                status = r.StrengthOk ? "ok" : "not_passed";
                dataJson = JsonSerializer.Serialize(new
                {
                    utilization = Math.Round(utilization, 6),
                    passed = r.StrengthOk,
                    e0 = Math.Round(r.Strain.e0, 8),
                    ky = Math.Round(r.Strain.ky, 8),
                    kz = Math.Round(r.Strain.kz, 8),
                    eps_concrete_compression = Math.Round(r.EpsConcreteCompression, 8),
                    eps_concrete_ult = Math.Round(r.EpsConcreteUlt, 8),
                    eps_rebar_tension = Math.Round(r.EpsRebarTension, 8),
                    eps_rebar_ult = Math.Round(r.EpsRebarUlt, 8),
                    concrete_tension = ten,
                    iterations = r.Iterations,
                    eta = etaJson,
                    details = new[]
                    {
                        new
                        {
                            formula = "п. 8.1.30",
                            description = $"бетон: ε_b = {r.EpsConcreteCompression:F5} / ε_b,ult = {r.EpsConcreteUlt:F5}" + etaNote,
                            ratio = Math.Round(r.ConcreteRatio, 6),
                            passed = r.ConcreteOk
                        },
                        new
                        {
                            formula = "п. 8.1.30",
                            description = $"арматура: ε_s = {r.EpsRebarTension:F5} / ε_s,ult = {r.EpsRebarUlt:F5}" + etaNote,
                            ratio = Math.Round(r.RebarRatio, 6),
                            passed = r.RebarOk
                        }
                    }
                });
            }

            return Result(task, created, status, dataJson);
        }
        catch (Exception ex)
        {
            return Result(task, created, "error", JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }

    static CalcResult Result(CalcTask task, string created, string status, string dataJson) => new()
    {
        TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
        Created = created, Status = status, DataJson = dataJson
    };

    /// <summary>η строки: результат усиления, длина и ψ.</summary>
    readonly record struct EtaOutcome(RodEtaWiring.Result Wiring, bool Iterative, double LengthM,
                                      double PsiX, double PsiY, bool PsiFromLongSet, double Threshold, double N)
    {
        public bool Stable => Wiring.X.Stable && Wiring.Y.Stable;
    }

    /// <summary>Усиливает Mx/My строки по п. 8.1.15; без η — строка как есть.</summary>
    static (EtaOutcome? Eta, LoadItem Target) ApplyEta(CrossSection section, LoadItem item, CalcType calcType,
        bool ten, CalcSettings settings, BarCheckParams p, double? lengthM)
    {
        if (!p.EtaEnabled || p.Eta is not { } eta || lengthM is not double l) return (null, item);

        double threshold = eta.SlendernessThreshold ?? EccentricityAmplifier.SlendernessThreshold;
        double psiX = p.RowPsiX ?? eta.PsiX, psiY = p.RowPsiY ?? eta.PsiY;
        var solver = new StrainSolver(section, calcType, ten: ten,
            tol: settings.NewtonTolerance, maxIter: settings.NewtonMaxIter, h: settings.NewtonDeltaH);
        var wiring = RodEtaWiring.Apply(section, item.N, item.Mx, item.My,
            l * eta.MuX, l * eta.MuY, psiX, psiY, eta.Iterative,
            (mx, my) => solver.Solve(item.N, mx, my), threshold);

        var outcome = new EtaOutcome(wiring, eta.Iterative, l, psiX, psiY,
            p.RowPsiX != null || p.RowPsiY != null, threshold, item.N);
        var target = new LoadItem
        {
            Id = item.Id, Num = item.Num, Label = item.Label,
            N = item.N, Mx = wiring.MxEff, My = wiring.MyEff, Vx = item.Vx, Vy = item.Vy, T = item.T,
            SourceElementNum = item.SourceElementNum, SourceSectionNum = item.SourceSectionNum,
        };
        return (outcome, target);
    }

    /// <summary>Потеря устойчивости: |N| ≥ Ncr хотя бы в одной плоскости.</summary>
    static string UnstableJson(EtaOutcome eta)
    {
        var w = eta.Wiring;
        double ncr = Math.Min(w.X.Stable ? double.PositiveInfinity : w.X.Ncr,
                              w.Y.Stable ? double.PositiveInfinity : w.Y.Ncr);
        string plane = !w.X.Stable ? "Mx" : "My";
        double ratio = ncr > 0 ? Math.Abs(eta.N) / ncr : double.PositiveInfinity;
        return JsonSerializer.Serialize(new
        {
            utilization = StrainStateJsonHelper.FiniteRounded(ratio, 6),
            passed = false,
            eta = EtaJson(eta),
            details = new[]
            {
                new
                {
                    formula = "п. 8.1.15",
                    description = $"потеря устойчивости в плоскости {plane}: |N| = {Math.Abs(eta.N):F1} ≥ Ncr = {ncr:F1} кН",
                    ratio = StrainStateJsonHelper.FiniteRounded(ratio, 6),
                    passed = false
                }
            }
        });
    }

    static object EtaJson(EtaOutcome eta)
    {
        var (x, y) = (eta.Wiring.X, eta.Wiring.Y);
        return new
        {
            mode = eta.Iterative ? "iterative" : "formula",
            slendernessThreshold = eta.Threshold,
            lengthM = StrainStateJsonHelper.FiniteRounded(eta.LengthM, 4),
            psiX = StrainStateJsonHelper.FiniteRounded(eta.PsiX, 4),
            psiY = StrainStateJsonHelper.FiniteRounded(eta.PsiY, 4),
            psiFromLongSet = eta.PsiFromLongSet,
            l0x = StrainStateJsonHelper.FiniteRounded(x.L0, 4),
            ix = StrainStateJsonHelper.FiniteRounded(x.I, 4),
            slendernessX = x.I > 1e-9 ? StrainStateJsonHelper.FiniteRounded(x.L0 / x.I, 2) : null,
            etaX = StrainStateJsonHelper.FiniteRounded(x.Eta, 6),
            ncrX = StrainStateJsonHelper.FiniteRounded(x.Ncr, 4),
            stableX = x.Stable,
            l0y = StrainStateJsonHelper.FiniteRounded(y.L0, 4),
            iy = StrainStateJsonHelper.FiniteRounded(y.I, 4),
            slendernessY = y.I > 1e-9 ? StrainStateJsonHelper.FiniteRounded(y.L0 / y.I, 2) : null,
            etaY = StrainStateJsonHelper.FiniteRounded(y.Eta, 6),
            ncrY = StrainStateJsonHelper.FiniteRounded(y.Ncr, 4),
            stableY = y.Stable,
            mxEff = StrainStateJsonHelper.FiniteRounded(eta.Wiring.MxEff, 4),
            myEff = StrainStateJsonHelper.FiniteRounded(eta.Wiring.MyEff, 4),
        };
    }
}

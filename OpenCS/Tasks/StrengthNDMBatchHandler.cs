using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Обработчик задачи «Проверка прочности по НДМ (весь набор)».
/// Для каждой строки набора усилий находит плоскость деформаций методом Ньютона-Рафсона,
/// затем проверяет условия прочности по п.8.1.24 СП63.13330:
///   |ε_b,max| ≤ ε_b,ult   (бетон на сжатие)
///   |ε_s,max| ≤ ε_s,ult   (арматура на растяжение)
/// Предельные деформации определяются по п.8.1.30.
/// </summary>
public sealed class StrengthNDMBatchHandler : ITaskHandler
{
    public string Kind => "strength_ndm_batch";

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item,
                          CalcSettings settings, TaskRunContext? ctx = null)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            if (ctx?.Database is null)
                throw new InvalidOperationException(
                    "Для strength_ndm_batch требуется контекст с DatabaseService.");

            var forceSet = ctx.Database.ForceSets.FirstOrDefault(fs => fs.Id == task.ForceSetId)
                ?? throw new InvalidOperationException(
                    $"Набор усилий id={task.ForceSetId} не найден.");

            section.ResolveAndBuildDiagramms(settings.Sp63DescEtaMin,
                pool: ctx.Database.Diagrams,
                rebarDifferentialDiagram: settings.RebarDifferentialDiagram);
            bool ten = settings.ResolveConcreteTension(task.CalcType);

            var items = forceSet.Items;
            int total = items.Count;
            var rowResults = new object[total];
            var convergedArr = new bool[total];
            var passedArr = new bool[total];

            void Solve(int i)
            {
                var fi = items[i];
                var clone = settings.BatchParallel ? section.CloneForCalc() : section;
                var solver = new StrainSolver(clone, task.CalcType, ten: ten,
                    tol: settings.NewtonTolerance,
                    maxIter: settings.NewtonMaxIter,
                    h: settings.NewtonDeltaH);
                var k = solver.Solve(fi.N, fi.Mx, fi.My);
                convergedArr[i] = solver.Converged;

                if (!solver.Converged)
                {
                    passedArr[i] = false;
                    rowResults[i] = BuildRow(fi, k, solver,
                        epsConcreteCompression: 0, epsConcreteUlt: 0,
                        epsRebarTension: 0, epsRebarUlt: 0,
                        concreteOk: false, rebarOk: false, strengthOk: false);
                    return;
                }

                clone.SetEps(k, task.CalcType, ten);

                var (epsConcreteMin, epsConcreteMax) = ComputeExtremeConcreteStrains(clone, k);
                var (epsRebarMin, epsRebarMax) = ComputeExtremeRebarStrains(clone, k);

                double epsConcreteUlt = ResolveConcreteEpsUlt(clone, task.CalcType, epsConcreteMin, epsConcreteMax);
                double epsRebarUlt = ResolveRebarEpsUlt(clone, task.CalcType);

                bool concreteOk = Math.Abs(epsConcreteMin) <= Math.Abs(epsConcreteUlt) + 1e-10;
                bool rebarOk = epsRebarMax <= epsRebarUlt + 1e-10;
                bool strengthOk = concreteOk && rebarOk;

                passedArr[i] = strengthOk;
                rowResults[i] = BuildRow(fi, k, solver,
                    epsConcreteMin, epsConcreteUlt,
                    epsRebarMax, epsRebarUlt,
                    concreteOk, rebarOk, strengthOk);
            }

            if (settings.BatchParallel && total > 1)
                Parallel.For(0, total, Solve);
            else
                for (int i = 0; i < total; i++) Solve(i);

            int convergedCount = convergedArr.Count(c => c);
            int passedCount = passedArr.Count(p => p);
            bool allConverged = convergedCount == total;
            bool allPassed = passedCount == total;

            var data = new
            {
                all_converged = allConverged,
                converged_count = convergedCount,
                all_passed = allPassed,
                passed_count = passedCount,
                total,
                rows = rowResults
            };

            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = allPassed ? "ok" : (allConverged ? "not_passed" : "partial"),
                DataJson = JsonSerializer.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id,
                TaskKind = task.Kind,
                TaskTag = task.Tag,
                Created = created,
                Status = "error",
                DataJson = JsonSerializer.Serialize(new { error = ex.Message })
            };
        }
    }

    static object BuildRow(LoadItem fi, Kurvature k, StrainSolver solver,
        double epsConcreteCompression, double epsConcreteUlt,
        double epsRebarTension, double epsRebarUlt,
        bool concreteOk, bool rebarOk, bool strengthOk) => new
    {
        label = fi.Label,
        num = fi.Num,
        N = fi.N,
        Mx = fi.Mx,
        My = fi.My,
        e0 = Math.Round(k.e0, 8),
        ky = Math.Round(k.ky, 8),
        kz = Math.Round(k.kz, 8),
        eps_concrete_compression = Math.Round(epsConcreteCompression, 8),
        eps_concrete_ult = Math.Round(epsConcreteUlt, 8),
        eps_rebar_tension = Math.Round(epsRebarTension, 8),
        eps_rebar_ult = Math.Round(epsRebarUlt, 8),
        concrete_ok = concreteOk,
        rebar_ok = rebarOk,
        strength_ok = strengthOk,
        iterations = solver.Iterations,
        residual = Math.Round(solver.Residual, 6),
        status = solver.Converged ? "ok" : "not_converged"
    };

    /// <summary>
    /// Экстремальные деформации бетона (сжатие — min, растяжение — max)
    /// по вершинам контуров Hull.
    /// </summary>
    static (double min, double max) ComputeExtremeConcreteStrains(CrossSection section, Kurvature k)
    {
        double min = 0, max = 0;
        bool found = false;
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            if (area.Material?.Type != MatType.Concrete) continue;
            if (area.Hull == null) continue;
            var xs = area.Hull.X; var ys = area.Hull.Y;
            for (int i = 0; i < xs.Count; i++)
            {
                double eps = ka.e0 + ka.ky * ys[i] + ka.kz * xs[i];
                if (!found) { min = max = eps; found = true; }
                else { if (eps < min) min = eps; if (eps > max) max = eps; }
            }
        }
        return (min, max);
    }

    /// <summary>
    /// Экстремальные деформации арматуры (растяжение — max)
    /// по точечным волокнам.
    /// </summary>
    static (double min, double max) ComputeExtremeRebarStrains(CrossSection section, Kurvature k)
    {
        double min = 0, max = 0;
        bool found = false;
        foreach (var (area, ka) in section.EnumerateAreas(k))
        {
            foreach (var f in area.Fibers.Where(f => f.TypeFiber == FiberType.point))
            {
                double eps = ka.e0 + ka.ky * f.Y + ka.kz * f.X;
                if (!found) { min = max = eps; found = true; }
                else { if (eps < min) min = eps; if (eps > max) max = eps; }
            }
        }
        return (min, max);
    }

    /// <summary>
    /// Предельная деформация сжатия бетона ε_b,ult по п.8.1.30 СП63.13330.
    /// При двузначной эпюре (изгиб, внецентренное сжатие/растяжение с большими эксцентриситетами):
    ///   ε_b,ult = ε_b2 (предельная деформация диаграммы).
    /// При однозначной эпюре (всё сечение в сжатии):
    ///   ε_b,ult = ε_b2 - (ε_b2 - ε_b0) * ε_1/ε_2,
    /// где ε_1, ε_2 — деформации на противоположных гранях (|ε_2| ≥ |ε_1|).
    /// </summary>
    static double ResolveConcreteEpsUlt(CrossSection section, CalcType calc,
        double epsConcreteMin, double epsConcreteMax)
    {
        if (epsConcreteMax <= 0)
        {
            double eps1 = Math.Abs(epsConcreteMax);
            double eps2 = Math.Abs(epsConcreteMin);

            double eb2 = double.NaN, eb0 = double.NaN;
            foreach (var area in section.Areas)
            {
                if (area.Material?.Type != MatType.Concrete) continue;
                if (!area.Diagramms.TryGetValue(calc, out var dgr)) continue;
                if (dgr.Ic.X.Length == 0) continue;

                eb2 = dgr.Ic.X.Min();
                var ch = calc switch
                {
                    CalcType.C => area.Material.C,
                    CalcType.CL => area.Material.CL,
                    CalcType.N => area.Material.N,
                    CalcType.NL => area.Material.NL,
                    _ => area.Material.C
                };
                if (ch != null && double.IsFinite(ch.Ec0) && ch.Ec0 < 0)
                    eb0 = ch.Ec0;
                else
                    eb0 = eb2;
                break;
            }

            if (!double.IsFinite(eb2)) return -0.0035;

            if (eps2 > 1e-12 && double.IsFinite(eb0))
                return eb2 - (eb2 - eb0) * (eps1 / eps2);
            return eb2;
        }
        else
        {
            double eb2 = double.NaN;
            foreach (var area in section.Areas)
            {
                if (area.Material?.Type != MatType.Concrete) continue;
                if (!area.Diagramms.TryGetValue(calc, out var dgr)) continue;
                if (dgr.Ic.X.Length == 0) continue;

                eb2 = dgr.Ic.X.Min();
                break;
            }
            return double.IsFinite(eb2) ? eb2 : -0.0035;
        }
    }

    /// <summary>
    /// Предельная деформация растяжения арматуры ε_s,ult по п.8.1.30 СП63.13330:
    ///   0,025 — для арматуры с физическим пределом текучести (ReSteelF);
    ///   0,015 — для арматуры с условным пределом текучести (ReSteelU).
    /// </summary>
    static double ResolveRebarEpsUlt(CrossSection section, CalcType calc)
    {
        double maxEps = double.NaN;
        foreach (var area in section.Areas)
        {
            if (area.Material == null) continue;
            if (area.Material.Type != MatType.ReSteelF && area.Material.Type != MatType.ReSteelU)
                continue;

            var ch = calc switch
            {
                CalcType.C => area.Material.C,
                CalcType.CL => area.Material.CL,
                CalcType.N => area.Material.N,
                CalcType.NL => area.Material.NL,
                _ => area.Material.C
            };
            if (ch != null && double.IsFinite(ch.Et2) && ch.Et2 > 0)
            {
                maxEps = ch.Et2;
                break;
            }

            if (area.Diagramms.TryGetValue(calc, out var dgr) && dgr.It.X.Length > 0)
            {
                double dMax = dgr.It.X.Max();
                if (double.IsFinite(dMax) && dMax > 0)
                {
                    maxEps = dMax;
                    break;
                }
            }

            maxEps = area.Material.Type == MatType.ReSteelU ? 0.015 : 0.025;
            break;
        }
        return double.IsFinite(maxEps) ? maxEps : 0.025;
    }
}

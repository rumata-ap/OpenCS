using System.Text.Json;
using CScore;
using CSfea.Torsion;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>Базовый обработчик задачи кручения Сен-Венана.</summary>
public abstract class TorsionHandlerBase : ITaskHandler
{
    public abstract string Kind { get; }
    protected abstract TorsionMethod Method { get; }

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings)
        => Run(task, section, item, settings, null);

    public CalcResult Run(CalcTask task, CrossSection section, LoadItem item, CalcSettings settings, TaskRunContext? ctx)
    {
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        try
        {
            var p = TorsionParams.Parse(task.ParamsJson);
            var femOrder = string.Equals(p.FemOrder, "quadratic", StringComparison.OrdinalIgnoreCase)
                ? FemElementOrder.Quadratic
                : FemElementOrder.Linear;
            var area = section.Areas[0];
            var boundary = area.FromMaterialArea();

            var baseMat = TorsionMaterialHelper.ResolveBaseMaterial(section);
            double gMpa = TorsionMaterialHelper.ShearModulusMpa(baseMat);
            double mkKNm = ResolveMk(p, item);
            double vxKN = ResolveForce(item.Vx, p.VxKN);
            double vyKN = ResolveForce(item.Vy, p.VyKN);
            double nKN = ResolveForce(item.N, p.NkN);
            double mxKNm = ResolveForce(item.Mx, p.MxKNm);
            double myKNm = ResolveForce(item.My, p.MyKNm);
            double nu = TorsionMaterialHelper.PoissonRatio(baseMat?.Type ?? MatType.Concrete);

            TorsionProps props;
            TorsionAutoConvergeResult? autoConverge = null;
            double elemSizeM;
            var ct = ctx?.CancellationToken ?? default;
            if (p.AutoConverge)
            {
                double? h0 = p.AutoH0 > 0 ? p.AutoH0 : null;
                int nRuns = p.AutoRuns >= 2 ? p.AutoRuns : 3;
                Action<int, int, double>? onStep = null;
                if (ctx?.Progress is { } prog)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    onStep = (done, total, h) => prog.Report(new CalcTaskProgress
                    {
                        Fraction = (double)done / total,
                        Message = string.Format(Loc.S("CalcTaskTorsionRunProgress"),
                            done, total, h.ToString("G4", inv))
                    });
                }
                autoConverge = TorsionRichardson.SolveAutoConverge(
                    boundary, Method, p.Triangulation, femOrder, h0, nRuns, ct, onStep, nu: nu);
                props = autoConverge.ToTorsionProps();
                elemSizeM = autoConverge.Steps[^1].ElementSize;
            }
            else
            {
                ct.ThrowIfCancellationRequested();
                elemSizeM = p.ElementSize > 0 ? p.ElementSize : 0.05;
                props = TorsionSolver.Solve(boundary, Method, elemSizeM, p.Triangulation, femOrder, ct, nu);
            }

            double tauMax = double.NaN, twistRate = double.NaN;
            if (gMpa > 0 && props.It > 0 && mkKNm > 0)
            {
                double gPa = gMpa * 1e6;
                double mk = mkKNm * 1e3;
                twistRate = mk / (gPa * props.It);
                tauMax = gPa * twistRate * props.TauUnitMax;
            }

            // Касательные напряжения от Vx/Vy (Тимошенко) — только МКЭ. E не участвует: для
            // однородного сечения силовая упругая задача даёт τ, не зависящее от модуля
            // упругости (как и σ от N/M, τ от T) — см. док TorsionShearStressPostprocessor.
            bool hasShearForces = Math.Abs(vxKN) > 1e-12 || Math.Abs(vyKN) > 1e-12;
            double[]? tauShearX = null, tauShearY = null, tauShearMagField = null;
            double tauShearMax = double.NaN;
            if (hasShearForces && double.IsFinite(props.ShearDeltaS) &&
                props.ShearVxUnitFieldX != null && props.ShearVxUnitFieldY != null &&
                props.ShearVyUnitFieldX != null && props.ShearVyUnitFieldY != null)
            {
                double vxN = vxKN * 1e3, vyN = vyKN * 1e3;
                (tauShearX, tauShearY) = TorsionShearStressPostprocessor.Combine(
                    props.ShearVxUnitFieldX, props.ShearVxUnitFieldY,
                    props.ShearVyUnitFieldX, props.ShearVyUnitFieldY,
                    props.ShearDeltaS, vxN, vyN);
                tauShearMagField = new double[tauShearX.Length];
                tauShearMax = 0.0;
                for (int i = 0; i < tauShearX.Length; i++)
                {
                    double mag = Math.Sqrt(tauShearX[i] * tauShearX[i] + tauShearY[i] * tauShearY[i]);
                    tauShearMagField[i] = mag;
                    if (mag > tauShearMax) tauShearMax = mag;
                }
            }

            // Комбинированные напряжения: σzz(N,Mx,My) — простая механика материалов (не зависит
            // от E), + τ(T,Vx,Vy) векторно (компоненты, не только магнитуда) → σvm, σ11 (главное
            // растягивающее). Только МКЭ (нужны узловые координаты и компоненты τ от T).
            bool hasCombinedStress = (Math.Abs(nKN) > 1e-12 || Math.Abs(mxKNm) > 1e-12 || Math.Abs(myKNm) > 1e-12
                    || Math.Abs(mkKNm) > 1e-12 || hasShearForces)
                && props.NodeX != null && props.NodeY != null;
            double[]? sigmaZzField = null, sigmaVmField = null, sigma11Field = null;
            double sigmaZzMax = double.NaN, sigmaZzMin = double.NaN, sigmaVmMax = double.NaN, sigma11Max = double.NaN;
            if (hasCombinedStress)
            {
                var (xc, yc, geoArea, ixx, iyy, ixy) = TorsionGeoMoments.Compute(boundary);
                double nN = nKN * 1e3, mxNm = mxKNm * 1e3, myNm = myKNm * 1e3;
                bool hasTorsionTau = double.IsFinite(twistRate) && props.TauUnitFieldX != null && props.TauUnitFieldY != null;
                double gPaForTau = gMpa * 1e6;

                int nNodes = props.NodeX!.Length;
                sigmaZzField = new double[nNodes];
                sigmaVmField = new double[nNodes];
                sigma11Field = new double[nNodes];
                for (int i = 0; i < nNodes; i++)
                {
                    double sigmaZz = CombinedStress.SigmaZz(props.NodeX[i], props.NodeY![i], xc, yc,
                        geoArea, ixx, iyy, ixy, nN, mxNm, myNm);

                    double tauX = 0.0, tauY = 0.0;
                    if (hasTorsionTau)
                    {
                        tauX += gPaForTau * twistRate * props.TauUnitFieldX![i];
                        tauY += gPaForTau * twistRate * props.TauUnitFieldY![i];
                    }
                    if (tauShearX != null && tauShearY != null)
                    {
                        tauX += tauShearX[i];
                        tauY += tauShearY[i];
                    }

                    var (sigVm, sig11, _) = CombinedStress.Combine(sigmaZz, tauX, tauY);

                    sigmaZzField[i] = sigmaZz;
                    sigmaVmField[i] = sigVm;
                    sigma11Field[i] = sig11;
                    if (double.IsNaN(sigmaZzMax) || sigmaZz > sigmaZzMax) sigmaZzMax = sigmaZz;
                    if (double.IsNaN(sigmaZzMin) || sigmaZz < sigmaZzMin) sigmaZzMin = sigmaZz;
                    if (double.IsNaN(sigmaVmMax) || sigVm > sigmaVmMax) sigmaVmMax = sigVm;
                    if (double.IsNaN(sigma11Max) || sig11 > sigma11Max) sigma11Max = sig11;
                }
            }

            var holesX = boundary.Holes?.Select(h => h.X.Select(v => v * 1000.0).ToArray()).ToList();
            var holesY = boundary.Holes?.Select(h => h.Y.Select(v => v * 1000.0).ToArray()).ToList();

            var data = new
            {
                method = Method.ToString().ToLowerInvariant(),
                fem_order = p.FemOrder,
                It_m4 = props.It,
                It_mm4 = props.It * 1e12,
                shear_center_x_m = TorsionJsonHelper.Finite(props.ShearCenterX),
                shear_center_y_m = TorsionJsonHelper.Finite(props.ShearCenterY),
                shear_center_trefftz_x_m = TorsionJsonHelper.Finite(props.ShearCenterTrefftzX),
                shear_center_trefftz_y_m = TorsionJsonHelper.Finite(props.ShearCenterTrefftzY),
                tau_unit_max = props.TauUnitMax,
                tau_unit_max_mm2 = props.TauUnitMax * 1e6,
                n_elements = props.NElements,
                singular = props.Singular,
                element_size_m = elemSizeM,
                twist_rate = TorsionJsonHelper.Finite(twistRate),
                tau_max_Pa = TorsionJsonHelper.Finite(tauMax),
                g_mpa = gMpa,
                e_mpa = baseMat?.E ?? 0,
                mk_knm = mkKNm,
                mk_from_force_set = Math.Abs(item.T) > 1e-12,
                vx_kn = vxKN,
                vy_kn = vyKN,
                shear_from_force_set = Math.Abs(item.Vx) > 1e-12 || Math.Abs(item.Vy) > 1e-12,
                warping_constant_m6 = TorsionJsonHelper.Finite(props.WarpingConstant),
                tau_shear_max_Pa = TorsionJsonHelper.Finite(tauShearMax),
                n_kn = nKN,
                mx_knm = mxKNm,
                my_knm = myKNm,
                sigma_zz_max_Pa = TorsionJsonHelper.Finite(sigmaZzMax),
                sigma_zz_min_Pa = TorsionJsonHelper.Finite(sigmaZzMin),
                sigma_vm_max_Pa = TorsionJsonHelper.Finite(sigmaVmMax),
                sigma_11_max_Pa = TorsionJsonHelper.Finite(sigma11Max),
                node_x = props.NodeX,
                node_y = props.NodeY,
                tau_unit = TorsionJsonHelper.FiniteArray(props.TauUnitField),
                tau_shear = TorsionJsonHelper.FiniteArray(tauShearMagField),
                sigma_zz = TorsionJsonHelper.FiniteArray(sigmaZzField),
                sigma_vm = TorsionJsonHelper.FiniteArray(sigmaVmField),
                sigma_11 = TorsionJsonHelper.FiniteArray(sigma11Field),
                potential = TorsionJsonHelper.FiniteArray(props.PotentialField),
                triangles = props.Triangles,
                boundary_x = props.BoundaryX,
                boundary_y = props.BoundaryY,
                boundary_j1 = props.BoundaryJ1,
                outer_x_mm = boundary.OuterX.Select(v => v * 1000.0).ToArray(),
                outer_y_mm = boundary.OuterY.Select(v => v * 1000.0).ToArray(),
                holes_x_mm = holesX,
                holes_y_mm = holesY,
                auto_converge = p.AutoConverge,
                convergence_h_mm = autoConverge?.Steps.Select(s => s.ElementSize * 1000.0).ToArray(),
                convergence_it_mm4 = autoConverge?.Steps.Select(s => s.Props.It * 1e12).ToArray(),
                it_order = autoConverge?.ItOrder,
                it_extrapolated = autoConverge?.ItExtrapolated,
                shear_center_order_x = autoConverge?.ShearCenterXOrder,
                shear_center_order_y = autoConverge?.ShearCenterYOrder,
                shear_center_extrapolated = autoConverge?.ShearCenterExtrapolated
            };
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = props.Singular ? "not_converged" : "ok",
                DataJson = TorsionJsonHelper.Serialize(data)
            };
        }
        catch (Exception ex)
        {
            return new CalcResult
            {
                TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
                Created = created, Status = "error",
                DataJson = TorsionJsonHelper.Serialize(new { error = ex.Message })
            };
        }
    }

    /// <summary>Mk: T из строки набора усилий, иначе ручное значение из ParamsJson.</summary>
    static double ResolveMk(TorsionParams p, LoadItem item)
    {
        if (Math.Abs(item.T) > 1e-12) return Math.Abs(item.T);
        return p.MkKNm > 0 ? p.MkKNm : 0;
    }

    /// <summary>Значение из строки набора усилий, иначе ручное значение из ParamsJson (со знаком).</summary>
    static double ResolveForce(double itemV, double paramV) => Math.Abs(itemV) > 1e-12 ? itemV : paramV;
}

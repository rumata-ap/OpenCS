using System;
using System.Linq;

namespace CScore.PrestressLoss
{
    public static class PrestressLossCalc
    {
        // Таблица 6.12 СП 63 — φ_b,cr [ряд: влажность 0=>75%, 1=>40-75%, 2=><40%; столбец: класс B20..B60]
        static readonly double[,] PhiBCrTable =
        {
            // B20   B25   B30   B35   B40   B45   B50   B55   B60
            { 2.1,  1.9,  1.7,  1.6,  1.5,  1.4,  1.3,  1.2,  1.1 },
            { 2.7,  2.4,  2.2,  2.0,  1.9,  1.8,  1.6,  1.5,  1.4 },
            { 3.1,  2.8,  2.5,  2.3,  2.2,  2.0,  1.9,  1.8,  1.7 },
        };

        static readonly double[] PhiBCrClasses = { 20, 25, 30, 35, 40, 45, 50, 55, 60 };

        static double GetPhiBCr(HumidityClass h, double concrClass)
        {
            int row = h switch { HumidityClass.Above75 => 0, HumidityClass.Below40 => 2, _ => 1 };
            int col = 0;
            for (int i = 0; i < PhiBCrClasses.Length; i++)
                if (concrClass >= PhiBCrClasses[i]) col = i;
            return PhiBCrTable[row, col];
        }

        static double GetConcreteClass(CrossSection section)
        {
            var area = section.Areas.FirstOrDefault(a => a.Category == AreaCategory.Region);
            if (area?.Material?.chars.TryGetValue(CalcType.N, out var ch) == true)
                return ch.Class;
            return 30;
        }

        static double GetGroupCentroidY(MaterialArea area)
        {
            var pts  = area.Fibers.Where(f => f.TypeFiber == FiberType.point).ToList();
            if (pts.Count == 0) return 0;
            double sumA = pts.Sum(f => f.Area);
            return sumA > 0 ? pts.Sum(f => f.Area * f.Y) / sumA : 0;
        }

        static double GetGroupAreaM2(MaterialArea area)
            => area.Fibers.Where(f => f.TypeFiber == FiberType.point).Sum(f => f.Area);

        public static PrestressLossResult Compute(PrestressLossParams p, CrossSection? section)
        {
            var result = new PrestressLossResult();

            // --- Валидация ---
            foreach (var gp in p.Groups)
            {
                var area = section?.Areas.FirstOrDefault(a => a.Id == gp.AreaId);
                if (area == null && section != null)
                {
                    result.Errors.Add($"Группа AreaId={gp.AreaId}: область не найдена в сечении");
                    continue;
                }
                if (gp.RelaxFormula == RelaxFormula.ColdDrawnOrStrand)
                {
                    var mat = area?.Material;
                    if (mat == null || !mat.chars.TryGetValue(CalcType.N, out var chN) || chN.Ft <= 0)
                        result.Errors.Add(
                            $"Группа «{area?.Tag ?? gp.AreaId.ToString()}»: " +
                            "нет характеристик CalcType.N (нужны для R_sn по п.9.1.3)");
                }
            }
            if (p.Groups.Any(gp => gp.SigmaBpAuto) && section == null)
                result.Errors.Add("Автоматический σ_bpj требует передачи сечения (section == null)");
            if (result.Errors.Count > 0)
                return result;

            // --- Класс бетона и φ_b,cr ---
            double concrClass = p.ConcreteClassAuto
                ? GetConcreteClass(section!)
                : p.ConcreteClassOverride;
            if (concrClass < 20 || concrClass > 60)
            {
                result.Warnings.Add(
                    $"Класс бетона B{concrClass} вне диапазона таблицы 6.12 (B20–B60); принято граничное значение");
                concrClass = Math.Clamp(concrClass, 20, 60);
            }
            double phiBCr = GetPhiBCr(p.Humidity, concrClass);

            // E_b — модуль упругости бетона из первой бетонной области [кПа]
            double E_b = section?.Areas
                .FirstOrDefault(a => a.Category == AreaCategory.Region)
                ?.Material?.E ?? 1;

            // === Первые потери ===
            var groupResults = new System.Collections.Generic.List<PrestressGroupResult>();
            foreach (var gp in p.Groups)
            {
                var area  = section!.Areas.First(a => a.Id == gp.AreaId);
                var mat   = area.Material!;
                double Es_MPa = mat.E / 1000.0;   // кПа → МПа
                double Rsn    = mat.chars.TryGetValue(CalcType.N, out var chN)
                    ? chN.Ft / 1000.0 : 0;

                var gr = new PrestressGroupResult
                {
                    AreaId  = gp.AreaId,
                    AreaTag = area.Tag,
                    SigSp0  = gp.SigSp0,
                    AreaMm2 = GetGroupAreaM2(area) * 1e6   // м² → мм²
                };

                // Δσ_sp1 — релаксация (п.9.1.3)
                gr.DSp1 = gp.RelaxFormula switch
                {
                    RelaxFormula.HotRolled when gp.SubMethod == TensionSubMethod.Mechanical =>
                        Math.Max(0, 0.1 * gp.SigSp0 - 20),
                    RelaxFormula.HotRolled =>   // ElectroThermal
                        0.03 * gp.SigSp0,
                    RelaxFormula.ColdDrawnOrStrand =>
                        Math.Max(0, (0.22 * gp.SigSp0 / Rsn - 0.1) * gp.SigSp0),
                    RelaxFormula.StabilizedStrand =>
                        1.5 * (gp.RelaxR / 100.0) * gp.SigSp0,
                    _ => 0
                };

                // Δσ_sp2 — температурный перепад (только на упоры, п.9.1.4)
                if (p.Method == TensionMethod.OnSupports)
                {
                    double dt  = gp.UseDefaultDeltaT ? 65.0 : gp.DeltaT;
                    gr.DSp2 = 1.25 * dt;
                }

                // Δσ_sp3 — деформация формы (только на упоры + механический, п.9.1.5)
                if (p.Method == TensionMethod.OnSupports && gp.SubMethod == TensionSubMethod.Mechanical)
                {
                    if (gp.UseDefaultFormDeform)
                        gr.DSp3 = 30.0;
                    else
                    {
                        double eps = gp.NForms > 1
                            ? (gp.NForms - 1.0) / (2.0 * gp.NForms)
                              * gp.DeltaLForm / (gp.LForm * 1000.0)
                            : 0;
                        gr.DSp3 = eps * Es_MPa;
                    }
                }

                // Δσ_sp4 — деформация анкеров (только на упоры + механический, п.9.1.6)
                if (p.Method == TensionMethod.OnSupports && gp.SubMethod == TensionSubMethod.Mechanical)
                {
                    double dl  = gp.UseDefaultAnchorDeform ? 2.0 : gp.DeltaLAnchor;
                    double eps = dl / (gp.LAnchor * 1000.0);   // мм / (м*1000) = безразм.
                    gr.DSp4 = eps * Es_MPa;
                }

                // Δσ_sp7 — трение (только на бетон, п.9.1.2)
                if (p.Method == TensionMethod.OnConcrete)
                {
                    double arg = gp.KFriction * (gp.Omega1 * gp.XLength + gp.Theta);
                    gr.DSp7 = gp.SigSp0 * (1.0 - 1.0 / Math.Exp(arg));
                }

                gr.TotalFirst = gr.DSp1 + gr.DSp2 + gr.DSp3 + gr.DSp4 + gr.DSp7;
                gr.SigSp1     = Math.Max(0, gp.SigSp0 - gr.TotalFirst);

                groupResults.Add(gr);
            }

            // P̄_(1) = суммарная преднапрягающая сила после первых потерь [кН]
            double pFirst = 0;
            for (int i = 0; i < p.Groups.Count; i++)
            {
                double A_spj = GetGroupAreaM2(section!.Areas.First(a => a.Id == p.Groups[i].AreaId));
                pFirst += groupResults[i].SigSp1 * 1000.0 * A_spj;  // МПа*1000=кПа * м² = кН
            }
            result.PrecompForceFirst = pFirst;

            // === Приведённые характеристики сечения (для авто σ_bpj) ===
            double A_red = 0, I_red = 0, y_c = 0;
            {
                double sumA = 0, sumAY = 0;
                foreach (var area in section!.Areas)
                {
                    double alpha = area.Category == AreaCategory.Region
                        ? 1.0 : (area.Material?.E ?? E_b) / E_b;
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.none))
                    {
                        sumA  += alpha * f.Area;
                        sumAY += alpha * f.Area * f.Y;
                    }
                }
                if (sumA > 0) { y_c = sumAY / sumA; A_red = sumA; }
                foreach (var area in section.Areas)
                {
                    double alpha = area.Category == AreaCategory.Region
                        ? 1.0 : (area.Material?.E ?? E_b) / E_b;
                    foreach (var f in area.Fibers.Where(f => f.TypeFiber != FiberType.none))
                        I_red += alpha * f.Area * Math.Pow(f.Y - y_c, 2);
                }
            }

            // Суммарный момент от P̄_(1) относительно центра тяжести приведённого сечения [кН·м]
            double M_total = 0;
            for (int i = 0; i < p.Groups.Count; i++)
            {
                var area  = section!.Areas.First(a => a.Id == p.Groups[i].AreaId);
                double A  = GetGroupAreaM2(area);
                double Pj = groupResults[i].SigSp1 * 1000.0 * A;
                M_total  += Pj * (GetGroupCentroidY(area) - y_c);
            }

            // Площадь бетонного сечения для μ_spj [м²]
            double A_concrete = section!.Areas
                .Where(a => a.Category == AreaCategory.Region)
                .SelectMany(a => a.Fibers.Where(f => f.TypeFiber != FiberType.none))
                .Sum(f => f.Area);

            // === Вторые потери ===
            for (int i = 0; i < p.Groups.Count; i++)
            {
                var gp   = p.Groups[i];
                var gr   = groupResults[i];
                var area = section!.Areas.First(a => a.Id == gp.AreaId);
                var mat  = area.Material!;
                double Es_MPa = mat.E / 1000.0;

                // σ_bpj [МПа] — напряжение в бетоне на уровне арматуры после первых потерь
                double sigmaBpj;
                if (gp.SigmaBpAuto && A_red > 0)
                {
                    double dy = GetGroupCentroidY(area) - y_c;
                    // σ [кПа] = P/A_red + M*dy/I_red → /1000 = [МПа]
                    sigmaBpj = (pFirst / A_red + M_total * dy / I_red) / 1000.0;
                }
                else
                    sigmaBpj = gp.SigmaBpManual;

                gr.SigmaBpj = sigmaBpj;

                if (sigmaBpj < 0)
                {
                    // п.9.1.9: при σ_bpj < 0 вторые потери равны нулю
                    gr.DSp5 = 0;
                    gr.DSp6 = 0;
                }
                else
                {
                    // Δσ_sp5 — усадка (п.9.1.8)
                    double cc     = p.ConcreteClassAuto ? GetConcreteClass(section!) : p.ConcreteClassOverride;
                    double eps_sh = cc <= 35 ? 0.0002 : cc <= 40 ? 0.00025 : 0.0003;
                    double dSp5   = eps_sh * Es_MPa;
                    if (p.Method == TensionMethod.OnConcrete) dSp5 *= 0.75;
                    if (p.HeatTreated) dSp5 *= 0.85;
                    gr.DSp5 = dSp5;

                    // Δσ_sp6 — ползучесть (п.9.1.9, формула 9.9)
                    double alpha  = mat.E / E_b;   // α = E_s / E_b
                    double A_spj  = GetGroupAreaM2(area);
                    double mu_spj = A_concrete > 0 ? A_spj / A_concrete : 0;
                    double y_spj  = GetGroupCentroidY(area) - y_c;
                    double denom  = 1.0 + alpha * mu_spj
                                    * (1.0 + y_spj * y_spj * A_red / Math.Max(I_red, 1e-30))
                                    * (1.0 + 0.8 * phiBCr);
                    double dSp6   = denom > 0
                        ? 0.8 * alpha * phiBCr * sigmaBpj / denom
                        : 0;
                    if (p.HeatTreated) dSp6 *= 0.85;
                    gr.DSp6 = Math.Max(0, dSp6);
                }

                gr.TotalSecond = gr.DSp5 + gr.DSp6;
                gr.TotalAll    = gr.TotalFirst + gr.TotalSecond;
                gr.SigSp2      = Math.Max(0, gp.SigSp0 - gr.TotalAll);

                if (gr.TotalAll < 100)
                {
                    gr.MinLossWarning = true;
                    result.Warnings.Add(
                        $"Группа «{area.Tag}»: суммарные потери {gr.TotalAll:F1} МПа < 100 МПа (п.9.1.10)");
                }
            }

            // P̄_(2) = преднапрягающая сила после всех потерь [кН]
            double pTotal = 0;
            for (int i = 0; i < p.Groups.Count; i++)
            {
                double A_spj = GetGroupAreaM2(section!.Areas.First(a => a.Id == p.Groups[i].AreaId));
                pTotal += groupResults[i].SigSp2 * 1000.0 * A_spj;
            }
            result.PrecompForceTotal = pTotal;
            result.Groups = groupResults;
            return result;
        }
    }
}

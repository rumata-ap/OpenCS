using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
    /// <summary>
    /// Способ получения σs,crc в формуле ψs = 1 − 0,8·σs,crc/σs — ф. (8.137) п. 8.2.18 СП 63 —
    /// для упрощённых (формульных) проверок раскрытия трещин, плитных и стержневых.
    /// ВАЖНО: отдельной формулы для самой σs,crc в СП 63 нет. П. 8.2.18 определяет её словами:
    /// «напряжение в продольной растянутой арматуре в сечении с трещиной сразу после образования
    /// нормальных трещин, определяемое по 8.2.16, принимая в соответствующих формулах значения
    /// M = M_crc». Номера (8.137) и (8.138) относятся к ψs, а не к σs,crc.
    /// </summary>
    public enum SigmaSCrcMethod
    {
        /// <summary>
        /// Общее определение п. 8.2.18: σs,crc — та же σs по п. 8.2.16 при M = M_crc, с сохранением
        /// продольной силы. Считается той же цепочкой, что и σs (включая поправку высоты сжатой
        /// зоны по ф. (8.154) и вырождение при x_m ≤ 0), поэтому ψs остаётся согласованной.
        /// При N = 0 отношение σs,crc/σs вырождается в M_crc/M, то есть совпадает с ф. (8.138).
        /// Значение по умолчанию.
        /// </summary>
        ReleasedConcrete8137,

        /// <summary>
        /// Ф. (8.138): ψs = 1 − 0,8·M_crc/M — упрощение, допускаемое п. 8.2.18 для ИЗГИБАЕМЫХ
        /// элементов. Продольную силу не учитывает, поэтому при заметной N расходится с общим
        /// определением. Точность целиком определяется точностью формульного
        /// M_crc = Rbt,ser·γ·Wred. Отчётная σs,crc выводится обратным пересчётом из принятого ψs.
        /// </summary>
        CrackingMoment8138
    }

    /// <summary>
    /// Источник коэффициента пластичности γ в упругопластическом моменте сопротивления
    /// Wpl = γ·Wred (момент образования трещин, п. 8.2.11).
    /// </summary>
    public enum WplGammaMethod
    {
        /// <summary>СП 63.13330, прямоугольное сечение: γ = 1,3. Значение по умолчанию.</summary>
        Sp63,

        /// <summary>
        /// СНиП 2.03.01-84*: γ = 1,75 (формула Гвоздева-Дмитриева). Отвечает предпосылкам
        /// прямоугольной эпюры растянутой зоны при Ebt = Eb.
        /// </summary>
        Snip2030184,

        /// <summary>
        /// Радайкин О.В., 2018: γ = 1,6 + 1/(100·√μs), μs = As/(b·h) — эмпирическая
        /// зависимость от степени армирования, калиброванная по опытам Пирадова, Ватагина и
        /// Тошина. Область применения, заявленная автором, — бетоны В15…В35.
        /// </summary>
        Radaykin2018
    }

    public class ShellSimplStripResult
    {
        public string Name { get; set; } = "";
        public double M_des { get; set; }
        public double N_des { get; set; }
        public double H0 { get; set; }
        public double A_prime { get; set; }
        public double As_t { get; set; }
        public double As_c { get; set; }
        public double Ds { get; set; }
        public double Xm { get; set; }
        public double Zs { get; set; }
        public double Sigma_s_MPa { get; set; }
        /// <summary>σs,crc — напряжение в арматуре сразу после образования трещины, МПа (п. 8.2.18).</summary>
        public double Sigma_s_crc_MPa { get; set; }
        /// <summary>Коэффициент пластичности γ, принятый в Wpl = γ·Wred.</summary>
        public double Gamma { get; set; }
        public double Mcrc { get; set; }
        public bool Cracked { get; set; }
        public double Psi_s { get; set; }
        public double Ls_m { get; set; }
        public double Acrc_mm { get; set; }
        public double B_kNm2 { get; set; }
        public double Xi { get; set; }
        public double Xi_R { get; set; }
        public double M_ult { get; set; }
        public double Demand { get; set; }
        public double Eta { get; set; }
        public string Case { get; set; } = "";
        public bool NoRebar { get; set; }
    }

    public class ShellSimplDirectionResult
    {
        public double Alpha_deg { get; set; }
        public double M_n { get; set; }
        public double N_n { get; set; }
        public bool Top { get; set; }
        public ShellSimplStripResult Strip { get; set; } = null!;
    }

    public static class ShellSimplSolver
    {
        public record SolveParams(
            double Nx, double Ny, double Nxy,
            double Mx, double My, double Mxy,
            string Kind,
            double StepDeg = 10.0,
            double AcrcLimMm = 0.3,
            double Phi1 = 1.0,
            double Phi2 = 0.5,
            SigmaSCrcMethod SigmaSCrc = SigmaSCrcMethod.ReleasedConcrete8137,
            WplGammaMethod WplGamma = WplGammaMethod.Sp63
        );

        public sealed record SolveResult(
            string Method,
            string CalcType,
            SolveParams Forces,
            List<ShellSimplStripResult>? WaStrips,
            List<ShellSimplDirectionResult>? CapriDirs,
            ShellSimplDirectionResult? CriticalTop,
            ShellSimplDirectionResult? CriticalBot,
            double? EtaMax
        );

        public static SolveResult Solve(
            SolveParams p,
            PlateSection section,
            Material concreteMat,
            Material rebarMat,
            CalcType calcType)
        {
            bool isSls = p.Kind.EndsWith("sls");
            bool isWa = p.Kind.StartsWith("shell_simpl_wa_");
            bool isCapri = p.Kind.StartsWith("shell_simpl_capri_");

            var concreteChars = concreteMat.chars[calcType];
            var rebarChars = rebarMat.chars[calcType];

            double h = section.H;
            AggregateRebar(section, h, out double cb, out double ct,
                out double As_x_bot, out double As_x_top,
                out double As_y_bot, out double As_y_top,
                out double ds_x, out double ds_y);

            List<ShellSimplStripResult>? waStrips = null;
            List<ShellSimplDirectionResult>? capriDirs = null;
            ShellSimplDirectionResult? critTop = null;
            ShellSimplDirectionResult? critBot = null;
            double? etaMax = null;

            if (isWa)
            {
                WaFace(p.Mx, p.My, p.Mxy, true, out double Mx_top, out double My_top);
                WaFace(p.Mx, p.My, p.Mxy, false, out double Mx_bot, out double My_bot);
                WaMembrane(p.Nx, p.Ny, p.Nxy, true, out double Nxr, out double Nyr);

                waStrips = new List<ShellSimplStripResult>(4);

                if (isSls)
                {
                    waStrips.Add(MakeStripSls("x, верх", Mx_top, Nxr, h, h - ct, cb,
                        As_x_top, As_x_bot, ds_x, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm, p.SigmaSCrc, p.WplGamma));
                    waStrips.Add(MakeStripSls("x, низ", Mx_bot, Nxr, h, h - cb, ct,
                        As_x_bot, As_x_top, ds_x, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm, p.SigmaSCrc, p.WplGamma));
                    waStrips.Add(MakeStripSls("y, верх", My_top, Nyr, h, h - ct, cb,
                        As_y_top, As_y_bot, ds_y, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm, p.SigmaSCrc, p.WplGamma));
                    waStrips.Add(MakeStripSls("y, низ", My_bot, Nyr, h, h - cb, ct,
                        As_y_bot, As_y_top, ds_y, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm, p.SigmaSCrc, p.WplGamma));
                }
                else
                {
                    waStrips.Add(MakeStripUls("x, верх", Mx_top, Nxr, h, h - ct, cb,
                        As_x_top, As_x_bot, concreteChars, rebarChars));
                    waStrips.Add(MakeStripUls("x, низ", Mx_bot, Nxr, h, h - cb, ct,
                        As_x_bot, As_x_top, concreteChars, rebarChars));
                    waStrips.Add(MakeStripUls("y, верх", My_top, Nyr, h, h - ct, cb,
                        As_y_top, As_y_bot, concreteChars, rebarChars));
                    waStrips.Add(MakeStripUls("y, низ", My_bot, Nyr, h, h - cb, ct,
                        As_y_bot, As_y_top, concreteChars, rebarChars));
                    etaMax = waStrips.Max(s => s.Eta);
                }
            }

            if (isCapri)
            {
                int n = Math.Max(1, (int)Math.Round(180.0 / p.StepDeg));
                capriDirs = new List<ShellSimplDirectionResult>(n);
                for (int i = 0; i < n; i++)
                {
                    double aDeg = i * p.StepDeg;
                    capriDirs.Add(DirectionStrip(aDeg,
                        p.Nx, p.Ny, p.Nxy, p.Mx, p.My, p.Mxy,
                        h, cb, ct,
                        As_x_top, As_y_top, As_x_bot, As_y_bot,
                        ds_x, ds_y,
                        concreteChars, rebarChars,
                        isSls, p.Phi1, p.Phi2, p.AcrcLimMm, p.SigmaSCrc, p.WplGamma));
                }

                if (isSls)
                {
                    var topDirs = capriDirs.Where(d => d.Top && !d.Strip.NoRebar).ToList();
                    var botDirs = capriDirs.Where(d => !d.Top && !d.Strip.NoRebar).ToList();
                    critTop = topDirs.Count > 0 ? topDirs.MaxBy(d => d.Strip.Acrc_mm) : null;
                    critBot = botDirs.Count > 0 ? botDirs.MaxBy(d => d.Strip.Acrc_mm) : null;
                }
                else
                {
                    var topDirs = capriDirs.Where(d => d.Top && !d.Strip.NoRebar).ToList();
                    var botDirs = capriDirs.Where(d => !d.Top && !d.Strip.NoRebar).ToList();
                    critTop = topDirs.Count > 0 ? topDirs.MaxBy(d => d.Strip.Eta) : null;
                    critBot = botDirs.Count > 0 ? botDirs.MaxBy(d => d.Strip.Eta) : null;
                    etaMax = capriDirs.Where(d => !d.Strip.NoRebar).Select(d => d.Strip.Eta).DefaultIfEmpty(0.0).Max();
                }
            }

            return new SolveResult(
                isWa ? "wa" : "capri",
                isSls ? "sls" : "uls",
                p, waStrips, capriDirs, critTop, critBot, etaMax
            );
        }

        static void AggregateRebar(PlateSection section, double h,
            out double coverBot, out double coverTop,
            out double As_x_bot, out double As_x_top,
            out double As_y_bot, out double As_y_top,
            out double ds_x, out double ds_y)
        {
            As_x_bot = 0; As_x_top = 0; As_y_bot = 0; As_y_top = 0;
            double? cb = null, ct = null;
            ds_x = 0.012; ds_y = 0.012;

            double half = h / 2.0;
            foreach (var rl in section.RebarLayers)
            {
                if (rl.Zsx <= 0.0)
                {
                    As_x_bot += rl.Asx; As_y_bot += rl.Asy;
                    double c = half + rl.Zsx;
                    if (cb == null || c < cb) cb = c;
                    if (rl.DiameterX > 0) ds_x = rl.DiameterX;
                    if (rl.DiameterY > 0) ds_y = rl.DiameterY;
                }
                else
                {
                    As_x_top += rl.Asx; As_y_top += rl.Asy;
                    double c = half - rl.Zsx;
                    if (ct == null || c < ct) ct = c;
                    if (rl.DiameterX > 0 && As_x_bot < 1e-14) ds_x = rl.DiameterX;
                    if (rl.DiameterY > 0 && As_y_bot < 1e-14) ds_y = rl.DiameterY;
                }
            }

            coverBot = cb ?? 0.05;
            coverTop = ct ?? 0.05;
        }

        internal static void WaFace(double Mx, double My, double Mxy, bool top,
            out double Mx_des, out double My_des)
        {
            if (!top) { Mx = -Mx; My = -My; }
            double absMxy = Math.Abs(Mxy);
            double Mx_s = Mx + absMxy;
            double My_s = My + absMxy;
            if (Mx_s < 0.0 && Math.Abs(Mx) > 1e-12)
            {
                My_s = My + Mxy * Mxy / Math.Abs(Mx);
                Mx_s = 0.0;
            }
            if (My_s < 0.0 && Math.Abs(My) > 1e-12)
            {
                Mx_s = Mx + Mxy * Mxy / Math.Abs(My);
                My_s = 0.0;
            }
            Mx_des = Math.Max(0.0, Mx_s);
            My_des = Math.Max(0.0, My_s);
        }

        internal static void WaMembrane(double Nx, double Ny, double Nxy, bool tensile,
            out double Nx_des, out double Ny_des)
        {
            if (!tensile) { Nx = -Nx; Ny = -Ny; }
            double absNxy = Math.Abs(Nxy);
            double Nxr = Nx + absNxy;
            double Nyr = Ny + absNxy;
            if (Nxr < 0.0 && Math.Abs(Nx) > 1e-12)
            {
                Nyr = Ny + Nxy * Nxy / Math.Abs(Nx);
                Nxr = 0.0;
            }
            if (Nyr < 0.0 && Math.Abs(Ny) > 1e-12)
            {
                Nxr = Nx + Nxy * Nxy / Math.Abs(Ny);
                Nyr = 0.0;
            }
            if (tensile)
            {
                Nx_des = Math.Max(0.0, Nxr);
                Ny_des = Math.Max(0.0, Nyr);
            }
            else
            {
                Nx_des = -Math.Max(0.0, Nxr);
                Ny_des = -Math.Max(0.0, Nyr);
            }
        }

        internal static double NeutralAxis(double h0, double aPrime,
            double As_t, double As_c, double alpha)
        {
            double B = alpha * (As_c + As_t);
            double C = -alpha * (As_t * h0 + As_c * aPrime);
            double disc = B * B - 2.0 * C;
            if (disc < 0.0) disc = 0.0;
            double xm = -B + Math.Sqrt(disc);
            return Math.Max(0.0, Math.Min(xm, h0));
        }

        /// <summary>
        /// П. 8.2.16, ф. (8.134): напряжение в растянутой арматуре сечения с трещиной
        /// от совместного действия M и N (N со знаком: "+" — растяжение), с приведением
        /// к сечению с трещиной (aRedCrc/iRedCrc — площадь/момент инерции приведённого
        /// сечения по сжатой зоне бетона и арматуре, см. вызывающий код).
        /// </summary>
        internal static double ComputeSigmaSCrackedSection(
            double M, double N, double h0, double xm,
            double aRedCrc, double iRedCrc, double alpha, double rsSer)
        {
            if (iRedCrc < 1e-15 || aRedCrc < 1e-15) return 0.0;
            double sigma = alpha * (M * (h0 - xm) / iRedCrc + N / aRedCrc);
            return Math.Clamp(sigma, 0.0, rsSer);
        }

        /// <summary>
        /// Коэффициент пластичности γ для Wpl = γ·Wred по выбранному источнику.
        /// μs считается по полной высоте сечения (As/(b·h)) — так он определён в источнике
        /// формулы Radaykin2018.
        /// </summary>
        internal static double ResolveWplGamma(WplGammaMethod method, double asT, double b, double h)
        {
            switch (method)
            {
                case WplGammaMethod.Snip2030184:
                    return 1.75;
                case WplGammaMethod.Radaykin2018:
                    double mu = b > 1e-15 && h > 1e-15 ? asT / (b * h) : 0.0;
                    return mu > 1e-12 ? 1.6 + 1.0 / (100.0 * Math.Sqrt(mu)) : 1.75;
                default:
                    return 1.3;
            }
        }

        /// <summary>
        /// П. 8.2.18, ф. (8.137): σs,crc — напряжение в растянутой арматуре в сечении с
        /// трещиной сразу после её образования, посчитанное ПО НАПРЯЖЕНИЯМ, а не через
        /// отношение моментов (8.138).
        ///
        /// В момент образования трещины растянутый бетон рабочей зоны Abt перестаёт нести
        /// усилие Rbt,ser·Abt, и оно целиком переходит в арматуру. Приращение напряжения с
        /// учётом упругого перераспределения (смещения нейтральной оси) даёт
        ///     σs,crc = Rbt,ser/ρs · (1 + αs·ρs),   ρs = As_t/Abt,   αs = Es/Eb,
        /// то есть σs,crc = Rbt,ser·(Abt + αs·As_t)/As_t. Модуль бетона берётся начальный
        /// (Eb), а не приведённый Eb,red: перераспределение при образовании трещины —
        /// мгновенное, ползучесть в нём не участвует.
        ///
        /// Вариант (8.138) σs,crc/σs = Mcrc/M в упрощённом пути вырождается: σs,crc,
        /// полученное подстановкой Mcrc в ту же упругую модель сечения с трещиной, даёт
        /// ровно отношение моментов, и точность ψs целиком определяется точностью
        /// формульного Mcrc = Rbt,ser·1,3·Wred. Этот Wpl заведомо занижен (см. тест примера
        /// 47), из-за чего ψs завышался, а acrc уходила в запас на 15-25%.
        /// </summary>
        internal static double SigmaSCrcFromReleasedConcrete(
            double rbt, double abt, double asT, double alphaFull, double sigmaS)
        {
            if (asT < 1e-15 || abt < 1e-15 || rbt <= 0.0) return 0.0;
            double sigma = rbt * (abt + alphaFull * asT) / asT;
            return Math.Clamp(sigma, 0.0, Math.Max(0.0, sigmaS));
        }

        internal static void FullSectionProps(double h, double h0, double aPrime,
            double As_t, double As_c, double alphaFull,
            out double A_red, out double I_red)
        {
            double A_b = h;
            double A_st = alphaFull * As_t;
            double A_sc = alphaFull * As_c;
            A_red = A_b + A_st + A_sc;
            double S_red = h * h / 2.0 + A_st * h0 + A_sc * aPrime;
            double yc = S_red / A_red;
            double I_b = h * h * h / 12.0 + h * (yc - h / 2.0) * (yc - h / 2.0);
            double I_st = A_st * (yc - h0) * (yc - h0);
            double I_sc = A_sc * (yc - aPrime) * (yc - aPrime);
            I_red = I_b + I_st + I_sc;
        }

        static ShellSimplStripResult MakeStripSls(string name,
            double M_des, double N_des, double h, double h0, double aPrime,
            double As_t, double As_c, double ds,
            MaterialChars concrete, MaterialChars rebar,
            double phi1, double phi2, double acrcLimMm,
            SigmaSCrcMethod sigmaSCrcMethod, WplGammaMethod wplGamma)
        {
            var r = ComputeStripSls(M_des, N_des, h, h0, aPrime, As_t, As_c, ds,
                concrete, rebar, phi1, phi2, acrcLimMm, sigmaSCrcMethod, wplGamma);
            r.Name = name;
            return r;
        }

        static ShellSimplStripResult MakeStripUls(string name,
            double M_des, double N_des, double h, double h0, double aPrime,
            double As_t, double As_c,
            MaterialChars concrete, MaterialChars rebar)
        {
            var r = ComputeStripUls(M_des, N_des, h, h0, aPrime, As_t, As_c, concrete, rebar);
            r.Name = name;
            return r;
        }

        internal static ShellSimplStripResult ComputeStripSls(
            double M_des, double N_des, double h, double h0, double a_prime,
            double As_t, double As_c, double ds,
            MaterialChars concrete, MaterialChars rebar,
            double phi1, double phi2, double acrcLimMm,
            SigmaSCrcMethod sigmaSCrcMethod = SigmaSCrcMethod.ReleasedConcrete8137,
            WplGammaMethod wplGamma = WplGammaMethod.Sp63)
        {
            bool noRebar = As_t < 1e-12;
            var r = new ShellSimplStripResult
            {
                M_des = M_des, N_des = N_des,
                H0 = h0, A_prime = a_prime, As_t = As_t, As_c = As_c, Ds = ds,
                NoRebar = noRebar
            };
            if (noRebar) return r;

            // Все характеристики материала уже в кПа
            double Eb = concrete.E;          // кПа
            double Rb_ser = Math.Abs(concrete.Fc);  // кПа
            double Rbt = concrete.Ft;        // кПа
            double Es = rebar.E;             // кПа
            double Rs_ser = Math.Abs(rebar.Ft);      // кПа (для арматуры Ft = Rs_ser)
            double Eb_red = Rb_ser / 0.0015;
            double alphaFull = Es / Eb;
            double alpha = Es / Eb_red;
            double b = 1.0;

            FullSectionProps(h, h0, a_prime, As_t, As_c, alphaFull,
                out double A_red, out double I_red);
            double S_red = h * h / 2.0 + alphaFull * As_t * h0 + alphaFull * As_c * a_prime;
            double ycFull = S_red / A_red;
            double yt = h - ycFull;
            double Wred = I_red / yt;
            double gamma = ResolveWplGamma(wplGamma, As_t, b, h);
            r.Gamma = gamma;
            double Wpl = gamma * Wred;
            double ex = Wred / A_red;

            double mcrc = Rbt * Wpl - N_des * ex;
            if (mcrc < 0.0) mcrc = 0.0;
            r.Mcrc = mcrc;
            r.Cracked = M_des > mcrc;

            // Высота сжатой зоны сечения с трещиной, п. 8.2.28. Изгибная составляющая x_M —
            // по ф. (8.151), она от M и N не зависит. Но п. 8.2.28 озаглавлен «для ИЗГИБАЕМЫХ
            // элементов»; при действии продольной силы её поправляют по ф. (8.154):
            //     x_m = x_M ± I_red·N/(A_red·M),   знак «−» при растягивающей N.
            // I_red, A_red — полного сечения (без учёта трещин), с тем же коэффициентом
            // приведения αs1, что и x_M (п. 8.2.16: «принимая αs2 = αs1»), поэтому считаются
            // отдельно от A_red/I_red выше, где взят начальный модуль Eb для Mcrc.
            // Высота сжатой зоны сечения с трещиной, п. 8.2.28. Изгибная составляющая x_M —
            // по ф. (8.151), она от M и N не зависит. Но п. 8.2.28 озаглавлен «для ИЗГИБАЕМЫХ
            // элементов»; при действии продольной силы её поправляют по ф. (8.154):
            //     x_m = x_M ± I_red·N/(A_red·M),   знак «−» при растягивающей N.
            // I_red, A_red в (8.154) считаются с тем же коэффициентом приведения αs1, что и x_M.
            // Норма формулировкой «для полного сечения (без учёта трещин)» коэффициент не
            // называет, и прочтений два: (а) это объект п. 8.2.12, где α = Es/Eb по начальному
            // модулю; (б) это часть расчёта x_m по 8.2.28, а п. 8.2.16 прямо предписывает вести
            // его «принимая коэффициент приведения арматуры к бетону αs2 = αs1». Принято (б):
            // указание 8.2.16 адресовано именно определению x_m, тогда как формулировка п. 8.2.12
            // обслуживает Mcrc. Разница заметная: I_red/A_red = 3472 мм² против 3396 мм².
            double xM = NeutralAxis(h0, a_prime, As_t, As_c, alpha);
            FullSectionProps(h, h0, a_prime, As_t, As_c, alpha,
                out double A_red_s1, out double I_red_s1);
            double armSls = h0 - a_prime;

            // Напряжение в растянутой арматуре по п. 8.2.16 для ПРОИЗВОЛЬНОГО момента.
            // Вынесено в функцию, потому что п. 8.2.18 определяет σs,crc не отдельной формулой,
            // а как ту же σs с подстановкой M = Mcrc: «...определяемое по 8.2.16, принимая в
            // соответствующих формулах значения M = M_crc». Значит обе величины обязаны
            // считаться одним путём, включая поправку (8.154) и вырождение при x_m ≤ 0.
            double SigmaSAtMoment(double m)
            {
                double x;
                if (m > 1e-9) x = xM - I_red_s1 * N_des / (A_red_s1 * m);
                else x = N_des > 1e-9 ? -1.0 : xM;    // чистое растяжение — сжатой зоны нет вовсе
                if (x > h0) x = h0;

                if (x > 1e-9)
                {
                    // Приведённые площадь/момент инерции сечения С ТРЕЩИНОЙ (сжатая зона бетона
                    // + арматура, п. 8.2.16) — в отличие от A_red/I_red выше, тех же величин БЕЗ
                    // трещины, используемых только для Mcrc (п. 8.2.11-8.2.12).
                    double aCrc = x + alpha * (As_t + As_c);
                    double iCrc = x * x * x / 3.0
                        + alpha * As_t * (h0 - x) * (h0 - x)
                        + alpha * As_c * (x - a_prime) * (x - a_prime);
                    // Ф. (8.134): σs = [M·(h0−yc)/Ired ± N/Ared]·αs1, yc = x.
                    return ComputeSigmaSCrackedSection(m, N_des, h0, x, aCrc, iCrc, alpha, Rs_ser);
                }

                // x_m ≤ 0: сжатой зоны нет, сечение растянуто насквозь. Предпосылка ф. (8.134)
                // (упругое сечение со сжатой зоной бетона) не выполняется, и размазывание N по
                // A_red,crc занижало бы σs в разы. Бетон выключается из работы, растяжение
                // делят оба ряда арматуры — та же схема, что в п. 8.1.19а для прочности:
                //     F_t + F_c = N;   F_t·z_t + F_c·z_c = M   (z — от центра тяжести сечения)
                // откуда F_t = (M + N·(h/2 − a')) / (h0 − a').
                // При M → 0 даёт σs = N/(As + A's) — точное значение для центрального растяжения.
                double f = armSls > 1e-12 ? (m + N_des * (h / 2.0 - a_prime)) / armSls : 0.0;
                double sg = As_t > 1e-14 ? f / As_t : 0.0;
                return Math.Clamp(sg, 0.0, Rs_ser);
            }

            double xm;
            if (M_des > 1e-9) xm = xM - I_red_s1 * N_des / (A_red_s1 * M_des);
            else xm = N_des > 1e-9 ? -1.0 : xM;
            if (xm > h0) xm = h0;
            r.Xm = xm;
            r.Zs = h0 - Math.Max(0.0, xm) / 3.0;

            double sigma_s = SigmaSAtMoment(M_des);
            r.Sigma_s_MPa = sigma_s / 1000.0;

            // П. 8.2.17: высота растянутой зоны для Abt берётся ПО РАСЧЁТУ МОМЕНТА
            // ОБРАЗОВАНИЯ ТРЕЩИН, то есть по нейтральной оси приведённого сечения БЕЗ
            // трещины (ycFull), а не по оси сечения с трещиной xm. Ограничения нормы —
            // 2a ≤ xt ≤ 0,5·h0 (так же, как в ComputeAbt деформационного решателя).
            double a_tens = h - h0;
            double xtCrc = Math.Max(0.0, yt);   // yt = h − ycFull, ось сечения БЕЗ трещины
            double h_bt = Math.Min(Math.Max(xtCrc, 2.0 * a_tens), h0 / 2.0);
            double Abt = b * h_bt;
            double lsRaw = 0.5 * Abt / As_t * ds;
            double lsMin = Math.Max(10.0 * ds, 0.10);
            double lsMax = Math.Min(40.0 * ds, 0.40);
            double ls_m = Math.Max(lsMin, Math.Min(lsRaw, lsMax));
            r.Ls_m = ls_m;

            // П. 8.2.18: σs,crc — напряжение в арматуре сразу после образования трещин,
            // «определяемое по 8.2.16, принимая в соответствующих формулах значения M = Mcrc».
            // Отдельной формулы для неё в СП 63 нет: (8.137) — это ψs = 1 − 0,8·σs,crc/σs,
            // а (8.138) — упрощённая ψs = 1 − 0,8·Mcrc/M «для изгибаемых элементов».
            double sigma_s_crc = sigma_s;
            if (r.Cracked)
            {
                if (sigmaSCrcMethod == SigmaSCrcMethod.CrackingMoment8138)
                {
                    // Ф. (8.138): отношение задаётся напрямую моментами. σs,crc выводится
                    // обратным пересчётом, чтобы отчётная величина отвечала принятому ψs.
                    double ratio = M_des > 1e-9 ? Math.Clamp(mcrc / M_des, 0.0, 1.0) : 0.0;
                    sigma_s_crc = sigma_s * ratio;
                }
                else
                {
                    // Общее определение п. 8.2.18 — та же цепочка при M = Mcrc.
                    sigma_s_crc = Math.Min(SigmaSAtMoment(mcrc), sigma_s);
                }
            }
            r.Sigma_s_crc_MPa = sigma_s_crc / 1000.0;

            double psi_s = 1.0;
            if (r.Cracked && sigma_s > 1e-3)
            {
                psi_s = 1.0 - 0.8 * sigma_s_crc / sigma_s;
                if (psi_s < 0.1) psi_s = 0.1;
                if (psi_s > 1.0) psi_s = 1.0;
            }
            r.Psi_s = psi_s;

            double phi3 = N_des > 1e-3 ? 1.2 : 1.0;

            double acrc_mm = 0;
            if (r.Cracked && sigma_s > 1e-3)
            {
                double acrc_m = phi1 * phi2 * phi3 * psi_s * sigma_s / Es * ls_m;
                acrc_mm = acrc_m * 1000.0;
            }
            r.Acrc_mm = acrc_mm;

            double Es_red = Es / psi_s;
            double zStiff = h0 - xm / 3.0;
            r.B_kNm2 = Es_red * As_t * zStiff * (h0 - xm);

            return r;
        }

        internal static ShellSimplStripResult ComputeStripUls(
            double M_des, double N_des, double h, double h0, double a_prime,
            double As_t, double As_c,
            MaterialChars concrete, MaterialChars rebar)
        {
            bool noRebar = As_t < 1e-12;
            var r = new ShellSimplStripResult
            {
                M_des = M_des, N_des = N_des,
                H0 = h0, A_prime = a_prime, As_t = As_t, As_c = As_c,
                NoRebar = noRebar
            };
            if (noRebar) return r;

            // Все характеристики материала уже в кПа
            double Rb = Math.Abs(concrete.Fc);
            double Rs = Math.Abs(rebar.Ft);      // для арматуры Ft = Rs
            double Rsc = Math.Abs(rebar.Fc);     // для арматуры Fc = Rsc
            double Es = rebar.E;
            double b = 1.0;
            double xi_r = 0.8 / (1.0 + Rs / (Es * 0.0035));
            r.Xi_R = xi_r;
            double arm = h0 - a_prime;

            const double N_THRESHOLD = 1e-3;
            double x, m_ult, demand;
            string caseStr;

            if (N_des < -N_THRESHOLD)
            {
                double N_c = -N_des;
                x = (N_c + Rs * As_t - Rsc * As_c) / (Rb * b);
                if (x / h0 > xi_r)
                {
                    double num = N_c + Rs * As_t * (1.0 + xi_r) / (1.0 - xi_r) - Rsc * As_c;
                    double den = Rb * b + 2.0 * Rs * As_t / (h0 * (1.0 - xi_r));
                    x = num / den;
                }
                x = Math.Max(0.0, Math.Min(x, h0));
                m_ult = Rb * b * x * (h0 - 0.5 * x) + Rsc * As_c * arm;
                if (M_des < N_THRESHOLD)
                {
                    demand = N_c * arm / 2.0;
                    caseStr = "Центр. сжатие, N·e ≤ M_ult (§8.1.14)";
                }
                else
                {
                    double e0 = M_des / N_c;
                    double e = e0 + arm / 2.0;
                    demand = N_c * e;
                    caseStr = "Внецентр. сжатие, N·e ≤ M_ult (§8.1.14)";
                }
            }
            else if (Math.Abs(N_des) <= N_THRESHOLD)
            {
                x = (Rs * As_t - Rsc * As_c) / (Rb * b);
                x = Math.Max(0.0, Math.Min(x, xi_r * h0));

                // П. 8.1.9: при x ≤ 2a' сжатая арматура не дорабатывает до Rsc, и ф. (8.5)
                // занижает плечо внутренней пары (равнодействующая сжатия уезжает к грани).
                // В этом случае норма переходит на ф. (8.9) — момент относительно
                // равнодействующей сжатой зоны бетона при исключённой сжатой арматуре.
                // Та же трактовка, что в формульной проверке нормального сечения
                // (Sp63NormalChecker), формула переиспользуется, а не дублируется.
                if (x <= 2.0 * a_prime)
                {
                    double xNoCompression = Rs * As_t / (Rb * b);
                    m_ult = Sp63.Normal.Sp63NormalFormulas.SymmetricMoment(
                        Rs, As_t, h0, a_prime, xNoCompression, compressionRebarWasExcluded: true);
                    // Отчётная высота сжатой зоны — та, что отвечает принятому равновесию.
                    x = Math.Min(xNoCompression, h0);
                    caseStr = "Изгиб, x ≤ 2a′, M ≤ M_ult (§8.1.9)";
                }
                else
                {
                    m_ult = Rb * b * x * (h0 - 0.5 * x) + Rsc * As_c * arm;
                    caseStr = "Изгиб, M ≤ M_ult (§8.1.8)";
                }
                demand = M_des;
            }
            else
            {
                double e0 = M_des / N_des;
                double half = arm / 2.0;
                if (e0 < half && As_c > 1e-12)
                {
                    double e_t = half - e0;
                    double e_c = half + e0;
                    // П. 8.1.19а, рисунок 8.4а: плечо до ОДНОГО стержня спаривается с несущей
                    // способностью ДРУГОГО — (8.20) N·e ≤ Rs·A's·(h0−a') и (8.21) N·e' ≤ Rs·As·(h0−a').
                    // Момент относительно стержня уравновешивает противоположный стержень, поэтому
                    // e_t (плечо до растянутого ряда) идёт с As_c, а e_c — с As_t.
                    double M_t = Rs * As_c * arm;
                    double M_c = Rs * As_t * arm;
                    double dem_t = N_des * e_t;
                    double dem_c = N_des * e_c;
                    double eta_t = M_t > 1e-9 ? dem_t / M_t : double.MaxValue;
                    double eta_c = M_c > 1e-9 ? dem_c / M_c : double.MaxValue;
                    if (eta_c >= eta_t)
                    {
                        demand = dem_c; m_ult = M_c;
                        caseStr = "Внецентр. растяж., N·e' ≤ M'_ult (§8.1.19а)";
                    }
                    else
                    {
                        demand = dem_t; m_ult = M_t;
                        caseStr = "Внецентр. растяж., N·e ≤ M_ult (§8.1.19а)";
                    }
                    x = 0.0;
                }
                else if (e0 < half && As_c <= 1e-12)
                {
                    m_ult = Rs * As_t * arm;
                    demand = N_des * half;
                    caseStr = "Центр. растяж., N ≤ N_ult (§8.1.18)";
                    x = 0.0;
                }
                else
                {
                    x = (Rs * As_t - Rsc * As_c - N_des) / (Rb * b);   // (8.25)
                    if (x > xi_r * h0) x = xi_r * h0;                  // норма: подставляют ξR·h0
                    if (x <= 0.0)
                    {
                        // (8.25) дала x ≤ 0: сжатой зоны нет, и предпосылка (8.24) с Rsc·A's
                        // не выполняется — противоположный стержень не может быть сжат. Норма
                        // этот вырожденный случай не оговаривает; сечение работает как в 8.1.19а,
                        // поэтому переходим на условие (8.21) с M'_ult по (8.23). Это же делает
                        // η непрерывной на границе e0 = (h0−a')/2 и совпадает с равновесием
                        // двух растянутых рядов.
                        x = 0.0;
                        m_ult = Rs * As_t * arm;
                        demand = N_des * (e0 + half);
                        caseStr = "Внецентр. растяж., нет сжатой зоны, N·e' ≤ M'_ult (§8.1.19, ф. 8.21)";
                    }
                    else
                    {
                        m_ult = Rb * b * x * (h0 - 0.5 * x) + Rsc * As_c * arm;   // (8.24)
                        demand = N_des * (e0 - half);                             // (8.20)
                        caseStr = "Внецентр. растяж., N·e ≤ M_ult (§8.1.19б)";
                    }
                }
            }

            r.Xm = x;
            r.Xi = x / h0;
            r.M_ult = m_ult;
            r.Demand = demand;
            r.Eta = m_ult > 1e-9 ? demand / m_ult : double.MaxValue;
            r.Case = caseStr;

            // σs в растянутой арматуре при ULS (кПа → МПа)
            if (x > 1e-9 && x < h0)
            {
                double eps_s = 0.0035 * (h0 - x) / x;
                double sig = Math.Min(eps_s * Es, Rs);
                r.Sigma_s_MPa = Math.Max(0, sig) / 1000.0;
            }
            else if (x <= 1e-9)
            {
                if (M_des > N_THRESHOLD || N_des > N_THRESHOLD)
                    r.Sigma_s_MPa = Rs / 1000.0;
                else
                    r.Sigma_s_MPa = 0;
            }
            else
            {
                r.Sigma_s_MPa = 0;
            }
            return r;
        }

        static ShellSimplDirectionResult DirectionStrip(
            double alphaDeg, double Nx, double Ny, double Nxy,
            double Mx, double My, double Mxy,
            double h, double cb, double ct,
            double As_x_top, double As_y_top, double As_x_bot, double As_y_bot,
            double ds_x, double ds_y,
            MaterialChars concreteChars, MaterialChars rebarChars,
            bool isSls, double phi1, double phi2, double acrcLimMm,
            SigmaSCrcMethod sigmaSCrcMethod, WplGammaMethod wplGamma)
        {
            double alpha = alphaDeg * Math.PI / 180.0;
            double c = Math.Cos(alpha), s = Math.Sin(alpha);
            double c2 = c * c, s2 = s * s, cs = c * s;

            double M_n = Mx * c2 + My * s2 + 2.0 * Mxy * cs;
            double N_n = Nx * c2 + Ny * s2 + 2.0 * Nxy * cs;

            double As_n_top = As_x_top * c2 + As_y_top * s2;
            double As_n_bot = As_x_bot * c2 + As_y_bot * s2;

            double ds_top = EffDs(As_x_top, ds_x, As_y_top, ds_y, c2, s2);
            double ds_bot = EffDs(As_x_bot, ds_x, As_y_bot, ds_y, c2, s2);

            bool top = M_n >= 0.0;
            ShellSimplStripResult strip;
            if (isSls)
            {
                strip = top
                    ? ComputeStripSls(Math.Abs(M_n), N_n, h, h - ct, cb,
                        As_n_top, As_n_bot, ds_top, concreteChars, rebarChars, phi1, phi2, acrcLimMm, sigmaSCrcMethod, wplGamma)
                    : ComputeStripSls(Math.Abs(M_n), N_n, h, h - cb, ct,
                        As_n_bot, As_n_top, ds_bot, concreteChars, rebarChars, phi1, phi2, acrcLimMm, sigmaSCrcMethod, wplGamma);
            }
            else
            {
                strip = top
                    ? ComputeStripUls(Math.Abs(M_n), N_n, h, h - ct, cb,
                        As_n_top, As_n_bot, concreteChars, rebarChars)
                    : ComputeStripUls(Math.Abs(M_n), N_n, h, h - cb, ct,
                        As_n_bot, As_n_top, concreteChars, rebarChars);
            }

            return new ShellSimplDirectionResult
            {
                Alpha_deg = alphaDeg, M_n = M_n, N_n = N_n, Top = top, Strip = strip
            };
        }

        static double EffDs(double Ax, double dx, double Ay, double dy,
            double c2, double s2)
        {
            double w = Ax * c2 + Ay * s2;
            if (w < 1e-14) return (dx + dy) / 2.0;
            return (Ax * c2 * dx + Ay * s2 * dy) / w;
        }
    }
}

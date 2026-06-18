using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore
{
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
            double Phi2 = 0.5
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
                        As_x_top, As_x_bot, ds_x, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm));
                    waStrips.Add(MakeStripSls("x, низ", Mx_bot, Nxr, h, h - cb, ct,
                        As_x_bot, As_x_top, ds_x, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm));
                    waStrips.Add(MakeStripSls("y, верх", My_top, Nyr, h, h - ct, cb,
                        As_y_top, As_y_bot, ds_y, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm));
                    waStrips.Add(MakeStripSls("y, низ", My_bot, Nyr, h, h - cb, ct,
                        As_y_bot, As_y_top, ds_y, concreteChars, rebarChars, p.Phi1, p.Phi2, p.AcrcLimMm));
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
                        isSls, p.Phi1, p.Phi2, p.AcrcLimMm));
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
            double phi1, double phi2, double acrcLimMm)
        {
            var r = ComputeStripSls(M_des, N_des, h, h0, aPrime, As_t, As_c, ds,
                concrete, rebar, phi1, phi2, acrcLimMm);
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
            double phi1, double phi2, double acrcLimMm)
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
            double Wpl = 1.3 * Wred;
            double ex = Wred / A_red;

            double mcrc = Rbt * Wpl - N_des * ex;
            if (mcrc < 0.0) mcrc = 0.0;
            r.Mcrc = mcrc;
            r.Cracked = M_des > mcrc;

            double xmM = NeutralAxis(h0, a_prime, As_t, As_c, alpha);
            double xm = xmM;
            if (Math.Abs(M_des) > 1e-9 && Math.Abs(N_des) > 1e-6)
            {
                double corr = I_red * N_des / (A_red * M_des);
                xm = Math.Max(0.0, Math.Min(xmM - corr, h0));
            }
            r.Xm = xm;
            double zs = h0 - xm / 3.0;
            r.Zs = zs;

            double sigma_s = 0;
            if (zs > 1e-9)
            {
                double AsTot = As_t + (As_c > 0 ? As_c : As_t);
                sigma_s = M_des / (zs * As_t) + N_des / AsTot;
                if (sigma_s > Rs_ser) sigma_s = Rs_ser;
                if (sigma_s < 0) sigma_s = 0;
            }
            r.Sigma_s_MPa = sigma_s / 1000.0;

            double sigma_s_crc = sigma_s;
            if (r.Cracked && mcrc > 1e-9)
            {
                double xmCrc = xmM;
                if (Math.Abs(mcrc) > 1e-9 && Math.Abs(N_des) > 1e-6)
                {
                    double corrCrc = I_red * N_des / (A_red * mcrc);
                    xmCrc = Math.Max(0.0, Math.Min(xmM - corrCrc, h0));
                }
                double zsCrc = h0 - xmCrc / 3.0;
                if (zsCrc > 1e-9)
                {
                    double AsTot = As_t + (As_c > 0 ? As_c : As_t);
                    double sc = mcrc / (zsCrc * As_t) + N_des / AsTot;
                    if (sc > Rs_ser) sc = Rs_ser;
                    if (sc < 0) sc = 0;
                    sigma_s_crc = sc;
                }
            }

            double psi_s = 1.0;
            if (r.Cracked && sigma_s > 1e-3)
            {
                psi_s = 1.0 - 0.8 * sigma_s_crc / sigma_s;
                if (psi_s < 0.1) psi_s = 0.1;
                if (psi_s > 1.0) psi_s = 1.0;
            }
            r.Psi_s = psi_s;

            double a_tens = h - h0;
            double xtFull = Math.Max(0.0, h - xm);
            double h_bt = Math.Min(Math.Max(xtFull, 2.0 * a_tens), h0 / 2.0);
            double Abt = b * h_bt;
            double lsRaw = 0.5 * Abt / As_t * ds;
            double lsMin = Math.Max(10.0 * ds, 0.10);
            double lsMax = Math.Min(40.0 * ds, 0.40);
            double ls_m = Math.Max(lsMin, Math.Min(lsRaw, lsMax));
            r.Ls_m = ls_m;

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
                    caseStr = "Центр. сжатие → §8.1.14";
                }
                else
                {
                    double e0 = M_des / N_c;
                    double e = e0 + arm / 2.0;
                    demand = N_c * e;
                    caseStr = "Внецентр. сжатие (§8.1.14)";
                }
            }
            else if (Math.Abs(N_des) <= N_THRESHOLD)
            {
                x = (Rs * As_t - Rsc * As_c) / (Rb * b);
                x = Math.Max(0.0, Math.Min(x, xi_r * h0));
                m_ult = Rb * b * x * (h0 - 0.5 * x) + Rsc * As_c * arm;
                demand = M_des;
                caseStr = "Изгиб (§8.1.9)";
            }
            else
            {
                double e0 = M_des / N_des;
                double half = arm / 2.0;
                if (e0 < half && As_c > 1e-12)
                {
                    double e_t = half - e0;
                    double e_c = half + e0;
                    double M_t = Rs * As_t * arm;
                    double M_c = Rs * As_c * arm;
                    double dem_t = N_des * e_t;
                    double dem_c = N_des * e_c;
                    double eta_t = M_t > 1e-9 ? dem_t / M_t : double.MaxValue;
                    double eta_c = M_c > 1e-9 ? dem_c / M_c : double.MaxValue;
                    if (eta_c >= eta_t)
                    {
                        demand = dem_c; m_ult = M_c;
                        caseStr = "Растяж. малый e, стор. As' (§8.1.19а)";
                    }
                    else
                    {
                        demand = dem_t; m_ult = M_t;
                        caseStr = "Растяж. малый e, стор. As (§8.1.19а)";
                    }
                    x = 0.0;
                }
                else if (e0 < half && As_c <= 1e-12)
                {
                    m_ult = Rs * As_t * arm;
                    demand = N_des * half;
                    caseStr = "Центр./малый e растяж. (§8.1.18)";
                    x = 0.0;
                }
                else
                {
                    x = (Rs * As_t - Rsc * As_c - N_des) / (Rb * b);
                    if (x > xi_r * h0) x = xi_r * h0;
                    if (x < 0) x = 0;
                    m_ult = Rb * b * x * (h0 - 0.5 * x) + Rsc * As_c * arm;
                    double e = e0 - half;
                    demand = N_des * e;
                    caseStr = "Внецентр. растяж. большой e (§8.1.19б)";
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
            bool isSls, double phi1, double phi2, double acrcLimMm)
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
                        As_n_top, As_n_bot, ds_top, concreteChars, rebarChars, phi1, phi2, acrcLimMm)
                    : ComputeStripSls(Math.Abs(M_n), N_n, h, h - cb, ct,
                        As_n_bot, As_n_top, ds_bot, concreteChars, rebarChars, phi1, phi2, acrcLimMm);
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

using System;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Арматурный слой плитного сечения (на 1 м ширины).
   /// Asx/Asy — м²/м; zsx/zsy — м от срединной плоскости.
   /// </summary>
   public class PlateRebarLayer
   {
      /// <summary>Название слоя.</summary>
      public string Name { get; set; } = "";

      // ── Площади ────────────────────────────────────────────────────────────
      /// <summary>Площадь арматуры вдоль x, м²/м.</summary>
      public double Asx { get; set; }
      /// <summary>Площадь арматуры вдоль y, м²/м.</summary>
      public double Asy { get; set; }

      // ── z-координаты центров ────────────────────────────────────────────────
      /// <summary>z-координата арматуры x от срединной плоскости, м. Отрицательное — к «нижней» грани.</summary>
      public double Zsx { get; set; }
      /// <summary>z-координата арматуры y от срединной плоскости, м.</summary>
      public double Zsy { get; set; }

      // ── Способ задания ─────────────────────────────────────────────────────
      /// <summary>Способ задания: "direct" | "diameter_count" | "diameter_spacing".</summary>
      public string InputMode { get; set; } = "diameter_spacing";

      public double DiameterX       { get; set; }
      public double DiameterY       { get; set; }
      public double CountPerMeterX  { get; set; }
      public double CountPerMeterY  { get; set; }
      public double SpacingX        { get; set; }
      public double SpacingY        { get; set; }

      // ── Материал (опционально, иначе используется глобальный) ──────────────
      /// <summary>Id материала арматуры слоя. 0 = использовать глобальный RebarMaterialId.</summary>
      public int MaterialId { get; set; }

      /// <summary>Обновить Asx/Asy из режима diameter_count / diameter_spacing.</summary>
      public void RecalcArea()
      {
         if (InputMode == "diameter_count")
         {
            double rx = DiameterX / 2.0;
            double ry = DiameterY / 2.0;
            Asx = Math.PI * rx * rx * CountPerMeterX;
            Asy = Math.PI * ry * ry * CountPerMeterY;
         }
         else if (InputMode == "diameter_spacing")
         {
            Asx = SpacingX > 0 ? Math.PI * DiameterX * DiameterX / (4.0 * SpacingX) : 0.0;
            Asy = SpacingY > 0 ? Math.PI * DiameterY * DiameterY / (4.0 * SpacingY) : 0.0;
         }
         // "direct" — Asx/Asy задаются напрямую
      }
   }

   /// <summary>
   /// Плитное (оболочечное) сечение — доменная модель и расчётный объект.
   /// Модель: Кирхгоф–Лявь, послойное интегрирование бетона + суммирование арматуры.
   /// </summary>
   public class PlateSection
   {
      // ── Идентификация ──────────────────────────────────────────────────────
      public int    Id  { get; set; }
      public int    Num { get; set; }
      /// <summary>Название/обозначение сечения.</summary>
      public string Tag { get; set; } = "";

      // ── Геометрия ──────────────────────────────────────────────────────────
      /// <summary>Толщина плиты/стены, м.</summary>
      public double H { get; set; } = 0.2;
      /// <summary>Число бетонных слоёв для послойного интегрирования (рек. ≥ 10).</summary>
      public int NLayers { get; set; } = 10;

      // ── Материалы (Id в БД) ────────────────────────────────────────────────
      /// <summary>Id материала бетона.</summary>
      public int ConcreteMaterialId { get; set; }
      /// <summary>Id глобального материала арматуры (используется для слоёв без собственного материала).</summary>
      public int RebarMaterialId { get; set; }

      // ── Арматурные слои ────────────────────────────────────────────────────
      /// <summary>Арматурные слои. Сериализуются как JSON-столбец в БД.</summary>
      public List<PlateRebarLayer> RebarLayers { get; set; } = [];

      // ── Модель бетона ──────────────────────────────────────────────────────
      /// <summary>Учёт растяжения бетона.</summary>
      public bool TensionConcrete { get; set; }
      /// <summary>Модель снижения прочности β: "" | "vecchio_collins".</summary>
      public string SofteningModel { get; set; } = "";
      /// <summary>Параметр εc2 для модели Vecchio–Collins.</summary>
      public double SofteningEpsC2 { get; set; } = 0.002;

      // ── Расчёт ─────────────────────────────────────────────────────────────

      /// <summary>
      /// Вычислить результирующие усилия для заданного деформационного состояния.
      /// </summary>
      /// <param name="state">Деформационное состояние [ε₀x, ε₀y, γ₀xy, κx, κy, κxy].</param>
      /// <param name="concreteDiagram">Диаграмма бетона (соответствующего CalcType).</param>
      /// <param name="rebarDiagram">Диаграмма арматуры (глобальная).</param>
      /// <param name="layerDiagrams">Диаграммы арматуры по слоям; null-значение = использовать rebarDiagram.</param>
      /// <param name="computeStiffness">Вычислять касательные жёсткости (4 доп. вызова).</param>
      /// <returns>Результирующие усилия на 1 м ширины.</returns>
      public ShellResult Compute(
         ShellStrainState state,
         Diagramm concreteDiagram,
         Diagramm rebarDiagram,
         IReadOnlyList<Diagramm?>? layerDiagrams = null,
         bool computeStiffness = true)
      {
         var (nx, ny, nxy, mx, my, mxy,
              nxc, nyc, nxyc, mxc, myc, mxyc,
              nxr, nyr, mxr, myr) = Integrate(state, concreteDiagram, rebarDiagram, layerDiagrams);

         double zc = 0.0, eax = 0.0, eay = 0.0, eix = 0.0, eiy = 0.0;

         if (computeStiffness)
         {
            const double hd = 1e-7;

            var s1 = new ShellStrainState(state.Eps0x + hd, state.Eps0y, state.Gamma0xy, state.Kx, state.Ky, state.Kxy);
            var (nx1, _, _, mx1, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s1, concreteDiagram, rebarDiagram, layerDiagrams);
            double dNx = nx1 - nx;
            eax = dNx / hd;
            zc  = Math.Abs(dNx) > 0.0 ? (mx1 - mx) / dNx : GeomCentroid();

            var s2 = new ShellStrainState(state.Eps0x, state.Eps0y + hd, state.Gamma0xy, state.Kx, state.Ky, state.Kxy);
            var (_, ny2, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s2, concreteDiagram, rebarDiagram, layerDiagrams);
            eay = (ny2 - ny) / hd;

            var s3 = new ShellStrainState(state.Eps0x, state.Eps0y, state.Gamma0xy, state.Kx + hd, state.Ky, state.Kxy);
            var (_, _, _, mx3, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s3, concreteDiagram, rebarDiagram, layerDiagrams);
            eix = (mx3 - mx) / hd;

            var s4 = new ShellStrainState(state.Eps0x, state.Eps0y, state.Gamma0xy, state.Kx, state.Ky + hd, state.Kxy);
            var (_, _, _, _, my4, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s4, concreteDiagram, rebarDiagram, layerDiagrams);
            eiy = (my4 - my) / hd;
         }
         else
         {
            zc = GeomCentroid();
         }

         return new ShellResult
         {
            Nx = nx, Ny = ny, Nxy = nxy, Mx = mx, My = my, Mxy = mxy,
            NxConcrete = nxc, NyConcrete = nyc, NxyConcrete = nxyc,
            MxConcrete = mxc, MyConcrete = myc, MxyConcrete = mxyc,
            NxRebar = nxr, NyRebar = nyr, MxRebar = mxr, MyRebar = myr,
            Zc = zc, EAx = eax, EAy = eay, EIx = eix, EIy = eiy,
         };
      }

      /// <summary>
      /// Усилия и полные касательные блоки A/B/D (6×6 по мембране+изгибу) + As.
      /// Forward FD по [ε₀x, ε₀y, γ₀xy, κx, κy, κxy]; 7 вызовов интегратора.
      /// </summary>
      public PlateShellTangentResult ComputeTangent(
         ShellStrainState state,
         Diagramm concreteDiagram,
         Diagramm rebarDiagram,
         IReadOnlyList<Diagramm?>? layerDiagrams = null,
         double concreteE_MPa = 30000.0,
         double nu = 0.2,
         double kShear = 5.0 / 6.0,
         double[,]? asOverride = null,
         double fdStep = 1e-7)
      {
         var (nx, ny, nxy, mx, my, mxy, _, _, _, _, _, _, _, _, _, _) =
            Integrate(state, concreteDiagram, rebarDiagram, layerDiagrams);

         double[] state6 =
         [
            state.Eps0x, state.Eps0y, state.Gamma0xy,
            state.Kx, state.Ky, state.Kxy
         ];
         var f0 = new[] { nx, ny, nxy, mx, my, mxy };
         double h = fdStep * (Norm6(state6) + 1.0);

         var j = new double[6, 6];
         for (int col = 0; col < 6; col++)
         {
            var arr = (double[])state6.Clone();
            arr[col] += h;
            var sPert = ShellStrainState.FromArray(arr);
            var (nx1, ny1, nxy1, mx1, my1, mxy1, _, _, _, _, _, _, _, _, _, _) =
               Integrate(sPert, concreteDiagram, rebarDiagram, layerDiagrams);
            var f1 = new[] { nx1, ny1, nxy1, mx1, my1, mxy1 };
            for (int row = 0; row < 6; row++)
               j[row, col] = (f1[row] - f0[row]) / h;
         }

         var a = Submatrix(j, 0, 0);
         var b = Submatrix(j, 0, 3);
         var d = Submatrix(j, 3, 3);
         var asMat = asOverride ?? BuildAs(concreteE_MPa, nu, kShear);

         return new PlateShellTangentResult
         {
            Nx = nx, Ny = ny, Nxy = nxy, Mx = mx, My = my, Mxy = mxy,
            A = a, B = b, D = d, As = asMat,
         };
      }

      static double Norm6(double[] v)
      {
         double s = 0;
         foreach (double x in v) s += x * x;
         return Math.Sqrt(s);
      }

      static double[,] Submatrix(double[,] m, int row0, int col0)
      {
         var r = new double[3, 3];
         for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
               r[i, j] = m[row0 + i, col0 + j];
         return r;
      }

      /// <summary>Линейная As: k_shear·G·h·1000 (кН/м при γ=1), G в МПа.</summary>
      public double[,] BuildAs(double concreteE_MPa, double nu = 0.2, double kShear = 5.0 / 6.0)
      {
         double g = concreteE_MPa / (2.0 * (1.0 + nu));
         double v = kShear * g * H * 1000.0;
         return new[,] { { v, 0.0 }, { 0.0, v } };
      }

      // ── Внутреннее: послойное интегрирование ───────────────────────────────

      // Возвращает 16 значений: суммарные + детализация бетон/арматура
      private (double nx, double ny, double nxy, double mx, double my, double mxy,
               double nxc, double nyc, double nxyc, double mxc, double myc, double mxyc,
               double nxr, double nyr, double mxr, double myr)
         Integrate(ShellStrainState s, Diagramm cDiag, Diagramm rDiag, IReadOnlyList<Diagramm?>? layerDiags)
      {
         double h  = H;
         int    nl = NLayers < 1 ? 1 : NLayers;
         double dz = h / nl;
         double z0 = -h / 2.0 + dz / 2.0;  // центр первого слоя

         double nxc = 0, nyc = 0, nxyc = 0, mxc = 0, myc = 0, mxyc = 0;

         for (int i = 0; i < nl; i++)
         {
            double zi  = z0 + i * dz;
            double ex  = s.EpsX(zi);
            double ey  = s.EpsY(zi);
            double gxy = s.GammaXY(zi);

            PrincipalStrains2D(ex, ey, gxy, out double eps1, out double eps2, out double theta);

            double beta = SofteningModel == "vecchio_collins"
               ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;

            double sig1 = ConcreteStress(cDiag, eps1, beta);
            double sig2 = ConcreteStress(cDiag, eps2, beta);

            RotateStressesToXY(sig1, sig2, theta, out double sigx, out double sigy, out double txy);

            // σ [МПа] · dz [м] · 1000 → кН/м; · zi → кН·м/м
            double kf = dz * 1000.0;
            nxc  += sigx * kf;
            nyc  += sigy * kf;
            nxyc += txy  * kf;
            mxc  += sigx * kf * zi;
            myc  += sigy * kf * zi;
            mxyc += txy  * kf * zi;
         }

         double nxr = 0, nyr = 0, mxr = 0, myr = 0;

         for (int li = 0; li < RebarLayers.Count; li++)
         {
            var rl = RebarLayers[li];
            var rd = layerDiags != null && li < layerDiags.Count && layerDiags[li] != null
                     ? layerDiags[li]! : rDiag;
            if (rd == null) continue;

            if (rl.Asx > 0.0)
            {
               double esx = s.EpsX(rl.Zsx);
               double ssx = RebarStress(rd, esx);
               // σ [МПа] · A [м²/м] · 1000 → кН/м; · z → кН·м/м
               nxr += ssx * rl.Asx * 1000.0;
               mxr += ssx * rl.Asx * rl.Zsx * 1000.0;
            }

            if (rl.Asy > 0.0)
            {
               double esy = s.EpsY(rl.Zsy);
               double ssy = RebarStress(rd, esy);
               nyr += ssy * rl.Asy * 1000.0;
               myr += ssy * rl.Asy * rl.Zsy * 1000.0;
            }
         }

         return (nxc + nxr, nyc + nyr, nxyc, mxc + mxr, myc + myr, mxyc,
                 nxc, nyc, nxyc, mxc, myc, mxyc,
                 nxr, nyr, mxr, myr);
      }

      double GeomCentroid()
      {
         int nl = NLayers < 1 ? 1 : NLayers;
         double dz = H / nl;
         double z0 = -H / 2.0 + dz / 2.0;
         double sumZ = 0, sumH = 0;
         for (int i = 0; i < nl; i++) { double zi = z0 + i * dz; sumZ += zi * dz; sumH += dz; }
         return sumH > 0 ? sumZ / sumH : 0.0;
      }

      // ── Напряжения материалов ──────────────────────────────────────────────

      double ConcreteStress(Diagramm d, double eps, double beta)
      {
         if (eps > 0.0)
            return TensionConcrete ? d.Sig(eps, out _, tenB: true) : 0.0;
         return beta * d.Sig(eps, out _, tenB: false);
      }

      static double RebarStress(Diagramm d, double eps)
         => d.Sig(eps, out _);

      // ── Преобразование деформаций/напряжений (Мор) ────────────────────────

      static void PrincipalStrains2D(double ex, double ey, double gxy,
         out double eps1, out double eps2, out double theta)
      {
         double avg  = 0.5 * (ex + ey);
         double diff = 0.5 * (ex - ey);
         double R    = Math.Sqrt(diff * diff + (0.5 * gxy) * (0.5 * gxy));
         eps1  = avg + R;
         eps2  = avg - R;
         theta = 0.5 * Math.Atan2(gxy, ex - ey);
      }

      static void RotateStressesToXY(double sig1, double sig2, double theta,
         out double sigx, out double sigy, out double txy)
      {
         double c  = Math.Cos(theta);
         double s  = Math.Sin(theta);
         double c2 = c * c, s2 = s * s, sc = s * c;
         sigx = sig1 * c2 + sig2 * s2;
         sigy = sig1 * s2 + sig2 * c2;
         txy  = (sig1 - sig2) * sc;
      }

      // ── Vecchio–Collins β-фактор ───────────────────────────────────────────

      /// <summary>
      /// β-фактор снижения прочности бетона на сжатие при поперечном растяжении
      /// (Vecchio &amp; Collins, 1986).
      /// β = 1 / (0.8 + 170·ε₁) ≤ 1, ε₁ — максимальная (растягивающая) главная деформация.
      /// </summary>
      static double VecchioCollinsBeta(double eps1, double epsC2)
      {
         if (eps1 <= 0.0) return 1.0;
         double beta = 1.0 / (0.8 + 170.0 * eps1);
         return beta < 0.0 ? 0.0 : beta > 1.0 ? 1.0 : beta;
      }
   }
}

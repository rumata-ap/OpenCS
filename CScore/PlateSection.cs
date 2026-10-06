using System;
using System.Collections.Generic;
using CScore.PlateRebar;

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

      // ── Пространственное армирование (PlateRebarField) ──────────────────────
      /// <summary>Техническая грань оболочки, к которой относится слой в контексте
      /// PlateRebarField (не используется однородным расчётом PlateSection.Compute).</summary>
      public RebarFace Face { get; set; } = RebarFace.PlusN;
      /// <summary>Угол направлений X/Y слоя относительно локальной оси u поверхности, град.</summary>
      public double Angle { get; set; }

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

      /// <summary>Глубокая копия слоя (для потокобезопасного клонирования сечения).</summary>
      public PlateRebarLayer Clone() => new()
      {
         Name = Name, Asx = Asx, Asy = Asy, Zsx = Zsx, Zsy = Zsy,
         InputMode = InputMode, DiameterX = DiameterX, DiameterY = DiameterY,
         CountPerMeterX = CountPerMeterX, CountPerMeterY = CountPerMeterY,
         SpacingX = SpacingX, SpacingY = SpacingY, MaterialId = MaterialId,
         Face = Face, Angle = Angle,
      };
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
      /// <summary>Id PlanarRegion, для которого это сечение сгенерировано автоматически по
      /// раскладке зон армирования (срез 4). Null — обычная, не сгенерированная запись.</summary>
      public int? GeneratedForRegionId { get; set; }

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
      /// <summary>Параметр εc2 для модели Vecchio–Collins — деформация бетона на пике
      /// диаграммы (по СП 63 εb0 = 0,002), положительная величина.</summary>
      public double SofteningEpsC2 { get; set; } = 0.002;

      /// <summary>
      /// Нелинейная модель интегрирования по толщине:
      /// "layered" — слоистая (главные ε₁/ε₂ + softening, разбиение на NLayers);
      /// "char1d_principal" — 1D по характерным точкам, по главным (+softening);
      /// "char1d_axial" — 1D по характерным точкам, по осям (σx←εx, σy←εy, без softening/σxy).
      /// Пустое/неизвестное значение трактуется как "layered".
      /// </summary>
      public string PlateModel { get; set; } = "layered";

      /// <summary>Тип диаграммы бетона при расчёте по толщине.</summary>
      public DiagrammType ConcreteDiagramType { get; set; } = DiagrammType.L3;

      /// <summary>
      /// Коэффициент Пуассона бетона ДО трещины в слоистой модели ("layered"); после трещины
      /// в слое ν = 0. 0 — прежний закон слоя (одноосные диаграммы по главным направлениям).
      /// При ν &gt; 0 — подход Дарвина — Пекнольда: эквивалентные одноосные деформации
      /// ε_i,eq = (ε_i + ν·ε_j)/(1 − ν²), секущие E_i = σ(ε_i,eq)/ε_i,eq и
      /// σ = Q·ε, Q = 1/(1 − ν²)·[[E₁, ν√(E₁E₂)], [ν√(E₁E₂), E₂]].
      /// Параметр расчёта: в БД не хранится.
      /// </summary>
      public double PoissonUncracked { get; set; }

      /// <summary>
      /// Глубокий клон сечения для потокобезопасности пакетных задач
      /// (симметрично <see cref="CrossSection.CloneForCalc"/>). Диаграммы не входят —
      /// строятся отдельно и используются только на чтение.
      /// </summary>
      public PlateSection CloneForCalc() => new()
      {
         Id = Id, Num = Num, Tag = Tag, H = H, NLayers = NLayers,
         ConcreteMaterialId = ConcreteMaterialId, RebarMaterialId = RebarMaterialId,
         TensionConcrete = TensionConcrete, SofteningModel = SofteningModel,
         SofteningEpsC2 = SofteningEpsC2, PlateModel = PlateModel,
         ConcreteDiagramType = ConcreteDiagramType, PoissonUncracked = PoissonUncracked,
         RebarLayers = RebarLayers.Select(l => l.Clone()).ToList(),
      };

      // ── Расчёт ─────────────────────────────────────────────────────────────

      /// <summary>
      /// Вычислить результирующие усилия для заданного деформационного состояния.
      /// </summary>
      /// <param name="state">Деформационное состояние [ε₀x, ε₀y, γ₀xy, κx, κy, κxy].</param>
      /// <param name="concreteDiagram">Диаграмма бетона (соответствующего CalcType).</param>
      /// <param name="rebarDiagram">Диаграмма арматуры (глобальная).</param>
      /// <param name="layerDiagrams">Диаграммы арматуры по слоям; null-значение = использовать rebarDiagram.</param>
      /// <param name="computeStiffness">Вычислять касательные жёсткости (4 доп. вызова).</param>
      /// <param name="layerState">Память трещин и εs,crc (только "layered"); null — без памяти,
      /// прежнее поведение.</param>
      /// <returns>Результирующие усилия на 1 м ширины.</returns>
      public ShellResult Compute(
         ShellStrainState state,
         Diagramm concreteDiagram,
         Diagramm rebarDiagram,
         IReadOnlyList<Diagramm?>? layerDiagrams = null,
         bool computeStiffness = true,
         bool? tensionOverride = null,
         PlateLayerState? layerState = null)
      {
         var (nx, ny, nxy, mx, my, mxy,
              nxc, nyc, nxyc, mxc, myc, mxyc,
              nxr, nyr, mxr, myr) = Integrate(state, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);

         double zc = 0.0, eax = 0.0, eay = 0.0, eix = 0.0, eiy = 0.0;

         if (computeStiffness)
         {
            const double hd = 1e-7;

            var s1 = new ShellStrainState(state.Eps0x + hd, state.Eps0y, state.Gamma0xy, state.Kx, state.Ky, state.Kxy);
            var (nx1, _, _, mx1, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s1, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);
            double dNx = nx1 - nx;
            eax = dNx / hd;
            zc  = Math.Abs(dNx) > 0.0 ? (mx1 - mx) / dNx : GeomCentroid();

            var s2 = new ShellStrainState(state.Eps0x, state.Eps0y + hd, state.Gamma0xy, state.Kx, state.Ky, state.Kxy);
            var (_, ny2, _, _, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s2, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);
            eay = (ny2 - ny) / hd;

            var s3 = new ShellStrainState(state.Eps0x, state.Eps0y, state.Gamma0xy, state.Kx + hd, state.Ky, state.Kxy);
            var (_, _, _, mx3, _, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s3, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);
            eix = (mx3 - mx) / hd;

            var s4 = new ShellStrainState(state.Eps0x, state.Eps0y, state.Gamma0xy, state.Kx, state.Ky + hd, state.Kxy);
            var (_, _, _, _, my4, _, _, _, _, _, _, _, _, _, _, _) = Integrate(s4, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);
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
         double fdStep = 1e-7,
         bool? tensionOverride = null,
         PlateLayerState? layerState = null)
      {
         var (nx, ny, nxy, mx, my, mxy, _, _, _, _, _, _, _, _, _, _) =
            Integrate(state, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);

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
               Integrate(sPert, concreteDiagram, rebarDiagram, layerDiagrams, tensionOverride, layerState);
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

      /// <summary>
      /// Выборка эпюр ε(z) и σ(z) по толщине для визуализации. Не участвует в
      /// интегрировании. Напряжения бетона согласованы с <see cref="PlateModel"/>:
      /// "char1d_axial" — по осям (σxy=0); иначе по главным + softening.
      /// </summary>
      public PlateThroughThickness SampleThroughThickness(
         ShellStrainState state, Diagramm cDiag, Diagramm rDiag,
         IReadOnlyList<Diagramm?>? layerDiags, int nPoints, bool? tensionOverride = null)
      {
         int n = nPoints < 2 ? 2 : nPoints;
         var r = new PlateThroughThickness
         {
            Z = new double[n], EpsX = new double[n], EpsY = new double[n],
            GammaXY = new double[n], SigX = new double[n], SigY = new double[n],
            TauXY = new double[n],
         };
         double zlo = -H / 2.0, dz = H / (n - 1);
         bool axial = PlateModel == "char1d_axial";

         for (int i = 0; i < n; i++)
         {
            double z = zlo + i * dz;
            double ex = state.EpsX(z), ey = state.EpsY(z), gxy = state.GammaXY(z);
            r.Z[i] = z; r.EpsX[i] = ex; r.EpsY[i] = ey; r.GammaXY[i] = gxy;

            if (axial)
            {
               r.SigX[i] = ConcreteStress(cDiag, ex, 1.0, tensionOverride);
               r.SigY[i] = ConcreteStress(cDiag, ey, 1.0, tensionOverride);
               r.TauXY[i] = 0.0;
            }
            else
            {
               PrincipalStrains2D(ex, ey, gxy, out double eps1, out double eps2, out double theta);
               double beta = SofteningModel == "vecchio_collins"
                  ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;
               double sig1 = ConcreteStress(cDiag, eps1, beta, tensionOverride);
               double sig2 = ConcreteStress(cDiag, eps2, beta, tensionOverride);
               RotateStressesToXY(sig1, sig2, theta, out double sx, out double sy, out double txy);
               r.SigX[i] = sx; r.SigY[i] = sy; r.TauXY[i] = txy;
            }
         }

         for (int li = 0; li < RebarLayers.Count; li++)
         {
            var rl = RebarLayers[li];
            var rd = layerDiags != null && li < layerDiags.Count && layerDiags[li] != null
                     ? layerDiags[li]! : rDiag;
            if (rd == null) continue;
            if (rl.Asx > 0.0)
               r.Rebar.Add(new RebarStressPoint(rl.Zsx, RebarStress(rd, state.EpsX(rl.Zsx)), true));
            if (rl.Asy > 0.0)
               r.Rebar.Add(new RebarStressPoint(rl.Zsy, RebarStress(rd, state.EpsY(rl.Zsy)), false));
         }
         return r;
      }

      /// <summary>
      /// Профили главных деформаций, напряжений, β и угла θ по толщине пластины
      /// (для вкладки «Главные оси»). Напряжения — в кПа.
      /// </summary>
      public PlatePrincipalAxes SamplePrincipalAxes(
         ShellStrainState state, Diagramm cDiag, int nPoints, bool? tensionOverride = null)
      {
         int n = nPoints < 2 ? 2 : nPoints;
         var r = new PlatePrincipalAxes
         {
            Z = new double[n], Eps1 = new double[n], Eps2 = new double[n],
            Sig1 = new double[n], Sig2 = new double[n],
            Beta = new double[n], ThetaDeg = new double[n],
         };
         double zlo = -H / 2.0, dz = H / (n - 1);
         bool axial = PlateModel == "char1d_axial";

         for (int i = 0; i < n; i++)
         {
            double z = zlo + i * dz;
            r.Z[i] = z;
            double ex = state.EpsX(z), ey = state.EpsY(z), gxy = state.GammaXY(z);
            if (axial)
            {
               r.Eps1[i] = Math.Max(ex, ey);
               r.Eps2[i] = Math.Min(ex, ey);
               r.Sig1[i] = ConcreteStress(cDiag, r.Eps1[i], 1.0, tensionOverride);
               r.Sig2[i] = ConcreteStress(cDiag, r.Eps2[i], 1.0, tensionOverride);
               r.Beta[i]     = 1.0;
               r.ThetaDeg[i] = ex >= ey ? 0.0 : 90.0;
            }
            else
            {
               PrincipalStrains2D(ex, ey, gxy, out double eps1, out double eps2, out double theta);
               double beta = SofteningModel == "vecchio_collins"
                  ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;
               r.Eps1[i]     = eps1;
               r.Eps2[i]     = eps2;
               r.Sig1[i]     = ConcreteStress(cDiag, eps1, beta, tensionOverride);
               r.Sig2[i]     = ConcreteStress(cDiag, eps2, beta, tensionOverride);
               r.Beta[i]     = beta;
               r.ThetaDeg[i] = theta * 180.0 / Math.PI;
            }
         }
         return r;
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

      // ── Узлы/веса квадратуры Гаусса–Лежандра 5-го порядка на [-1,1] ─────────
      static readonly double[] Gl5Pts =
         { -0.9061798459386640, -0.5384693101056831, 0.0, 0.5384693101056831, 0.9061798459386640 };
      static readonly double[] Gl5Wts =
         {  0.2369268850561891,  0.4786286704993665, 0.5688888888888889, 0.4786286704993665, 0.2369268850561891 };

      // ── Внутреннее: диспетчер интегрирования по модели ─────────────────────

      // Возвращает 16 значений: суммарные + детализация бетон/арматура
      private (double nx, double ny, double nxy, double mx, double my, double mxy,
               double nxc, double nyc, double nxyc, double mxc, double myc, double mxyc,
               double nxr, double nyr, double mxr, double myr)
         Integrate(ShellStrainState s, Diagramm cDiag, Diagramm rDiag, IReadOnlyList<Diagramm?>? layerDiags,
            bool? tensionOverride = null, PlateLayerState? layerState = null)
      {
         if (!IsLayered && (layerState != null || PoissonUncracked != 0.0))
            throw new InvalidOperationException(
               $"ν до трещины и память трещин заданы только для слоистой модели, а не \"{PlateModel}\".");
         CheckLayerState(layerState);

         var (nxc, nyc, nxyc, mxc, myc, mxyc) = PlateModel switch
         {
            "char1d_axial"     => IntegrateConcreteChar1dAxial(s, cDiag, tensionOverride),
            "char1d_principal" => IntegrateConcreteChar1dPrincipal(s, cDiag, tensionOverride),
            _                  => IntegrateConcreteLayered(s, cDiag, tensionOverride, layerState),
         };

         var (nxr, nyr, mxr, myr) = IntegrateRebar(s, rDiag, layerDiags, layerState);

         return (nxc + nxr, nyc + nyr, nxyc, mxc + mxr, myc + myr, mxyc,
                 nxc, nyc, nxyc, mxc, myc, mxyc,
                 nxr, nyr, mxr, myr);
      }

      // ── Бетон: слоистая модель (разбиение на NLayers, главные + softening) ──
      private (double nxc, double nyc, double nxyc, double mxc, double myc, double mxyc)
         IntegrateConcreteLayered(ShellStrainState s, Diagramm cDiag, bool? tensionOverride,
            PlateLayerState? layerState)
      {
         int    nl = NLayers < 1 ? 1 : NLayers;
         double dz = H / nl;
         // Начальный модуль нужен только закону с ν (секущая при ε_eq = 0).
         double e0 = PoissonUncracked != 0.0 ? InitialConcreteModulus(cDiag) : 0.0;

         double nxc = 0, nyc = 0, nxyc = 0, mxc = 0, myc = 0, mxyc = 0;

         for (int i = 0; i < nl; i++)
         {
            var p = EvaluateConcreteLayerCore(i, s, cDiag, tensionOverride, layerState, e0);
            double zi = p.Z;
            RotateStressesToXY(p.Sig1, p.Sig2, p.Theta, out double sigx, out double sigy, out double txy);

            // σ [кПа = кН/м²] · dz [м] → кН/м; · zi → кН·м/м
            double kf = dz;
            nxc  += sigx * kf;
            nyc  += sigy * kf;
            nxyc += txy  * kf;
            mxc  += sigx * kf * zi;
            myc  += sigy * kf * zi;
            mxyc += txy  * kf * zi;
         }

         return (nxc, nyc, nxyc, mxc, myc, mxyc);
      }

      // ── Бетон: 1D по характерным точкам, по осям ───────────────────────────
      private (double nxc, double nyc, double nxyc, double mxc, double myc, double mxyc)
         IntegrateConcreteChar1dAxial(ShellStrainState s, Diagramm cDiag, bool? tensionOverride)
      {
         double h = H, zlo = -h / 2.0, zhi = h / 2.0;
         double[] crit = cDiag.GetCriticalStrains();

         var zs = new System.Collections.Generic.SortedSet<double> { zlo, zhi };
         AddAxisBreaks(zs, s.Eps0x, s.Kx, crit, zlo, zhi);
         AddAxisBreaks(zs, s.Eps0y, s.Ky, crit, zlo, zhi);
         var nodes = new System.Collections.Generic.List<double>(zs);

         double nxc = 0, nyc = 0, mxc = 0, myc = 0;
         for (int seg = 0; seg < nodes.Count - 1; seg++)
         {
            double a = nodes[seg], b = nodes[seg + 1];
            double half = 0.5 * (b - a);
            if (half <= 1e-15) continue;
            double mid = 0.5 * (a + b);
            for (int g = 0; g < Gl5Pts.Length; g++)
            {
               double z  = mid + half * Gl5Pts[g];
               double w  = Gl5Wts[g] * half;
               double sigx = ConcreteStress(cDiag, s.EpsX(z), 1.0, tensionOverride);
               double sigy = ConcreteStress(cDiag, s.EpsY(z), 1.0, tensionOverride);
               double kf = w;          // σ[кПа=кН/м²]·dz[м] → кН/м
               nxc += sigx * kf;  mxc += sigx * kf * z;
               nyc += sigy * kf;  myc += sigy * kf * z;
            }
         }
         return (nxc, nyc, 0.0, mxc, myc, 0.0);
      }

      // z-точки, где εx(z)=ε0+k·z пересекает характерную деформацию: z=(c−ε0)/k.
      private static void AddAxisBreaks(System.Collections.Generic.SortedSet<double> zs,
         double eps0, double k, double[] crit, double zlo, double zhi)
      {
         if (Math.Abs(k) < 1e-12) return;       // равномерно по толщине — нет изломов
         foreach (double c in crit)
         {
            double z = (c - eps0) / k;
            if (z > zlo + 1e-12 && z < zhi - 1e-12) zs.Add(z);
         }
      }

      // ── Бетон: 1D по характерным точкам, по главным (+softening) ───────────
      private (double nxc, double nyc, double nxyc, double mxc, double myc, double mxyc)
         IntegrateConcreteChar1dPrincipal(ShellStrainState s, Diagramm cDiag, bool? tensionOverride)
      {
         double h = H, zlo = -h / 2.0, zhi = h / 2.0;
         double[] crit = cDiag.GetCriticalStrains();

         var zs = new System.Collections.Generic.SortedSet<double> { zlo, zhi };
         AddPrincipalBreaks(zs, s, crit, zlo, zhi);
         var nodes = new System.Collections.Generic.List<double>(zs);

         double nxc = 0, nyc = 0, nxyc = 0, mxc = 0, myc = 0, mxyc = 0;
         for (int seg = 0; seg < nodes.Count - 1; seg++)
         {
            double a = nodes[seg], b = nodes[seg + 1];
            double half = 0.5 * (b - a);
            if (half <= 1e-15) continue;
            double mid = 0.5 * (a + b);
            for (int g = 0; g < Gl5Pts.Length; g++)
            {
               double z  = mid + half * Gl5Pts[g];
               double w  = Gl5Wts[g] * half;
               PrincipalStrains2D(s.EpsX(z), s.EpsY(z), s.GammaXY(z),
                  out double eps1, out double eps2, out double theta);
               double beta = SofteningModel == "vecchio_collins"
                  ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;
               double sig1 = ConcreteStress(cDiag, eps1, beta, tensionOverride);
               double sig2 = ConcreteStress(cDiag, eps2, beta, tensionOverride);
               RotateStressesToXY(sig1, sig2, theta, out double sigx, out double sigy, out double txy);
               double kf = w;
               nxc += sigx * kf;  mxc += sigx * kf * z;
               nyc += sigy * kf;  myc += sigy * kf * z;
               nxyc += txy * kf;  mxyc += txy * kf * z;
            }
         }
         return (nxc, nyc, nxyc, mxc, myc, mxyc);
      }

      // z-точки, где ε₁(z) или ε₂(z) пересекает характерную деформацию.
      // ε_{1,2}(z)=A(z)±√B(z); A линейна, B квадратична. Уравнение сводится к
      // B(z)=(c−A(z))² — квадратному относительно z (корни в замкнутой форме).
      private static void AddPrincipalBreaks(System.Collections.Generic.SortedSet<double> zs,
         ShellStrainState s, double[] crit, double zlo, double zhi)
      {
         // A(z)=a0+a1 z ; p(z)=0.5(εx−εy)=p0+p1 z ; q(z)=0.5 γxy=q0+q1 z ; B=p²+q²
         double a0 = 0.5 * (s.Eps0x + s.Eps0y), a1 = 0.5 * (s.Kx + s.Ky);
         double p0 = 0.5 * (s.Eps0x - s.Eps0y), p1 = 0.5 * (s.Kx - s.Ky);
         double q0 = 0.5 * s.Gamma0xy,           q1 = 0.5 * s.Kxy;

         var roots = new System.Collections.Generic.List<double>();
         foreach (double c in crit)
         {
            // B(z) − (c−A(z))² = 0
            double A = p1 * p1 + q1 * q1 - a1 * a1;
            double B = 2.0 * (p0 * p1 + q0 * q1 + a1 * (c - a0));
            double C = p0 * p0 + q0 * q0 - (c - a0) * (c - a0);
            SolveQuadratic(A, B, C, roots);
         }
         foreach (double z in roots)
            if (z > zlo + 1e-12 && z < zhi - 1e-12) zs.Add(z);
      }

      // Вещественные корни A z² + B z + C = 0 (линейный случай при A≈0).
      private static void SolveQuadratic(double A, double B, double C,
         System.Collections.Generic.List<double> roots)
      {
         if (Math.Abs(A) < 1e-18)
         {
            if (Math.Abs(B) > 1e-18) roots.Add(-C / B);
            return;
         }
         double disc = B * B - 4.0 * A * C;
         if (disc < 0.0) return;
         double sq = Math.Sqrt(disc);
         roots.Add((-B + sq) / (2.0 * A));
         roots.Add((-B - sq) / (2.0 * A));
      }

      // ── Арматура: точечные вклады в Zsx/Zsy (общая для всех моделей) ───────
      private (double nxr, double nyr, double mxr, double myr)
         IntegrateRebar(ShellStrainState s, Diagramm rDiag, IReadOnlyList<Diagramm?>? layerDiags,
            PlateLayerState? layerState)
      {
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
               double ssx = RebarStressPsi(rd, esx, layerState, li, true);
               // σ [кПа = кН/м²] · A [м²/м] → кН/м; · z → кН·м/м
               nxr += ssx * rl.Asx;
               mxr += ssx * rl.Asx * rl.Zsx;
            }

            if (rl.Asy > 0.0)
            {
               double esy = s.EpsY(rl.Zsy);
               double ssy = RebarStressPsi(rd, esy, layerState, li, false);
               nyr += ssy * rl.Asy;
               myr += ssy * rl.Asy * rl.Zsy;
            }
         }

         return (nxr, nyr, mxr, myr);
      }

      // ── Закон слоя и память трещин (слоистая модель) ─────────────────────

      bool IsLayered => PlateModel is not ("char1d_axial" or "char1d_principal");

      void CheckLayerState(PlateLayerState? st)
      {
         if (st == null) return;
         int nl = NLayers < 1 ? 1 : NLayers;
         if (st.ConcreteLayerCount != nl || st.RebarLayerCount != RebarLayers.Count)
            throw new ArgumentException(
               $"Состояние слоёв ({st.ConcreteLayerCount} бетон, {st.RebarLayerCount} арматура) не " +
               $"соответствует сечению ({nl} бетон, {RebarLayers.Count} арматура).", nameof(st));
      }

      /// <summary>Начальный модуль сжатой ветви бетона (как в <see cref="ComputeSecant"/>).</summary>
      static double InitialConcreteModulus(Diagramm cDiag)
      {
         const double deps = 1e-7;
         return -cDiag.Sig(-deps, out _, tenB: false) / deps;
      }

      static double SecantModulus(double sig, double eps, double e0) => eps != 0.0 ? sig / eps : e0;

      /// <summary>Предельная растягивающая деформация бетона ε_bt,ult — конец растянутой ветви
      /// диаграммы (как <see cref="Fem.ShellCrackingSolver.TensionLimit"/>).</summary>
      public static double ConcreteTensionLimit(Diagramm cDiag)
      {
         ArgumentNullException.ThrowIfNull(cDiag);
         if (cDiag.It?.X == null || cDiag.It.X.Length == 0)
            throw new InvalidOperationException(
               "У диаграммы бетона не построена растянутая ветвь: ε_bt,ult определить нечем.");
         return cDiag.It.X.Max();
      }

      /// <summary>Индекс слоя бетона, в который попадает координата z (арматура на уровне слоя).</summary>
      public int ConcreteLayerIndexAt(double z)
      {
         int nl = NLayers < 1 ? 1 : NLayers;
         int i = (int)Math.Floor((z + H / 2.0) / (H / nl));
         return i < 0 ? 0 : i >= nl ? nl - 1 : i;
      }

      /// <summary>
      /// Закон слоя бетона в точке (слоистая модель): главные деформации, секущие модули,
      /// напряжения — те же числа, что идут в интегрирование усилий <see cref="Compute"/>.
      /// Нужен секущей ABD: слой с матрицей Q из этой точки воспроизводит усилия точно.
      /// </summary>
      public PlateConcreteLayerPoint EvaluateConcreteLayer(int layer, ShellStrainState s, Diagramm cDiag,
         bool? tensionOverride = null, PlateLayerState? layerState = null)
      {
         if (!IsLayered)
            throw new InvalidOperationException($"Закон слоя определён только для слоистой модели, а не \"{PlateModel}\".");
         CheckLayerState(layerState);
         return EvaluateConcreteLayerCore(layer, s, cDiag, tensionOverride, layerState, InitialConcreteModulus(cDiag));
      }

      PlateConcreteLayerPoint EvaluateConcreteLayerCore(int i, ShellStrainState s, Diagramm cDiag,
         bool? tensionOverride, PlateLayerState? layerState, double e0)
      {
         int    nl = NLayers < 1 ? 1 : NLayers;
         double dz = H / nl;
         double z0 = -H / 2.0 + dz / 2.0;  // центр первого слоя
         double zi = z0 + i * dz;

         PrincipalStrains2D(s.EpsX(zi), s.EpsY(zi), s.GammaXY(zi),
            out double eps1, out double eps2, out double theta);

         double beta = SofteningModel == "vecchio_collins"
            ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;

         // Трещина необратима: растяжение слоя выключено, ν = 0.
         bool cracked = layerState != null && layerState.IsCracked(i);
         bool? tension = cracked ? false : tensionOverride;
         double nu = cracked ? 0.0 : PoissonUncracked;

         double sig1, sig2, e1, e2, e1Eq, e2Eq, q11, q12, q22;
         if (nu == 0.0)
         {
            // Прежний закон: одноосные диаграммы по главным направлениям.
            sig1 = ConcreteStress(cDiag, eps1, beta, tension);
            sig2 = ConcreteStress(cDiag, eps2, beta, tension);
            e1Eq = eps1; e2Eq = eps2;
            e1 = SecantModulus(sig1, eps1, e0);
            e2 = SecantModulus(sig2, eps2, e0);
            q11 = e1; q22 = e2; q12 = 0.0;
         }
         else
         {
            // Дарвин — Пекнольд: секущие по эквивалентным одноосным деформациям, σ = Q·ε.
            double k = 1.0 / (1.0 - nu * nu);
            e1Eq = (eps1 + nu * eps2) * k;
            e2Eq = (eps2 + nu * eps1) * k;
            e1 = SecantModulus(ConcreteStress(cDiag, e1Eq, beta, tension), e1Eq, e0);
            e2 = SecantModulus(ConcreteStress(cDiag, e2Eq, beta, tension), e2Eq, e0);
            q11 = k * e1;
            q22 = k * e2;
            q12 = k * nu * Math.Sqrt(Math.Max(0.0, e1 * e2));
            sig1 = q11 * eps1 + q12 * eps2;
            sig2 = q12 * eps1 + q22 * eps2;
         }

         // Сдвиговой секущий модуль из соосности вращающейся трещины; при ε₁ ≈ ε₂ — предел
         // изотропного закона (Q11 + Q22)/4 − Q12/2 (= E/(2(1 + ν)) при E₁ = E₂ = E).
         double dEps = eps1 - eps2;
         double g12 = dEps > 1e-14 ? (sig1 - sig2) / (2.0 * dEps) : 0.25 * (q11 + q22) - 0.5 * q12;
         if (g12 < 0.0) g12 = 0.0;

         return new PlateConcreteLayerPoint(i, zi, dz, eps1, eps2, theta, e1Eq, e2Eq, nu,
            e1, e2, q11, q12, q22, g12, sig1, sig2, cracked);
      }

      /// <summary>
      /// Арматурный слой по направлению в точке: деформация, напряжение (с ψs по состоянию) и
      /// секущий модуль σ/ε (при ε = 0 — начальный). Те же числа, что в <see cref="Compute"/>.
      /// </summary>
      public PlateRebarPoint EvaluateRebar(int rebarLayer, bool alongX, ShellStrainState s, Diagramm rDiag,
         IReadOnlyList<Diagramm?>? layerDiags = null, PlateLayerState? layerState = null)
      {
         CheckLayerState(layerState);
         var rl = RebarLayers[rebarLayer];
         var rd = layerDiags != null && rebarLayer < layerDiags.Count && layerDiags[rebarLayer] != null
                  ? layerDiags[rebarLayer]! : rDiag;
         double area = alongX ? rl.Asx : rl.Asy;
         double z = alongX ? rl.Zsx : rl.Zsy;
         if (rd == null || area <= 0.0)
            return new PlateRebarPoint(rebarLayer, alongX, z, 0.0, 0.0, 0.0, 0.0, 1.0);

         double eps = alongX ? s.EpsX(z) : s.EpsY(z);
         double sig = RebarStressPsi(rd, eps, layerState, rebarLayer, alongX);
         const double deps = 1e-7;
         double e0 = rd.Sig(deps, out _, tenB: true) / deps;
         double epsCrcPsi = layerState?.EpsCrc(rebarLayer, alongX) ?? double.NaN;
         double psi = layerState == null ? 1.0
            : eps > 0.0 && epsCrcPsi > 0.0 && eps < 0.2 * epsCrcPsi ? 0.2
            : Curvature8232.PsiS(epsCrcPsi, eps);
         return new PlateRebarPoint(rebarLayer, alongX, z, area, eps, sig, SecantModulus(sig, eps, e0), psi);
      }

      /// <summary>
      /// Отметить новые трещины в слоях бетона по деформациям <paramref name="s"/>: слой трещит,
      /// когда главная растягивающая деформация (при ν &gt; 0 — эквивалентная одноосная) выходит за
      /// ε_bt,ult. Критерий чисто деформационный и от учёта растяжения бетона не зависит.
      /// Возвращает число новых трещин и арматурные слои/направления, у которых бетон на их
      /// уровне уже с трещиной, а εs,crc ещё не определена (для <see cref="ActivatePsi"/>).
      /// </summary>
      public PlateCrackUpdate UpdateCracks(PlateLayerState layerState, ShellStrainState s, Diagramm cDiag,
         double? epsBtUlt = null)
      {
         ArgumentNullException.ThrowIfNull(layerState);
         if (!IsLayered)
            throw new InvalidOperationException($"Память трещин определена только для слоистой модели, а не \"{PlateModel}\".");
         CheckLayerState(layerState);

         double limit = epsBtUlt ?? ConcreteTensionLimit(cDiag);
         int nl = layerState.ConcreteLayerCount;
         double dz = H / nl;
         double z0 = -H / 2.0 + dz / 2.0;
         int newCracks = 0;
         for (int i = 0; i < nl; i++)
         {
            if (layerState.IsCracked(i)) continue;
            double zi = z0 + i * dz;
            PrincipalStrains2D(s.EpsX(zi), s.EpsY(zi), s.GammaXY(zi), out double eps1, out double eps2, out _);
            double nu = PoissonUncracked;
            double e1Eq = nu == 0.0 ? eps1 : (eps1 + nu * eps2) / (1.0 - nu * nu);
            if (e1Eq > limit)
            {
               layerState.MarkCracked(i);
               newCracks++;
            }
         }

         var pending = new List<(int RebarLayer, bool AlongX)>();
         for (int li = 0; li < RebarLayers.Count; li++)
         {
            var rl = RebarLayers[li];
            if (rl.Asx > 0.0 && !layerState.HasEpsCrc(li, true) && layerState.IsCracked(ConcreteLayerIndexAt(rl.Zsx)))
               pending.Add((li, true));
            if (rl.Asy > 0.0 && !layerState.HasEpsCrc(li, false) && layerState.IsCracked(ConcreteLayerIndexAt(rl.Zsy)))
               pending.Add((li, false));
         }
         return new PlateCrackUpdate(newCracks, pending);
      }

      /// <summary>
      /// Заморозить εs,crc у ожидающих арматурных слоёв (п. 8.2.32 СП 63, решения 1–2 спеки): по
      /// <see cref="Fem.ShellCrackingSolver"/> при текущем соотношении усилий элемента
      /// <paramref name="forces6"/> (Nx, Ny, Nxy, Mx, My, Mxy; кН, кН·м на 1 м) — деформация
      /// стержня в сечении с трещиной при M = M_crc. Если поиск не сошёлся — запасной путь:
      /// чистый изгиб полосы в направлении стержня, растягивающий его грань. Если не сошёлся и он,
      /// εs,crc = 0 (ψs = 1, слой больше не ожидает).
      /// </summary>
      public PlatePsiActivation ActivatePsi(PlateLayerState layerState,
         IReadOnlyList<(int RebarLayer, bool AlongX)> pending, double[] forces6,
         Diagramm cDiag, Diagramm rDiag)
      {
         ArgumentNullException.ThrowIfNull(layerState);
         ArgumentNullException.ThrowIfNull(pending);
         ArgumentNullException.ThrowIfNull(forces6);
         CheckLayerState(layerState);
         if (pending.Count == 0) return new PlatePsiActivation(0, 0, 0);

         var solver = new Fem.ShellCrackingSolver(this, cDiag, rDiag);
         var main = solver.Solve(forces6, alongX: true);
         var crackedMain = main.Converged ? main.CrackedStrainState : null;
         var fallbackStates = new Dictionary<(bool AlongX, int Sign), ShellStrainState?>();

         int activated = 0, fallbacks = 0, failures = 0;
         foreach (var (li, alongX) in pending)
         {
            var rl = RebarLayers[li];
            double z = alongX ? rl.Zsx : rl.Zsy;
            var st = crackedMain;
            if (st == null)
            {
               // Чистый изгиб полосы: M = ∫σ·z dz, растяжение стержня у грани z — момент знака z.
               int sign = z < 0.0 ? -1 : 1;
               if (!fallbackStates.TryGetValue((alongX, sign), out st))
               {
                  var target = new double[6];
                  target[alongX ? 3 : 4] = sign;
                  var r = solver.Solve(target, alongX);
                  st = r.Converged ? r.CrackedStrainState : null;
                  fallbackStates[(alongX, sign)] = st;
               }
               if (st != null) fallbacks++;
            }

            double epsCrc = st == null ? double.NaN : alongX ? st.EpsX(z) : st.EpsY(z);
            if (!double.IsFinite(epsCrc))
            {
               epsCrc = 0.0;
               failures++;
            }
            else activated++;
            layerState.SetEpsCrc(li, alongX, epsCrc);
         }
         return new PlatePsiActivation(activated, fallbacks, failures);
      }

      /// <summary>Напряжение арматуры с ψs (п. 8.2.32): у растянутого стержня с определённой
      /// εs,crc &gt; 0 — с диаграммы при деформации в трещине εs + 0,8·εs,crc (8.160–8.161), за
      /// текучестью поправка затухает сама; сжатый — по диаграмме без поправки. εs здесь — средняя
      /// деформация; при M = M_crc она равна 0,2·εs,crc (σ = σ(εs,crc)), и закон 8.160 действует с этой
      /// точки. При 0 &lt; εs &lt; 0,2·εs,crc (разгрузка, перераспределение, зона смены знака момента) —
      /// луч из начала координат в эту точку: иначе при εs → 0+ напряжение скачком уходило бы на
      /// 0,8·Es·εs,crc (~100 МПа).</summary>
      static double RebarStressPsi(Diagramm d, double eps, PlateLayerState? layerState, int rebarLayer, bool alongX)
      {
         if (layerState != null && eps > 0.0)
         {
            double epsCrc = layerState.EpsCrc(rebarLayer, alongX);
            if (epsCrc > 0.0)
               return eps >= 0.2 * epsCrc
                  ? RebarStress(d, eps + 0.8 * epsCrc)
                  : RebarStress(d, epsCrc) * eps / (0.2 * epsCrc);
         }
         return RebarStress(d, eps);
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

      double ConcreteStress(Diagramm d, double eps, double beta, bool? tensionOverride)
      {
         bool tension = tensionOverride ?? TensionConcrete;
         if (eps > 0.0)
            return tension ? d.Sig(eps, out _, tenB: true) : 0.0;
         return beta * d.Sig(eps, out _, tenB: false);
      }

      static double RebarStress(Diagramm d, double eps)
         => d.Sig(eps, out _);

      // ── Преобразование деформаций/напряжений (Мор) ────────────────────────

      internal static void PrincipalStrains2D(double ex, double ey, double gxy,
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

      /// <summary>Нижняя граница β = f<sub>cd2</sub>/f<sub>cd1</sub> = 0,60/0,85 (Model Code 1990):
      /// прочность растрескавшегося бетона не ниже, чем при сжатии поперёк трещин.</summary>
      internal const double VecchioCollinsBetaMin = 0.6 / 0.85;

      /// <summary>
      /// β-фактор снижения прочности бетона на сжатие при поперечном растяжении
      /// (Vecchio &amp; Collins, 1986) в редакции Craveiro et al., IBRACON 2021, ур. (50):
      /// β = 1 / (0,8 + 0,34·ε₁/|εc2|), 0,60/0,85 ≤ β ≤ 1; ε₁ — максимальная (растягивающая)
      /// главная деформация, εc2 — деформация бетона на пике диаграммы. При εc2 = 0,002
      /// совпадает с исходной формой 1/(0,8 + 170·ε₁) до нижней границы.
      /// </summary>
      internal static double VecchioCollinsBeta(double eps1, double epsC2)
      {
         if (eps1 <= 0.0) return 1.0;
         double ec2 = Math.Abs(epsC2) > 1e-9 ? Math.Abs(epsC2) : 0.002;
         double beta = 1.0 / (0.8 + 0.34 * eps1 / ec2);
         return beta < VecchioCollinsBetaMin ? VecchioCollinsBetaMin : beta > 1.0 ? 1.0 : beta;
      }

      // ── Секущие жёсткости ────────────────────────────────────────────────────

      /// <summary>
      /// Вычислить секущие и упругие жёсткости сечения по сходившемуся НДС.
      /// Секущий модуль: E_sec = σ(ε)/ε при |ε|&gt;1e-9, иначе начальный касательный E₀.
      /// Для «layered» и «char1d_principal»: главные оси + поворот тензора жёсткости (МКПТ).
      /// Для «char1d_axial»: осевые модули независимо по x и y.
      /// Единицы: EA — кН/м, EI — кН·м, Zc — мм.
      /// </summary>
      public ShellSecantStiffness ComputeSecant(
         ShellStrainState state,
         Diagramm cDiag, Diagramm rDiag,
         IReadOnlyList<Diagramm?>? layerDiags = null,
         bool? tensionOverride = null)
      {
         const double DEPS = 1e-7;
         // Начальный сжимающий модуль бетона (≥0), без ветви растяжения
         double E0c = -cDiag.Sig(-DEPS, out _, tenB: false) / DEPS;

         double EAx = 0, ESx = 0, EIx0 = 0;
         double EAy = 0, ESy = 0, EIy0 = 0;
         double EAxEl = 0, ESxEl = 0, EIx0El = 0;
         double EAyEl = 0, ESyEl = 0, EIy0El = 0;

         bool axial = PlateModel == "char1d_axial";
         int nl = NLayers < 1 ? 1 : NLayers;
         double dz = H / nl;
         double z0 = -H / 2.0 + dz / 2.0;

         for (int i = 0; i < nl; i++)
         {
            double zi  = z0 + i * dz;
            double ex  = state.EpsX(zi);
            double ey  = state.EpsY(zi);
            double gxy = state.GammaXY(zi);

            double EsX, EsY;
            if (axial)
            {
               double sigx = ConcreteStress(cDiag, ex, 1.0, tensionOverride);
               double sigy = ConcreteStress(cDiag, ey, 1.0, tensionOverride);
               EsX = SecantOrE0(sigx, ex, E0c);
               EsY = SecantOrE0(sigy, ey, E0c);
            }
            else
            {
               PrincipalStrains2D(ex, ey, gxy, out double eps1, out double eps2, out double theta);
               double beta = SofteningModel == "vecchio_collins"
                  ? VecchioCollinsBeta(eps1, SofteningEpsC2) : 1.0;

               double sig1 = ConcreteStress(cDiag, eps1, beta, tensionOverride);
               double sig2 = ConcreteStress(cDiag, eps2, beta, tensionOverride);

               double E1 = SecantOrE0(sig1, eps1, E0c);
               double E2 = SecantOrE0(sig2, eps2, E0c);

               double Esum = E1 + E2;
               double Gsec = Esum > 1e-9 ? E1 * E2 / Esum : 0.0;
               double cosT = Math.Cos(theta), sinT = Math.Sin(theta);
               double c2 = cosT * cosT, s2 = sinT * sinT;

               EsX = E1 * c2 * c2 + 4.0 * Gsec * s2 * c2 + E2 * s2 * s2;
               EsY = E1 * s2 * s2 + 4.0 * Gsec * s2 * c2 + E2 * c2 * c2;
            }

            EAx  += EsX * dz; ESx  += EsX * dz * zi; EIx0  += EsX * dz * zi * zi;
            EAy  += EsY * dz; ESy  += EsY * dz * zi; EIy0  += EsY * dz * zi * zi;
            EAxEl += E0c * dz; ESxEl += E0c * dz * zi; EIx0El += E0c * dz * zi * zi;
            EAyEl += E0c * dz; ESyEl += E0c * dz * zi; EIy0El += E0c * dz * zi * zi;
         }

         for (int li = 0; li < RebarLayers.Count; li++)
         {
            var rl = RebarLayers[li];
            var rd = layerDiags != null && li < layerDiags.Count && layerDiags[li] != null
                     ? layerDiags[li]! : rDiag;
            double E0r = rd.Sig(DEPS, out _, tenB: true) / DEPS;

            if (rl.Asx > 0)
            {
               double esx = state.EpsX(rl.Zsx);
               double ssx = RebarStress(rd, esx);
               double Es  = SecantOrE0(ssx, esx, E0r);
               EAx   += Es  * rl.Asx; ESx   += Es  * rl.Asx * rl.Zsx; EIx0  += Es  * rl.Asx * rl.Zsx * rl.Zsx;
               EAxEl += E0r * rl.Asx; ESxEl += E0r * rl.Asx * rl.Zsx; EIx0El += E0r * rl.Asx * rl.Zsx * rl.Zsx;
            }
            if (rl.Asy > 0)
            {
               double esy = state.EpsY(rl.Zsy);
               double ssy = RebarStress(rd, esy);
               double Es  = SecantOrE0(ssy, esy, E0r);
               EAy   += Es  * rl.Asy; ESy   += Es  * rl.Asy * rl.Zsy; EIy0  += Es  * rl.Asy * rl.Zsy * rl.Zsy;
               EAyEl += E0r * rl.Asy; ESyEl += E0r * rl.Asy * rl.Zsy; EIy0El += E0r * rl.Asy * rl.Zsy * rl.Zsy;
            }
         }

         static double SecantOrE0(double sig, double eps, double E0)
            => Math.Abs(eps) > 1e-9 ? sig / eps : E0;
         static double ZcFrom(double ES, double EA)
            => Math.Abs(EA) > 1e-9 ? ES / EA : 0.0;
         static double EIcFrom(double EI0, double ES, double EA)
            => Math.Abs(EA) > 1e-9 ? EI0 - ES * ES / EA : EI0;
         static double PhiSafe(double a, double b)
            => Math.Abs(b) > 1e-9 ? a / b : -1.0;

         double zcx   = ZcFrom(ESx,   EAx);   double zcy   = ZcFrom(ESy,   EAy);
         double EIxc  = EIcFrom(EIx0,  ESx,   EAx);  double EIyc  = EIcFrom(EIy0,  ESy,   EAy);
         double zcxEl = ZcFrom(ESxEl, EAxEl); double zcyEl = ZcFrom(ESyEl, EAyEl);
         double EIxcEl = EIcFrom(EIx0El, ESxEl, EAxEl);
         double EIycEl = EIcFrom(EIy0El, ESyEl, EAyEl);

         return new ShellSecantStiffness
         {
            EAx = EAx, EAy = EAy,
            ZcxMm = zcx * 1e3, ZcyMm = zcy * 1e3,
            EIxc = EIxc, EIyc = EIyc,
            EAxEl = EAxEl, EAyEl = EAyEl,
            ZcxElMm = zcxEl * 1e3, ZcyElMm = zcyEl * 1e3,
            EIxcEl = EIxcEl, EIycEl = EIycEl,
            PhiEAx  = PhiSafe(EAx,  EAxEl),  PhiEAy  = PhiSafe(EAy,  EAyEl),
            PhiEIxc = PhiSafe(EIxc, EIxcEl), PhiEIyc = PhiSafe(EIyc, EIycEl),
         };
      }
   }
}

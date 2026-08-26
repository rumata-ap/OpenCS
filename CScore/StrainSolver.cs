using System;

namespace CScore
{
   /// <summary>
   /// Итерационный решатель плоскости деформаций методом Ньютона-Рафсона.
   /// Находит (e0, ky, kz) такую, что CrossSection.Integral(k) ≈ (N, My, Mz).
   /// </summary>
   public class StrainSolver
   {
      public bool   Converged  { get; private set; }
      public int    Iterations { get; private set; }
      public double Residual   { get; private set; }

      readonly CrossSection _section;
      readonly CalcType     _calc;
      readonly bool         _ten;
      readonly bool         _ca;
      readonly double       _tol;
      readonly int          _maxIter;
      readonly double       _h;
      readonly bool         _central;
      readonly Func<Kurvature, Load> _evaluate;

      /// <summary>Максимум половинных делений шага на одной итерации (λ до ~1e-9).</summary>
      const int MaxBacktracks = 30;

      /// <summary>
      /// Предельная деформация волокна, за которую не выпускается пробный шаг.
      /// Заведомо больше предельных деформаций любых диаграмм (ε_t2 арматуры ≈ 0.025),
      /// но не даёт Ньютону улететь туда, где ВСЕ волокна выключены: там отклик почти
      /// постоянен, невязка формально «улучшается», а якобиан вырождается в ноль.
      /// </summary>
      const double EpsBound = 0.05;

      double _yExtent = -1.0;
      double _xExtent = -1.0;

      public StrainSolver(CrossSection section, CalcType calc = CalcType.C,
                          bool ten = true, bool ca = true,
                          double tol = 0.5, int maxIter = 60, double h = 1e-7,
                          bool centralJacobian = true,
                          Func<Kurvature, Load>? evaluate = null)
      {
         _section = section;
         _calc    = calc;
         _ten     = ten;
         _ca      = ca;
         _tol     = tol;
         _maxIter = maxIter;
         _h       = h;
         _central = centralJacobian;
         _evaluate = evaluate ?? (k => _section.Integral(k, _calc, _ten, _ca));
      }

      /// <summary>
      /// Решает обратную задачу: при заданных target-усилиях N/My/Mz (кН, кН·м)
      /// находит кривизну k = (e0, ky, kz). Возвращает найденную Kurvature.
      /// </summary>
      /// <param name="initialGuess">
      /// Начальное приближение кривизны. Null (по умолчанию) — используется штатная упругая
      /// оценка <see cref="CrossSection.Guess"/>. Передавайте явное приближение, когда упругая
      /// оценка заведомо далека от решения (например, для трещиноватого сечения, где реальная
      /// жёсткость сильно отличается от упругой) — это предотвращает расхождение метода Ньютона.
      /// </param>
      public Kurvature Solve(double nTarget, double mxTarget, double myTarget, Kurvature? initialGuess = null)
      {
         var target = new Load { N = nTarget, Mx = mxTarget, My = myTarget };
         Kurvature k = initialGuess ?? _section.Guess(target);
         if (!double.IsFinite(k.e0)) k.e0 = 0;
         if (!double.IsFinite(k.ky)) k.ky = 0;
         if (!double.IsFinite(k.kz)) k.kz = 0;

         // Один откат к штатной упругой оценке: спасает от заведомо плохого стартового
         // приближения (плоскость в зоне, где все волокна выключены — якобиан там нулевой,
         // а невязка почти не зависит от кривизны).
         bool elasticRestartUsed = initialGuess == null;

         for (int iter = 0; iter < _maxIter; iter++)
         {
            var f0 = _evaluate(k);
            double r0 = f0.N  - nTarget;
            double r1 = f0.Mx - mxTarget;
            double r2 = f0.My - myTarget;

            Residual = Math.Sqrt(r0 * r0 + r1 * r1 + r2 * r2);
            Iterations = iter + 1;

            if (Residual < _tol) { Converged = true; break; }

            // Числовой Якобиан 3×3 (центральные разности)
            double[,] J = new double[3, 3];
            var axes = new[]
            {
               new Kurvature { e0 = _h },
               new Kurvature { ky = _h },
               new Kurvature { kz = _h },
            };
            for (int j = 0; j < 3; j++)
            {
               var fp = _evaluate(k + axes[j]);
               if (_central)
               {
                  var fm = _evaluate(k - axes[j]);
                  J[0, j] = (fp.N  - fm.N)  / (2 * _h);
                  J[1, j] = (fp.Mx - fm.Mx) / (2 * _h);
                  J[2, j] = (fp.My - fm.My) / (2 * _h);
               }
               else
               {
                  J[0, j] = (fp.N  - f0.N)  / _h;
                  J[1, j] = (fp.Mx - f0.Mx) / _h;
                  J[2, j] = (fp.My - f0.My) / _h;
               }
            }

            // Решение 3×3 системы J·Δk = r методом Гаусса
            double[] rhs = [r0, r1, r2];
            if (!GaussSolve(J, rhs, out double[] dk))
            {
               // Вырожденный якобиан: вне диаграмм отклик не зависит от плоскости.
               if (TryElasticRestart(ref k, target, ref elasticRestartUsed)) continue;
               break;
            }

            if (!TryDampedStep(k, dk, nTarget, mxTarget, myTarget, Residual, out k))
            {
               // Ни одна доля шага не уменьшает невязку.
               if (TryElasticRestart(ref k, target, ref elasticRestartUsed)) continue;
               break;
            }
         }

         return k;
      }

      /// <summary>
      /// Возврат к штатному упругому приближению <see cref="CrossSection.Guess"/>, если
      /// итерации застряли (вырожденный якобиан или шаг, не уменьшающий невязку).
      /// Выполняется не более одного раза за решение и только тогда, когда старт был задан
      /// извне — иначе перезапуск привёл бы в ту же точку.
      /// </summary>
      bool TryElasticRestart(ref Kurvature k, Load target, ref bool used)
      {
         if (used) return false;
         used = true;
         k = _section.Guess(target);
         if (!double.IsFinite(k.e0)) k.e0 = 0;
         if (!double.IsFinite(k.ky)) k.ky = 0;
         if (!double.IsFinite(k.kz)) k.kz = 0;
         return true;
      }

      /// <summary>
      /// Шаг Ньютона с демпфированием (backtracking): пробует полный шаг, затем
      /// половинные доли, и принимает первую, которая уменьшает норму невязки.
      /// Без этого чистый Ньютон на сильно нелинейной диаграмме (или при плохом
      /// начальном приближении) перелетает решение и входит в предельный цикл.
      /// </summary>
      /// <returns>false, если ни одна доля шага не улучшила невязку.</returns>
      bool TryDampedStep(Kurvature k, double[] dk,
                         double nTarget, double mxTarget, double myTarget,
                         double residual, out Kurvature next)
      {
         double lambda = StepLimit(dk);
         for (int attempt = 0; attempt < MaxBacktracks; attempt++)
         {
            var trial = new Kurvature
            {
               e0 = k.e0 - lambda * dk[0],
               ky = k.ky - lambda * dk[1],
               kz = k.kz - lambda * dk[2],
            };

            if (double.IsFinite(trial.e0) && double.IsFinite(trial.ky) && double.IsFinite(trial.kz))
            {
               var f = _evaluate(trial);
               double d0 = f.N - nTarget, d1 = f.Mx - mxTarget, d2 = f.My - myTarget;
               double trialResidual = Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
               if (double.IsFinite(trialResidual) && trialResidual < residual)
               {
                  next = trial;
                  return true;
               }
            }

            lambda *= 0.5;
         }

         next = k;
         return false;
      }

      /// <summary>
      /// Начальная доля шага λ₀: полный шаг, если он не выводит крайнее волокно
      /// сечения за <see cref="EpsBound"/>, иначе — доля, которая укладывается в границу.
      /// </summary>
      double StepLimit(double[] dk)
      {
         EnsureExtents();
         double span = Math.Abs(dk[0]) + Math.Abs(dk[1]) * _yExtent + Math.Abs(dk[2]) * _xExtent;
         if (!double.IsFinite(span) || span <= EpsBound) return 1.0;
         return EpsBound / span;
      }

      /// <summary>Габариты сечения от начала координат — для оценки деформации крайнего волокна.</summary>
      void EnsureExtents()
      {
         if (_yExtent >= 0.0) return;
         try
         {
            var (minX, maxX, minY, maxY) = _section.SectionBoundingBox();
            _xExtent = Math.Max(Math.Abs(minX), Math.Abs(maxX));
            _yExtent = Math.Max(Math.Abs(minY), Math.Abs(maxY));
         }
         catch (InvalidOperationException)
         {
            _xExtent = 0.0;
            _yExtent = 0.0;
         }
      }

      // Метод Гаусса с выбором ведущего элемента. Возвращает false при сингулярности.
      static bool GaussSolve(double[,] a, double[] b, out double[] x)
      {
         x = new double[3];
         // Копия для работы
         double[,] m = (double[,])a.Clone();
         double[]  v = (double[])b.Clone();
         int n = 3;

         for (int col = 0; col < n; col++)
         {
            // Поиск ведущего элемента
            int pivot = col;
            for (int row = col + 1; row < n; row++)
               if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                  pivot = row;

            double pivVal = m[pivot, col];
            if (!double.IsFinite(pivVal) || Math.Abs(pivVal) < 1e-15)
               return false;

            // Перестановка строк
            if (pivot != col)
            {
               for (int k2 = 0; k2 < n; k2++)
                  (m[col, k2], m[pivot, k2]) = (m[pivot, k2], m[col, k2]);
               (v[col], v[pivot]) = (v[pivot], v[col]);
            }

            // Прямой ход
            for (int row = col + 1; row < n; row++)
            {
               double factor = m[row, col] / m[col, col];
               for (int k2 = col; k2 < n; k2++)
                  m[row, k2] -= factor * m[col, k2];
               v[row] -= factor * v[col];
            }
         }

         // Обратный ход
         for (int row = n - 1; row >= 0; row--)
         {
            double sum = v[row];
            for (int k2 = row + 1; k2 < n; k2++)
               sum -= m[row, k2] * x[k2];
            x[row] = sum / m[row, row];
         }

         return true;
      }
   }
}

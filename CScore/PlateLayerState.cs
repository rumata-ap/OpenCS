using System;
using System.Collections.Generic;

namespace CScore
{
   /// <summary>
   /// Слой бетона слоистой пластины в точке: главные деформации ε₁ ≥ ε₂ и угол θ главной оси 1
   /// к оси x, эквивалентные одноосные деформации, секущие модули по диаграмме E₁, E₂, матрица
   /// слоя Q в главных осях (σ₁ = Q11·ε₁ + Q12·ε₂, σ₂ = Q12·ε₁ + Q22·ε₂), секущий G₁₂ и
   /// напряжения σ₁, σ₂ — ровно те, что интегрируются в усилия. Напряжения — кПа.
   /// </summary>
   public readonly record struct PlateConcreteLayerPoint(
      int Layer, double Z, double Dz,
      double Eps1, double Eps2, double Theta,
      double Eps1Eq, double Eps2Eq, double Nu,
      double E1, double E2, double Q11, double Q12, double Q22, double G12,
      double Sig1, double Sig2, bool Cracked);

   /// <summary>Арматурный слой по направлению в точке: z, площадь на 1 м (м²/м), деформация,
   /// напряжение (кПа, с ψs), секущий модуль σ/ε и ψs. Area = 0 — стержней этого направления нет.</summary>
   public readonly record struct PlateRebarPoint(
      int RebarLayer, bool AlongX, double Z, double Area,
      double Eps, double Sig, double ESecant, double Psi);

   /// <summary>Итог <see cref="PlateSection.UpdateCracks"/>: новые трещины и арматура, ждущая εs,crc.</summary>
   public sealed record PlateCrackUpdate(int NewCracks, IReadOnlyList<(int RebarLayer, bool AlongX)> PendingPsi);

   /// <summary>Итог <see cref="PlateSection.ActivatePsi"/>: сколько εs,crc определено (из них —
   /// запасным путём по чистому изгибу) и сколько не найдено вовсе (ψs = 1).</summary>
   public readonly record struct PlatePsiActivation(int Activated, int Fallbacks, int Failures);

   /// <summary>
   /// Память слоистого сечения пластины в нелинейном расчёте: трещины в слоях бетона и
   /// деформации арматуры в трещине εs,crc (п. 8.2.32 СП 63) по арматурным слоям и направлениям.
   ///
   /// Трещина в слое необратима: однажды отмеченный слой дальше не несёт растяжения и работает
   /// с ν = 0 — иначе в итерациях слой «мигал» бы между состояниями. εs,crc замораживается в момент
   /// первой трещины в слое бетона на уровне арматуры; до этого ψs = 1.
   ///
   /// Пробное и зафиксированное состояния разделены: <see cref="Commit"/> принимает шаг,
   /// <see cref="Revert"/> откатывает отвергнутый шаг к последнему принятому, <see cref="Reset"/>
   /// очищает всё. Читается всегда пробное состояние (оно включает зафиксированное).
   /// Не потокобезопасен: одно состояние — на один КЭ (точку интегрирования).
   /// </summary>
   public sealed class PlateLayerState
   {
      readonly bool[] _cracked, _crackedCommitted;
      readonly double[] _epsCrcX, _epsCrcY, _epsCrcXCommitted, _epsCrcYCommitted;

      /// <param name="concreteLayers">Число слоёв бетона (<see cref="PlateSection.NLayers"/>).</param>
      /// <param name="rebarLayers">Число арматурных слоёв (<see cref="PlateSection.RebarLayers"/>).</param>
      public PlateLayerState(int concreteLayers, int rebarLayers)
      {
         if (concreteLayers < 1) throw new ArgumentOutOfRangeException(nameof(concreteLayers));
         if (rebarLayers < 0) throw new ArgumentOutOfRangeException(nameof(rebarLayers));
         _cracked = new bool[concreteLayers];
         _crackedCommitted = new bool[concreteLayers];
         _epsCrcX = Unset(rebarLayers);
         _epsCrcY = Unset(rebarLayers);
         _epsCrcXCommitted = Unset(rebarLayers);
         _epsCrcYCommitted = Unset(rebarLayers);
      }

      /// <summary>Состояние под сечение: размеры берутся из него.</summary>
      public static PlateLayerState For(PlateSection section)
      {
         ArgumentNullException.ThrowIfNull(section);
         return new PlateLayerState(section.NLayers < 1 ? 1 : section.NLayers, section.RebarLayers.Count);
      }

      public int ConcreteLayerCount => _cracked.Length;
      public int RebarLayerCount => _epsCrcX.Length;

      /// <summary>Есть ли трещина в слое бетона.</summary>
      public bool IsCracked(int layer) => _cracked[layer];

      /// <summary>Число слоёв бетона с трещиной.</summary>
      public int CrackedCount
      {
         get
         {
            int n = 0;
            foreach (bool c in _cracked) if (c) n++;
            return n;
         }
      }

      /// <summary>Отметить трещину в слое (необратимо в пределах расчёта).</summary>
      public void MarkCracked(int layer) => _cracked[layer] = true;

      /// <summary>εs,crc арматурного слоя по направлению; NaN — ещё не определена (ψs = 1).</summary>
      public double EpsCrc(int rebarLayer, bool alongX)
         => alongX ? _epsCrcX[rebarLayer] : _epsCrcY[rebarLayer];

      /// <summary>Определена ли εs,crc (ψs учитывается). Значение ≤ 0 — определена, но поправки
      /// не даёт (стержень в момент трещины не растянут либо εs,crc найти не удалось).</summary>
      public bool HasEpsCrc(int rebarLayer, bool alongX) => !double.IsNaN(EpsCrc(rebarLayer, alongX));

      /// <summary>Заморозить εs,crc арматурного слоя по направлению.</summary>
      public void SetEpsCrc(int rebarLayer, bool alongX, double epsCrc)
      {
         if (double.IsNaN(epsCrc)) throw new ArgumentException("εs,crc не может быть NaN", nameof(epsCrc));
         if (alongX) _epsCrcX[rebarLayer] = epsCrc;
         else _epsCrcY[rebarLayer] = epsCrc;
      }

      /// <summary>Принять пробное состояние как зафиксированное (шаг сошёлся).</summary>
      public void Commit()
      {
         Array.Copy(_cracked, _crackedCommitted, _cracked.Length);
         Array.Copy(_epsCrcX, _epsCrcXCommitted, _epsCrcX.Length);
         Array.Copy(_epsCrcY, _epsCrcYCommitted, _epsCrcY.Length);
      }

      /// <summary>Откатить пробное состояние к последнему зафиксированному (шаг отвергнут).</summary>
      public void Revert()
      {
         Array.Copy(_crackedCommitted, _cracked, _cracked.Length);
         Array.Copy(_epsCrcXCommitted, _epsCrcX, _epsCrcX.Length);
         Array.Copy(_epsCrcYCommitted, _epsCrcY, _epsCrcY.Length);
      }

      /// <summary>Очистить всё: трещин нет, εs,crc не определены.</summary>
      public void Reset()
      {
         Array.Clear(_cracked); Array.Clear(_crackedCommitted);
         Array.Fill(_epsCrcX, double.NaN); Array.Fill(_epsCrcY, double.NaN);
         Array.Fill(_epsCrcXCommitted, double.NaN); Array.Fill(_epsCrcYCommitted, double.NaN);
      }

      /// <summary>Глубокая копия (пробное и зафиксированное состояния).</summary>
      public PlateLayerState Clone()
      {
         var c = new PlateLayerState(ConcreteLayerCount, RebarLayerCount);
         Array.Copy(_cracked, c._cracked, _cracked.Length);
         Array.Copy(_crackedCommitted, c._crackedCommitted, _cracked.Length);
         Array.Copy(_epsCrcX, c._epsCrcX, _epsCrcX.Length);
         Array.Copy(_epsCrcY, c._epsCrcY, _epsCrcY.Length);
         Array.Copy(_epsCrcXCommitted, c._epsCrcXCommitted, _epsCrcX.Length);
         Array.Copy(_epsCrcYCommitted, c._epsCrcYCommitted, _epsCrcY.Length);
         return c;
      }

      static double[] Unset(int n)
      {
         var a = new double[n];
         Array.Fill(a, double.NaN);
         return a;
      }
   }
}

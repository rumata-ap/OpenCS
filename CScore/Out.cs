using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CScore
{
   /// <summary>
   /// Результат расчёта поперечного сечения — содержит все параметры,
   /// характеризующие напряжённо-деформированное состояние сечения
   /// при заданной плоскости деформаций.
   /// </summary>
   [Serializable]
   public struct Out
   {
      /// <summary>
      /// Описание решения (текстовый статус).
      /// </summary>
      public string Solution;

      /// <summary>
      /// Признак сходимости итерационного процесса.
      /// </summary>
      public bool IsConverge;

      /// <summary>
      /// Признак наличия растянутых волокон в сечении.
      /// </summary>
      public bool IsTension;

      /// <summary>
      /// Признак наличия сжатых волокон в сечении.
      /// </summary>
      public bool IsCompression;

      /// <summary>
      /// Признак образования трещин в бетоне.
      /// </summary>
      public bool IsCrack;

      /// <summary>
      /// Номер итерации.
      /// </summary>
      public int i;

      /// <summary>
      /// Минимальная деформация в сечении (ε_b,min).
      /// </summary>
      public double ebmin;

      /// <summary>
      /// Максимальная деформация в сечении (ε_b,max).
      /// </summary>
      public double ebmax;

      /// <summary>
      /// Максимальная деформация в арматуре (ε_s,max).
      /// </summary>
      public double esmax;

      /// <summary>
      /// Расстояние от центра тяжести до равнодействующей сжимающих сил по оси X.
      /// </summary>
      public double ax;

      /// <summary>
      /// Расстояние от центра тяжести до равнодействующей сжимающих сил по оси Y.
      /// </summary>
      public double ay;

      /// <summary>
      /// Площадь сжатой зоны бетона.
      /// </summary>
      public double sa;

      /// <summary>
      /// Площадь растянутой зоны сечения.
      /// </summary>
      public double scrc;

      /// <summary>
      /// Коэффициент раскрытия трещин (ψ_s).
      /// </summary>
      public double psis;

      /// <summary>
      /// Относительная деформация в центре тяжести (ε₀).
      /// </summary>
      public double e0;

      /// <summary>
      /// Кривизна плоскости деформаций относительно оси X (k_x).
      /// </summary>
      public double kx;

      /// <summary>
      /// Кривизна плоскости деформаций относительно оси Y (k_y).
      /// </summary>
      public double ky;

      /// <summary>
      /// Площадь арматуры [м²].
      /// </summary>
      public double As;

      /// <summary>
      /// Площадь бетона сжатой зоны [м²].
      /// </summary>
      public double Abt;

      /// <summary>
      /// Приведённая площадь сечения (EA).
      /// </summary>
      public double EA;

      /// <summary>
      /// Приведённый момент инерции относительно оси X (EI_x).
      /// </summary>
      public double EIx;

      /// <summary>
      /// Приведённый момент инерции относительно оси Y (EI_y).
      /// </summary>
      public double EIy;

      /// <summary>
      /// Жёсткость при кручении (K_a).
      /// </summary>
      public double Ka;

      /// <summary>
      /// Жёсткость при изгибе относительно X (K_ix).
      /// </summary>
      public double Kix;

      /// <summary>
      /// Жёсткость при изгибе относительно Y (K_iy).
      /// </summary>
      public double Kiy;

      /// <summary>
      /// Предельная жёсткость (K_ult).
      /// </summary>
      public double Kult;

      /// <summary>
      /// Погрешность итерационного процесса.
      /// </summary>
      public double Error;

      /// <summary>
      /// Вычисленные внутренние усилия (результат расчёта).
      /// </summary>
      public Load OutLoad;

      /// <summary>
      /// Внешняя нагрузка (исходные данные).
      /// </summary>
      public Load InLoad;

      /// <summary>
      /// Фактическая нагрузка (скорректированная).
      /// </summary>
      public Load TrueLoad;

      /// <summary>
      /// Пороговые значения усилий.
      /// </summary>
      public Load Threshould;

      /// <summary>
      /// Усилия, соответствующие образованию трещин.
      /// </summary>
      public Load Crack;

      /// <summary>
      /// Деформации образования трещин.
      /// </summary>
      public List<double> ecrc;
   }
}
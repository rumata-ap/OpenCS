using CScore.Fem;

namespace CScore
{
   /// <summary>
   /// Огибающая строк набора усилий по сечениям КЭ: наименьшее и наибольшее значение каждой компоненты
   /// среди строк одного сечения. Мозаикам и эпюрам усилий нужны только эти значения — огибающую набора в
   /// миллионы строк можно посчитать в БД, не загружая строки в память.
   /// </summary>
   /// <remarks>
   /// Каналы пластин — <see cref="ShellForceComponent"/>: Nx/Ny/Nxy берутся из строк с погонными усилиями,
   /// σx/σy/τxy — из строк с напряжениями (недостающее напряжение такой строки равно нулю), моменты и
   /// поперечные силы — из всех строк. Каналы стержней — <see cref="BarForceComponent"/>.
   /// Строки без номера КЭ в огибающую не входят.
   /// </remarks>
   public sealed class ForceSetEnvelope
   {
      /// <summary>Число каналов пластин.</summary>
      public static readonly int ShellChannels = Enum.GetValues<ShellForceComponent>().Length;

      /// <summary>Число каналов стержней.</summary>
      public static readonly int BarChannels = Enum.GetValues<BarForceComponent>().Length;

      /// <summary>Сечение КЭ: наименьшие и наибольшие значения по каналам (NaN — значений нет).</summary>
      /// <param name="Elem">Номер КЭ.</param>
      /// <param name="Section">Номер сечения; null — у строк его нет.</param>
      public sealed record Entry(int Elem, int? Section, double[] Min, double[] Max)
      {
         /// <summary>Значение канала по правилу выбора; null — значений нет.</summary>
         public double? Pick(int channel, ForceRowAggregate aggregate) => Pick(Min[channel], Max[channel], aggregate);

         /// <summary>Выбор из наименьшего и наибольшего значения; null — значений нет.</summary>
         public static double? Pick(double min, double max, ForceRowAggregate aggregate)
         {
            if (double.IsNaN(min) || double.IsNaN(max)) return null;
            return aggregate switch
            {
               ForceRowAggregate.Max => max,
               ForceRowAggregate.Min => min,
               _ => Math.Abs(min) > Math.Abs(max) ? min : max,
            };
         }
      }

      /// <summary>Сечения КЭ в порядке номеров КЭ и сечений.</summary>
      public IReadOnlyList<Entry> Entries { get; }

      /// <summary>Есть строки пластин с напряжениями.</summary>
      public bool HasStresses { get; }

      /// <param name="entries">Сечения КЭ (порядок любой).</param>
      /// <param name="hasStresses">Есть строки пластин с напряжениями.</param>
      public ForceSetEnvelope(IEnumerable<Entry> entries, bool hasStresses)
      {
         Entries = entries.OrderBy(e => e.Elem).ThenBy(e => e.Section ?? int.MinValue).ToList();
         HasStresses = hasStresses;
      }

      /// <summary>Сечения КЭ, сгруппированные по номеру КЭ.</summary>
      public ILookup<int, Entry> ByElement() => Entries.ToLookup(e => e.Elem);

      /// <summary>Огибающая строк пластин.</summary>
      public static ForceSetEnvelope OfShell(IEnumerable<ShellLoadItem> rows)
      {
         var builder = new Builder(shell: true);
         foreach (var r in rows) builder.Add(r);
         return builder.Build();
      }

      /// <summary>Огибающая строк стержней.</summary>
      public static ForceSetEnvelope OfBar(IEnumerable<LoadItem> rows)
      {
         var builder = new Builder(shell: false);
         foreach (var r in rows) builder.Add(r);
         return builder.Build();
      }

      /// <summary>
      /// Накопление огибающей по строкам. Ссылки на строки не сохраняются — источник может подавать один и
      /// тот же объект строки, перезаполняя его (чтение из БД без объекта на строку).
      /// </summary>
      public sealed class Builder
      {
         readonly int _channels;
         readonly double[] _values;
         readonly Dictionary<(int, int?), Entry> _entries = [];
         bool _hasStresses;

         /// <param name="shell">Строки пластин, иначе стержней.</param>
         public Builder(bool shell)
         {
            _channels = shell ? ShellChannels : BarChannels;
            _values = new double[_channels];
         }

         /// <summary>Добавляет строку пластины (без номера КЭ — пропускается).</summary>
         public void Add(ShellLoadItem r)
         {
            if (r.SourceElementNum is not int elem) return;
            bool stress = r.SigmaX != null || r.SigmaY != null || r.TauXY != null;
            _hasStresses |= stress;
            var v = _values;
            v[(int)ShellForceComponent.Nx] = stress ? double.NaN : r.Nx;
            v[(int)ShellForceComponent.Ny] = stress ? double.NaN : r.Ny;
            v[(int)ShellForceComponent.Nxy] = stress ? double.NaN : r.Nxy;
            v[(int)ShellForceComponent.Mx] = r.Mx;
            v[(int)ShellForceComponent.My] = r.My;
            v[(int)ShellForceComponent.Mxy] = r.Mxy;
            v[(int)ShellForceComponent.Qx] = r.Qx;
            v[(int)ShellForceComponent.Qy] = r.Qy;
            v[(int)ShellForceComponent.SigmaX] = stress ? r.SigmaX ?? 0 : double.NaN;
            v[(int)ShellForceComponent.SigmaY] = stress ? r.SigmaY ?? 0 : double.NaN;
            v[(int)ShellForceComponent.TauXY] = stress ? r.TauXY ?? 0 : double.NaN;
            Add(elem, r.SourceSectionNum);
         }

         /// <summary>Добавляет строку стержня (без номера КЭ — пропускается).</summary>
         public void Add(LoadItem r)
         {
            if (r.SourceElementNum is not int elem) return;
            foreach (var c in Enum.GetValues<BarForceComponent>())
               _values[(int)c] = ElementForceField.BarValue(r, c);
            Add(elem, r.SourceSectionNum);
         }

         void Add(int elem, int? section)
         {
            if (!_entries.TryGetValue((elem, section), out var e))
            {
               e = new Entry(elem, section, Filled(_channels), Filled(_channels));
               _entries[(elem, section)] = e;
            }
            for (int c = 0; c < _channels; c++)
            {
               double v = _values[c];
               if (!double.IsFinite(v)) continue;
               if (double.IsNaN(e.Min[c]) || v < e.Min[c]) e.Min[c] = v;
               if (double.IsNaN(e.Max[c]) || v > e.Max[c]) e.Max[c] = v;
            }
         }

         /// <summary>Готовая огибающая.</summary>
         public ForceSetEnvelope Build() => new(_entries.Values, _hasStresses);

         static double[] Filled(int n)
         {
            var a = new double[n];
            Array.Fill(a, double.NaN);
            return a;
         }
      }
   }
}

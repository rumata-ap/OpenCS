using System.Text;

namespace CScore.Import;

/// <summary>Грань/направление слоя пластинчатого ТЗА ЛИРЫ: X/Y — вдоль осей X1/Y1 КЭ, T/B — у грани Z+/Z−.</summary>
public enum LiraPlateRebarSlot { XT, XB, YT, YB }

/// <summary>Привязка арматуры пластинчатого ТЗА (байт в хвосте записи версии 4).</summary>
public enum LiraRebarBinding
{
   /// <summary>Неизвестный код привязки.</summary>
   Unknown = 0,
   /// <summary>ЗС — задан защитный слой от грани до поверхности стержня.</summary>
   Cover = 1,
   /// <summary>ЦТ — задано расстояние от грани до центра тяжести арматуры.</summary>
   Centroid = 2,
   /// <summary>M — привязка из материалов; в файле хранится значение a на момент сохранения
   /// (в ЛИРЕ a1/a2 материалов — расстояния до ц. т. арматуры).</summary>
   FromMaterials = 3,
}

/// <summary>Слагаемое формулы слоя <c>dDsS</c>: n стержней Ø d с шагом s.</summary>
/// <param name="Count">Число стержней в слагаемом (1 для <c>dDsS</c>).</param>
/// <param name="DiameterMm">Диаметр стержня, мм.</param>
/// <param name="BarAreaCm2">Площадь одного стержня, см².</param>
/// <param name="SpacingMm">Шаг, мм.</param>
public sealed record LiraPlateRebarTerm(int Count, double DiameterMm, double BarAreaCm2, double SpacingMm)
{
   /// <summary>Площадь слагаемого на 1 п. м, см²/м. При шаге ≤ 0 — число стержней на метр.</summary>
   public double AreaPerMeterCm2 => SpacingMm > 0 ? Count * BarAreaCm2 * 1000.0 / SpacingMm : Count * BarAreaCm2;
}

/// <summary>Слой пластинчатого ТЗА (одно направление у одной грани).</summary>
public sealed class LiraPlateRebarLayer
{
   /// <summary>Грань и направление слоя.</summary>
   public LiraPlateRebarSlot Slot { get; init; }
   /// <summary>Слой задан суммарной площадью ΣAs (иначе — формулой <c>dDsS+…</c>).</summary>
   public bool IsTotalArea { get; init; }
   /// <summary>Формула слоя (пустая при ΣAs).</summary>
   public string Formula { get; init; } = "";
   /// <summary>ΣAs из файла, см²/м. Достоверна только при <see cref="IsTotalArea"/>.</summary>
   public double TotalAreaCm2 { get; init; }
   /// <summary>Привязка a из файла, см: защитный слой (ЗС) или расстояние до ц. т. (ЦТ, M).</summary>
   public double A { get; init; }
   /// <summary>Слагаемые формулы (пусто при ΣAs).</summary>
   public IReadOnlyList<LiraPlateRebarTerm> Terms { get; init; } = [];

   /// <summary>Площадь слоя на 1 п. м, см²/м.</summary>
   public double AreaPerMeterCm2 => IsTotalArea ? TotalAreaCm2 : Terms.Sum(t => t.AreaPerMeterCm2);

   /// <summary>
   /// Расстояние от грани до ц. т. слоя, см. При ЗС — a + d/2 каждого слагаемого, усреднённое
   /// по площади; Ø стержней соседнего (наружного) слоя не прибавляется: в ЛИРЕ защитный слой
   /// внутреннего направления задаётся пользователем сразу от грани.
   /// </summary>
   public double CentroidDistanceCm(LiraRebarBinding binding)
   {
      if (binding != LiraRebarBinding.Cover || IsTotalArea || Terms.Count == 0)
         return A;
      double area = 0, moment = 0;
      foreach (var t in Terms)
      {
         double ai = t.AreaPerMeterCm2;
         area += ai;
         moment += ai * (A + t.DiameterMm / 20.0);
      }
      return area > 0 ? moment / area : A + Terms[0].DiameterMm / 20.0;
   }
}

/// <summary>Пластинчатый ТЗА (AS / AS_S_* / составной AS_mult).</summary>
public sealed class LiraPlateReinforcementType
{
   /// <summary>Номер ТЗА (совпадает с номерами в таблице «Элементы - ТЗА»).</summary>
   public int Id { get; init; }
   /// <summary>Код шаблона записи: 54 — AS/AS_S_*, 52 — AS_mult.</summary>
   public int Kind { get; init; }
   /// <summary>Имя ТЗА (автоимя вида <c>AS XT d12s200/3.00 …</c>).</summary>
   public string Name { get; init; } = "";
   /// <summary>Комментарий пользователя.</summary>
   public string Comment { get; init; } = "";
   /// <summary>Привязка арматуры.</summary>
   public LiraRebarBinding Binding { get; init; }
   /// <summary>Код симметрии из файла (0 — нет, 1 — полная; прочие не проверены).</summary>
   public int Symmetry { get; init; }
   /// <summary>Слои после размножения по симметрии.</summary>
   public IReadOnlyList<LiraPlateRebarLayer> Layers { get; init; } = [];
   /// <summary>Номера составляющих ТЗА (только для AS_mult).</summary>
   public IReadOnlyList<int> ComponentIds { get; init; } = [];

   /// <summary>Составной тип AS_mult.</summary>
   public bool IsComposite => Kind == LiraRbtReader.KindPlateMultiple;
}

/// <summary>Грань сечения, у которой стоит арматура простого брусового ТЗА.</summary>
public enum LiraBarRebarFace
{
   /// <summary>Нижняя грань (шаблон <c>AUAS.B</c>).</summary>
   Bottom,
   /// <summary>Верхняя грань (шаблон <c>AUAS.T</c>).</summary>
   Top,
}

/// <summary>
/// Простой брусовый ТЗА <c>AUAS.B</c> / <c>AUAS.T</c>: ряд из n одинаковых стержней у нижней или верхней
/// грани сечения, включая угловые (автоимя <c>AUAS.B 3d16 c4.0/4.0</c>).
/// </summary>
public sealed class LiraBarReinforcementType
{
   /// <summary>Номер ТЗА (совпадает с номерами в таблице «Элементы - ТЗА»).</summary>
   public int Id { get; init; }
   /// <summary>Код шаблона записи: 21 — AUAS.B, 22 — AUAS.T.</summary>
   public int Kind { get; init; }
   /// <summary>Имя ТЗА.</summary>
   public string Name { get; init; } = "";
   /// <summary>Комментарий пользователя.</summary>
   public string Comment { get; init; } = "";
   /// <summary>Грань, у которой стоит ряд.</summary>
   public LiraBarRebarFace Face { get; init; }
   /// <summary>Число стержней в ряду.</summary>
   public int Count { get; init; }
   /// <summary>Диаметр стержня, мм.</summary>
   public double DiameterMm { get; init; }
   /// <summary>Площадь одного стержня, см².</summary>
   public double BarAreaCm2 { get; init; }
   /// <summary>Привязка a от грани ряда, см (смысл — по <see cref="Binding"/>).</summary>
   public double A { get; init; }
   /// <summary>Привязка a_s от боковой грани, см.</summary>
   public double ASide { get; init; }
   /// <summary>Привязка арматуры.</summary>
   public LiraRebarBinding Binding { get; init; }

   /// <summary>Площадь ряда, см².</summary>
   public double AreaCm2 => Count * BarAreaCm2;
}

/// <summary>ТЗА, который читатель опознал по заголовку, но не разбирает (брус, кольцо, полка…).</summary>
public sealed record LiraRbtSkippedType(int Id, int Kind, string Name, string Comment);

/// <summary>Результат чтения файла ТЗА (.RBT).</summary>
public sealed class LiraRbtFile
{
   /// <summary>Пластинчатые ТЗА по номеру.</summary>
   public IReadOnlyDictionary<int, LiraPlateReinforcementType> PlateTypes { get; init; } =
      new Dictionary<int, LiraPlateReinforcementType>();
   /// <summary>Простые брусовые ТЗА (AUAS.B / AUAS.T) по номеру.</summary>
   public IReadOnlyDictionary<int, LiraBarReinforcementType> BarTypes { get; init; } =
      new Dictionary<int, LiraBarReinforcementType>();
   /// <summary>Прочие ТЗА (не разобраны).</summary>
   public IReadOnlyList<LiraRbtSkippedType> Skipped { get; init; } = [];
   /// <summary>Предупреждения чтения.</summary>
   public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Читатель выгрузки типов заданного армирования (ТЗА) ЛИРА-САПР / ЛИРА-САПФИР (.RBT).
/// <para>
/// Файл — MFC CArchive, в котором один и тот же список ТЗА записан в нескольких версиях формата
/// подряд (записи версий 2, 3, 3, 4). Читаются только записи версии 4: у них есть признак привязки.
/// Заголовок записи: <c>int kind, int 4, int ID, CString имя, CString комментарий, COLORREF, int NL</c>.
/// Разбираются пластинчатые шаблоны (kind 54 — AS/AS_S_*, 52 — AS_mult) и простые брусовые
/// (21 — AUAS.B, 22 — AUAS.T); прочие шаблоны только опознаются по заголовку и попадают в
/// <see cref="LiraRbtFile.Skipped"/>.
/// </para>
/// <para>
/// Тело записи AUAS.B / AUAS.T после заголовка (смещения от конца заголовка): +103 <c>float Ø, мм</c>,
/// +107 <c>float As стержня, см²</c>, +111 <c>float a</c>, +115 <c>float a_s</c>, +119 <c>short n</c>,
/// +124 <c>byte привязка</c> (как у пластин). Сверено с автоименами шести ТЗА балок схемы 1-lin.
/// </para>
/// </summary>
public static class LiraRbtReader
{
   /// <summary>Код шаблона пластинчатого ТЗА AS / AS_S_*.</summary>
   public const int KindPlateSimple = 54;
   /// <summary>Код шаблона составного пластинчатого ТЗА AS_mult.</summary>
   public const int KindPlateMultiple = 52;
   /// <summary>Код шаблона брусового ТЗА AUAS.B (ряд у нижней грани).</summary>
   public const int KindBarBottom = 21;
   /// <summary>Код шаблона брусового ТЗА AUAS.T (ряд у верхней грани).</summary>
   public const int KindBarTop = 22;

   const int RecordVersion = 4;
   static readonly LiraPlateRebarSlot[] SlotOrder =
      [LiraPlateRebarSlot.XT, LiraPlateRebarSlot.XB, LiraPlateRebarSlot.YT, LiraPlateRebarSlot.YB];

   /// <summary>Прочитать файл .RBT.</summary>
   public static LiraRbtFile Read(string path) => Read(File.ReadAllBytes(path));

   /// <summary>Прочитать содержимое файла .RBT.</summary>
   public static LiraRbtFile Read(byte[] data)
   {
      var plates = new Dictionary<int, LiraPlateReinforcementType>();
      var bars = new Dictionary<int, LiraBarReinforcementType>();
      var skipped = new List<LiraRbtSkippedType>();
      var warnings = new List<string>();

      foreach (int offset in FindRecordHeaders(data))
      {
         var r = new Cursor(data, offset);
         int kind = r.Int32();
         r.Int32(); // версия записи (4)
         int id = r.Int32();
         string name = r.CString();
         string comment = r.CString();
         r.Int32(); // COLORREF
         r.Int32(); // закон нелинейного деформирования (−1 — нет)

         if (kind is KindBarBottom or KindBarTop)
         {
            try
            {
               bars[id] = ReadBarBody(r, kind, id, name, comment, warnings);
            }
            catch (FormatException ex)
            {
               warnings.Add($"ТЗА {id} «{name}»: запись не разобрана ({ex.Message}).");
            }
            continue;
         }
         if (kind != KindPlateSimple && kind != KindPlateMultiple)
         {
            skipped.RemoveAll(s => s.Id == id);
            skipped.Add(new LiraRbtSkippedType(id, kind, name, comment));
            continue;
         }
         try
         {
            var type = ReadPlateBody(r, kind, id, name, comment, warnings);
            plates[id] = type;
         }
         catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
         {
            warnings.Add($"ТЗА {id} «{name}»: запись не разобрана ({ex.Message}).");
         }
      }

      if (plates.Count == 0 && bars.Count == 0 && skipped.Count == 0)
         warnings.Add("В файле не найдено ни одной записи ТЗА версии 4.");
      return new LiraRbtFile { PlateTypes = plates, BarTypes = bars, Skipped = skipped, Warnings = warnings };
   }

   static LiraBarReinforcementType ReadBarBody(Cursor r, int kind, int id, string name, string comment,
      List<string> warnings)
   {
      // Размеры и рамка рисунка сечения-образца (B, H, ±H/2, ±B/2 …) — к армированию не относятся.
      r.Skip(103);
      double diameter = r.Single();
      double barArea = r.Single();
      double a = r.Single();
      double aSide = r.Single();
      int count = r.Int16();
      r.Skip(3);
      byte binding = r.Byte();

      if (count is <= 0 or > 1000 || !(diameter is > 0 and < 1000) || !(barArea is > 0 and < 1000))
         throw new FormatException($"n = {count}, Ø = {diameter}, As = {barArea}");
      var bindingValue = Enum.IsDefined(typeof(LiraRebarBinding), (int)binding)
         ? (LiraRebarBinding)binding : LiraRebarBinding.Unknown;
      if (bindingValue == LiraRebarBinding.Unknown)
         warnings.Add($"ТЗА {id} «{name}»: неизвестный код привязки {binding}, a трактуется как расстояние до ц. т.");

      return new LiraBarReinforcementType
      {
         Id = id, Kind = kind, Name = name, Comment = comment,
         Face = kind == KindBarBottom ? LiraBarRebarFace.Bottom : LiraBarRebarFace.Top,
         Count = count, DiameterMm = diameter, BarAreaCm2 = barArea, A = a, ASide = aSide,
         Binding = bindingValue,
      };
   }

   static LiraPlateReinforcementType ReadPlateBody(Cursor r, int kind, int id, string name, string comment,
      List<string> warnings)
   {
      r.Int32(); r.Int16(); r.Int32(); // 0, 0, 3 — назначение неизвестно, всегда одинаковы

      var slots = new Dictionary<LiraPlateRebarSlot, LiraPlateRebarLayer>();
      foreach (var slot in SlotOrder)
      {
         short present = r.Int16();
         if (present == 0) continue;
         if (present != 1) throw new FormatException($"признак слоя {slot} = {present}");
         slots[slot] = ReadLayer(r, slot);
      }

      // Хвост: int 36, float 100, float 16, int 15, 8 байт 0, int 3, int 3, byte 1
      r.Skip(4 + 4 + 4 + 4 + 8 + 4 + 4 + 1);
      byte binding = r.Byte();
      byte symmetry = r.Byte();

      int[] components = [];
      if (kind == KindPlateMultiple)
      {
         r.Int32(); // 2
         components = r.Int32Array();
      }
      r.Int32(); // порядковый номер записи в блоке

      var bindingValue = Enum.IsDefined(typeof(LiraRebarBinding), (int)binding)
         ? (LiraRebarBinding)binding : LiraRebarBinding.Unknown;
      if (bindingValue == LiraRebarBinding.Unknown)
         warnings.Add($"ТЗА {id} «{name}»: неизвестный код привязки {binding}, a трактуется как расстояние до ц. т.");

      if (symmetry == 1)
      {
         // Полная симметрия: хранится только ведущий слой XT
         if (slots.TryGetValue(LiraPlateRebarSlot.XT, out var lead))
            foreach (var slot in SlotOrder)
               if (!slots.ContainsKey(slot))
                  slots[slot] = CopyTo(lead, slot);
      }
      else if (symmetry != 0)
      {
         warnings.Add($"ТЗА {id} «{name}»: вид симметрии {symmetry} не поддержан, взяты только явно заданные слои.");
      }

      return new LiraPlateReinforcementType
      {
         Id = id, Kind = kind, Name = name, Comment = comment,
         Binding = bindingValue, Symmetry = symmetry,
         Layers = SlotOrder.Where(slots.ContainsKey).Select(s => slots[s]).ToList(),
         ComponentIds = components,
      };
   }

   static LiraPlateRebarLayer ReadLayer(Cursor r, LiraPlateRebarSlot slot)
   {
      r.Int32(); // способ (2)
      bool isTotal = r.Byte() == 1;
      string formula = r.CString();
      double total = r.Single();
      double a = r.Single();
      r.Int32(); // 1
      r.Int32(); // число слагаемых (длины массивов ниже)
      int[] counts = r.Int32Array();
      float[] d = r.SingleArray();
      float[] asBar = r.SingleArray();
      float[] s = r.SingleArray();

      var terms = new List<LiraPlateRebarTerm>();
      if (!isTotal)
      {
         int n = Math.Min(Math.Min(counts.Length, d.Length), Math.Min(asBar.Length, s.Length));
         for (int i = 0; i < n; i++)
            terms.Add(new LiraPlateRebarTerm(counts[i], d[i], asBar[i], s[i]));
      }
      return new LiraPlateRebarLayer
      {
         Slot = slot, IsTotalArea = isTotal, Formula = formula,
         TotalAreaCm2 = total, A = a, Terms = terms,
      };
   }

   static LiraPlateRebarLayer CopyTo(LiraPlateRebarLayer src, LiraPlateRebarSlot slot) => new()
   {
      Slot = slot, IsTotalArea = src.IsTotalArea, Formula = src.Formula,
      TotalAreaCm2 = src.TotalAreaCm2, A = src.A, Terms = src.Terms,
   };

   /// <summary>
   /// Смещения заголовков записей версии 4: <c>int kind (1…999), int 4, int ID (1…99999), FF FE FF</c>
   /// с двумя читаемыми строками следом.
   /// </summary>
   static IEnumerable<int> FindRecordHeaders(byte[] b)
   {
      for (int i = 0; i + 15 <= b.Length; i++)
      {
         if (b[i + 12] != 0xFF || b[i + 13] != 0xFE || b[i + 14] != 0xFF) continue;
         if (BitConverter.ToInt32(b, i + 4) != RecordVersion) continue;
         int kind = BitConverter.ToInt32(b, i);
         int id = BitConverter.ToInt32(b, i + 8);
         if (kind is <= 0 or >= 1000 || id is <= 0 or >= 100000) continue;
         var probe = new Cursor(b, i + 12);
         if (!probe.TryCString() || !probe.TryCString()) continue;
         yield return i;
      }
   }

   /// <summary>Последовательное чтение примитивов CArchive (little-endian, без выравнивания).</summary>
   sealed class Cursor(byte[] data, int pos)
   {
      int _pos = pos;

      public void Skip(int n) { Need(n); _pos += n; }
      public byte Byte() { Need(1); return data[_pos++]; }
      public short Int16() { Need(2); var v = BitConverter.ToInt16(data, _pos); _pos += 2; return v; }
      public int Int32() { Need(4); var v = BitConverter.ToInt32(data, _pos); _pos += 4; return v; }
      public float Single() { Need(4); var v = BitConverter.ToSingle(data, _pos); _pos += 4; return v; }

      public int[] Int32Array()
      {
         int n = ArrayLength();
         var a = new int[n];
         for (int i = 0; i < n; i++) a[i] = Int32();
         return a;
      }

      public float[] SingleArray()
      {
         int n = ArrayLength();
         var a = new float[n];
         for (int i = 0; i < n; i++) a[i] = Single();
         return a;
      }

      int ArrayLength()
      {
         int n = Int32();
         if (n < 0 || n > 1000) throw new FormatException($"длина массива {n} по смещению {_pos - 4}");
         return n;
      }

      /// <summary>CString Юникод: <c>FF FE FF</c>, длина в символах (байт; <c>FF</c> → WORD), UTF-16LE.</summary>
      public string CString()
      {
         if (!TryCString(out var s)) throw new FormatException($"ожидалась строка по смещению {_pos}");
         return s;
      }

      public bool TryCString() => TryCString(out _);

      bool TryCString(out string value)
      {
         value = "";
         int p = _pos;
         if (p + 4 > data.Length || data[p] != 0xFF || data[p + 1] != 0xFE || data[p + 2] != 0xFF) return false;
         p += 3;
         int n = data[p++];
         if (n == 0xFF)
         {
            if (p + 2 > data.Length) return false;
            n = BitConverter.ToUInt16(data, p);
            p += 2;
         }
         if (p + 2 * n > data.Length) return false;
         value = Encoding.Unicode.GetString(data, p, 2 * n);
         _pos = p + 2 * n;
         return true;
      }

      void Need(int n)
      {
         if (_pos + n > data.Length) throw new FormatException($"неожиданный конец файла по смещению {_pos}");
      }
   }
}

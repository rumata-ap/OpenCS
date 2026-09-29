using System.Text;

namespace CScore.Import;

/// <summary>
/// Подобранная ЛИРОЙ арматура пластины (оболочки) из файла *.asp.
/// Площади — как в таблице подбора ЛИРЫ: AS1..AS4 в см²/п. м, ASW — в единицах таблицы ЛИРЫ.
/// </summary>
/// <param name="ElementId">Номер КЭ в ЛИРЕ (в файле хранится с нуля, здесь уже +1).</param>
/// <param name="As1">Нижняя (Z−) по X1, см²/м.</param>
/// <param name="As2">Верхняя (Z+) по X1, см²/м.</param>
/// <param name="As3">Нижняя (Z−) по Y1, см²/м.</param>
/// <param name="As4">Верхняя (Z+) по Y1, см²/м.</param>
/// <param name="Asw">Поперечная ASW1 (в единицах таблицы ЛИРЫ); 0 — не требуется.</param>
/// <param name="ThicknessM">Толщина пластины, м.</param>
/// <param name="RebarClass">Класс продольной арматуры (A500 …).</param>
/// <param name="ConcreteClass">Класс бетона (B25 …).</param>
public sealed record LiraAspPlate(
   int ElementId, double As1, double As2, double As3, double As4, double Asw,
   double ThicknessM, string RebarClass, string ConcreteClass);

/// <summary>
/// Строка подбора стержня из *.asp (как «полная» строка таблицы ЛИРЫ по сечению): площади в см².
/// </summary>
/// <param name="Au1">Угловая, левый нижний угол.</param>
/// <param name="Au2">Угловая, правый нижний угол.</param>
/// <param name="Au3">Угловая, левый верхний угол.</param>
/// <param name="Au4">Угловая, правый верхний угол.</param>
/// <param name="As1">Нижняя продольная.</param>
/// <param name="As2">Верхняя продольная.</param>
/// <param name="As3">Боковая у левой грани.</param>
/// <param name="As4">Боковая у правой грани.</param>
/// <param name="Percent">Процент армирования.</param>
/// <param name="Asw1">Поперечная ASW1 (в единицах таблицы ЛИРЫ).</param>
/// <param name="Asw2">Поперечная ASW2 (в единицах таблицы ЛИРЫ).</param>
public sealed record LiraAspBarAreas(
   double Au1, double Au2, double Au3, double Au4,
   double As1, double As2, double As3, double As4,
   double Percent, double Asw1, double Asw2)
{
   /// <summary>Все значения нулевые (блок не заполнен).</summary>
   public bool IsEmpty => Au1 == 0 && Au2 == 0 && Au3 == 0 && Au4 == 0 && As1 == 0 && As2 == 0
                          && As3 == 0 && As4 == 0 && Percent == 0 && Asw1 == 0 && Asw2 == 0;

   /// <summary>
   /// Код сообщения ЛИРЫ, если подбор в сечении не выполнен: ЛИРА пишет его в поле площади со знаком
   /// минус (−274 в ASW1 — «разрушение по наклонной полосе при кручении, п. 8.1.41»). Null — ошибок нет.
   /// </summary>
   public int? FailureCode
   {
      get
      {
         foreach (double v in (double[])[Au1, Au2, Au3, Au4, As1, As2, As3, As4, Percent, Asw1, Asw2])
            if (v < 0) return (int)Math.Round(-v);
         return null;
      }
   }

   /// <summary>Сумма восьми продольных площадей AU1..AU4, AS1..AS4, см².</summary>
   public double LongitudinalSum => Au1 + Au2 + Au3 + Au4 + As1 + As2 + As3 + As4;
}

/// <summary>
/// Сечение стержня в *.asp: в файле два блока — для симметричного (С) и несимметричного (НС)
/// армирования; заполнен тот, который выбран в материалах ЛИРЫ.
/// </summary>
/// <param name="Symmetric">Блок «С».</param>
/// <param name="Unsymmetric">Блок «НС».</param>
public sealed record LiraAspBarSection(LiraAspBarAreas Symmetric, LiraAspBarAreas Unsymmetric)
{
   /// <summary>Армирование симметричное: блок НС пуст.</summary>
   public bool IsSymmetric => Unsymmetric.IsEmpty;

   /// <summary>Действующий блок (С или НС).</summary>
   public LiraAspBarAreas Areas => IsSymmetric ? Symmetric : Unsymmetric;
}

/// <summary>Подобранная ЛИРОЙ арматура стержня из файла *.asp.</summary>
public sealed class LiraAspBar
{
   /// <summary>Номер КЭ в ЛИРЕ (в файле не хранится: порядковый номер записи + 1).</summary>
   public int ElementId { get; init; }
   /// <summary>Класс продольной арматуры.</summary>
   public string RebarClass { get; init; } = "";
   /// <summary>Класс бетона.</summary>
   public string ConcreteClass { get; init; } = "";
   /// <summary>Координаты начала стержня (x, y, z), м.</summary>
   public (double X, double Y, double Z) Start { get; init; }
   /// <summary>Координаты конца стержня (x, y, z), м.</summary>
   public (double X, double Y, double Z) End { get; init; }
   /// <summary>Три привязки арматуры из материалов (a1, a2, a3), см.</summary>
   public (double A1, double A2, double A3) Covers { get; init; }
   /// <summary>Огибающая по сечениям (поэлементный максимум), как её хранит ЛИРА; коды ошибок
   /// (отрицательные значения) в неё не попадают.</summary>
   public LiraAspBarAreas Envelope { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
   /// <summary>Сечения по длине стержня (в порядке ЛИРЫ: 1, 2, …).</summary>
   public IReadOnlyList<LiraAspBarSection> Sections { get; init; } = [];
}

/// <summary>Содержимое файла *.asp (экспорт результатов армирования ЛИРЫ для САПФИР-ЖБК).</summary>
public sealed class LiraAspFile
{
   /// <summary>Сигнатура формата (ARM-SAPFIR_6 binary v.6.0 2022).</summary>
   public string Signature { get; init; } = "";
   /// <summary>Нормы подбора (СП 63.13330.2012/2018 …).</summary>
   public string DesignCode { get; init; } = "";
   /// <summary>Источник усилий (Усилия, РСУ, РСН).</summary>
   public string ForceSource { get; init; } = "";
   /// <summary>Вариант конструирования («Вариант 1: СП 63…»).</summary>
   public string Variant { get; init; } = "";
   /// <summary>Пластины по номеру КЭ.</summary>
   public IReadOnlyDictionary<int, LiraAspPlate> Plates { get; init; } = new Dictionary<int, LiraAspPlate>();
   /// <summary>Стержни по номеру КЭ.</summary>
   public IReadOnlyDictionary<int, LiraAspBar> Bars { get; init; } = new Dictionary<int, LiraAspBar>();
   /// <summary>Число записей продавливания (узлы колонн) — пока не разбираются.</summary>
   public int PunchingCount { get; init; }
   /// <summary>Предупреждения разбора.</summary>
   public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Читатель файла *.asp ЛИРЫ (сигнатура <c>ARM-SAPFIR_6</c>): подобранная арматура пластин и стержней
/// текущего варианта конструирования. Формат разобран по выгрузке ЛИРА-САПФИР 2025 и сверен с таблицами
/// подбора (все 5251 пластины и 278 стержней схемы 1-lin).
/// </summary>
/// <remarks>
/// Раскладка (little-endian, строки CP1251 с нулём в конце):
/// <code>
/// заголовок 272 Б: +0 сигнатура, +32 нормы, +96 u32 число пластин, +100 u32 размер записи пластины (156),
///   +104 источник усилий, +168 вариант, +240 u32 число записей продавливания, +244 u32 их размер (120),
///   +252 u32 число стержней
/// пластины:     +0 u32 номер КЭ − 1, +8 4 узла × xyz, +56 нормаль, +68 класс арматуры, +100 бетон,
///               +132 AS1..AS4, +148 толщина, м
/// продавливание: записи фиксированного размера (узел, точка колонны) — пропускаются
/// стержни:      +8 класс арматуры, +40 бетон, +72 начало xyz, +84 конец xyz, +100 a1 a2 a3,
///               +112 огибающая (11 float) + u32, далее на сечение 88 Б: блок С (11 float), блок НС (11 float);
///               длина записи 160 + 88·n, n в записи не хранится
/// доп. пластин: по 24 Б на пластину: 4 float, float −777 (маркер), float ASW1
/// хвост:        контуры продавливания — не разбираются
/// </code>
/// Каждый блок 11 float: AU1..AU4, AS1..AS4, %, ASW1, ASW2.
/// </remarks>
public static class LiraAspReader
{
   static readonly Encoding Cp1251;

   static LiraAspReader()
   {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      Cp1251 = Encoding.GetEncoding(1251);
   }

   const string SignaturePrefix = "ARM-SAPFIR_";
   const int HeaderSize = 272;
   const int BarHeaderSize = 160;
   const int BarSectionSize = 88;
   const int PlateExtraSize = 24;
   const float PlateExtraMarker = -777f;
   const int MaxBarSections = 64;

   /// <summary>Прочитать файл *.asp.</summary>
   public static LiraAspFile Read(string path) => Read(File.ReadAllBytes(path));

   /// <summary>Разобрать содержимое файла *.asp.</summary>
   /// <exception cref="InvalidDataException">Не файл ARM-SAPFIR или структура не сходится.</exception>
   public static LiraAspFile Read(byte[] data)
   {
      if (data.Length < HeaderSize || Str(data, 0, 30) is not { } sig || !sig.StartsWith(SignaturePrefix, StringComparison.Ordinal))
         throw new InvalidDataException("Файл не является экспортом армирования ЛИРЫ (ARM-SAPFIR).");

      var warnings = new List<string>();
      int nPlates = I32(data, 96), plateSize = I32(data, 100);
      int nPunch = I32(data, 240), punchSize = I32(data, 244);
      int nBars = I32(data, 252);
      if (nPlates < 0 || nPunch < 0 || nBars < 0 || (nPlates > 0 && plateSize < 152) || (nPunch > 0 && punchSize <= 0))
         throw new InvalidDataException("Заголовок *.asp не распознан.");

      long pos = HeaderSize;
      var plateRecs = new List<(int Id, float[] As, double H, string Rs, string Bt)>(nPlates);
      for (int i = 0; i < nPlates; i++, pos += plateSize)
      {
         Need(data, pos, plateSize);
         int p = (int)pos;
         plateRecs.Add((I32(data, p) + 1,
            [F32(data, p + 132), F32(data, p + 136), F32(data, p + 140), F32(data, p + 144)],
            F32(data, p + 148), Str(data, p + 68, 32), Str(data, p + 100, 32)));
      }

      pos += (long)nPunch * punchSize;
      Need(data, pos, 0);

      // Конец блока стержней: начало доп. записей пластин (маркер −777) — если пластины есть.
      long barsEnd = -1;
      if (nPlates > 0)
      {
         // После доп. записей пластин может идти хвост продавливания — ищем первое место,
         // где маркер стоит у всех записей.
         barsEnd = FindPlateExtras(data, pos, nPlates) ?? throw new InvalidDataException(
            "В *.asp не найден блок поперечной арматуры пластин.");
      }

      var bars = new Dictionary<int, LiraAspBar>(nBars);
      for (int i = 0; i < nBars; i++)
      {
         int p = (int)pos;
         Need(data, p, BarHeaderSize + BarSectionSize);
         bool last = i == nBars - 1;
         int n = BarSectionCount(data, p, last, barsEnd);
         if (n <= 0)
            throw new InvalidDataException($"Не удалось определить число сечений стержня {i + 1} в *.asp.");
         var sections = new LiraAspBarSection[n];
         for (int k = 0; k < n; k++)
         {
            int s = p + BarHeaderSize + k * BarSectionSize;
            sections[k] = new LiraAspBarSection(Areas(data, s), Areas(data, s + 44));
         }
         bars[i + 1] = new LiraAspBar
         {
            ElementId = i + 1,
            RebarClass = Str(data, p + 8, 32),
            ConcreteClass = Str(data, p + 40, 32),
            Start = (F32(data, p + 72), F32(data, p + 76), F32(data, p + 80)),
            End = (F32(data, p + 84), F32(data, p + 88), F32(data, p + 92)),
            Covers = (F32(data, p + 100), F32(data, p + 104), F32(data, p + 108)),
            Envelope = Areas(data, p + 112),
            Sections = sections,
         };
         pos += BarHeaderSize + (long)n * BarSectionSize;
      }

      if (barsEnd >= 0 && pos != barsEnd)
         warnings.Add($"Блок стержней *.asp закончился на {pos}, ожидалось {barsEnd}: данные могут быть сдвинуты.");

      var plates = new Dictionary<int, LiraAspPlate>(nPlates);
      for (int i = 0; i < plateRecs.Count; i++)
      {
         var r = plateRecs[i];
         double asw = barsEnd >= 0 ? F32(data, (int)(barsEnd + (long)i * PlateExtraSize + 20)) : 0;
         if (!plates.TryAdd(r.Id, new LiraAspPlate(r.Id, r.As[0], r.As[1], r.As[2], r.As[3], asw, r.H, r.Rs, r.Bt)))
            warnings.Add($"КЭ {r.Id} встречается в *.asp дважды — оставлена первая запись.");
      }

      return new LiraAspFile
      {
         Signature = sig,
         DesignCode = Str(data, 32, 64),
         ForceSource = Str(data, 104, 64),
         Variant = Str(data, 168, 72),
         Plates = plates,
         Bars = bars,
         PunchingCount = nPunch,
         Warnings = warnings,
      };
   }

   /// <summary>
   /// Начало доп. записей пластин: первое смещение ≥ <paramref name="from"/> с шагом 4, где у всех
   /// <paramref name="nPlates"/> записей по 24 Б стоит маркер −777 (+16).
   /// </summary>
   static long? FindPlateExtras(byte[] data, long from, int nPlates)
   {
      long maxStart = data.Length - (long)nPlates * PlateExtraSize;
      for (long s = from; s <= maxStart; s += 4)
      {
         if (F32(data, (int)(s + 16)) != PlateExtraMarker) continue;
         bool ok = true;
         for (int i = 1; i < nPlates && ok; i++)
            ok = F32(data, (int)(s + (long)i * PlateExtraSize + 16)) == PlateExtraMarker;
         if (ok) return s;
      }
      return null;
   }

   /// <summary>
   /// Число сечений стержня: наименьшее n, при котором следующая запись начинается с правдоподобного
   /// заголовка (для последнего стержня — ровно на конце блока, если он известен).
   /// </summary>
   static int BarSectionCount(byte[] data, int p, bool last, long barsEnd)
   {
      for (int n = 1; n <= MaxBarSections; n++)
      {
         long next = p + BarHeaderSize + (long)n * BarSectionSize;
         if (next > data.Length) return -1;
         if (last)
         {
            if (barsEnd >= 0 ? next == barsEnd : EnvelopeMatches(data, p, n)) return n;
         }
         else if (next + BarHeaderSize <= data.Length && LooksLikeBarHeader(data, (int)next))
            return n;
      }
      return -1;
   }

   /// <summary>Огибающая записи совпадает с поэлементным максимумом по n сечениям.</summary>
   static bool EnvelopeMatches(byte[] data, int p, int n)
   {
      for (int j = 0; j < 11; j++)
      {
         float max = 0;
         for (int k = 0; k < n; k++)
         {
            int s = p + BarHeaderSize + k * BarSectionSize + 4 * j;
            max = Math.Max(max, Math.Max(F32(data, s), F32(data, s + 44)));
         }
         if (Math.Abs(F32(data, p + 112 + 4 * j) - max) > 1e-6f) return false;
      }
      return true;
   }

   /// <summary>Заголовок стержня: u32 0 и ненулевой идентификатор в начале, непустые классы арматуры и бетона,
   /// привязки 0…100 см.</summary>
   static bool LooksLikeBarHeader(byte[] data, int p)
   {
      if (I32(data, p) != 0 || I32(data, p + 4) == 0) return false;
      if (!IsClassName(data, p + 8, 32) || !IsClassName(data, p + 40, 32)) return false;
      for (int k = 0; k < 3; k++)
      {
         float a = F32(data, p + 100 + 4 * k);
         if (!(a >= 0 && a <= 100)) return false;
      }
      return true;
   }

   /// <summary>Имя класса материала: непустая строка с нулём в пределах поля, начинается с буквы.</summary>
   static bool IsClassName(byte[] data, int p, int len)
   {
      int z = Array.IndexOf(data, (byte)0, p, len);
      if (z <= p || !char.IsLetter(Cp1251.GetString(data, p, 1)[0])) return false;
      for (int i = p; i < z; i++)
         if (data[i] < 0x20) return false;
      return true;
   }

   static LiraAspBarAreas Areas(byte[] d, int p) => new(
      F32(d, p), F32(d, p + 4), F32(d, p + 8), F32(d, p + 12),
      F32(d, p + 16), F32(d, p + 20), F32(d, p + 24), F32(d, p + 28),
      F32(d, p + 32), F32(d, p + 36), F32(d, p + 40));

   static void Need(byte[] data, long pos, int size)
   {
      if (pos < 0 || pos + size > data.Length)
         throw new InvalidDataException("Файл *.asp обрезан или структура не распознана.");
   }

   static int I32(byte[] d, int p) => BitConverter.ToInt32(d, p);
   static float F32(byte[] d, int p) => BitConverter.ToSingle(d, p);

   static string Str(byte[] d, int p, int len)
   {
      int z = Array.IndexOf(d, (byte)0, p, len);
      return Cp1251.GetString(d, p, (z < 0 ? p + len : z) - p).Trim();
   }
}

/// <summary>Сверка *.asp со схемой ЛИРЫ по номерам и видам КЭ.</summary>
/// <param name="PlatesMatched">Пластины *.asp, найденные в схеме как пластины.</param>
/// <param name="BarsMatched">Стержни *.asp, найденные в схеме как стержни.</param>
/// <param name="Missing">Номера КЭ из *.asp, которых нет в схеме.</param>
/// <param name="KindMismatch">Номера КЭ, вид которых в *.asp и схеме разный (стержень ↔ пластина).</param>
public sealed record LiraAspSchemaMatch(
   int PlatesMatched, int BarsMatched, IReadOnlyList<int> Missing, IReadOnlyList<int> KindMismatch)
{
   /// <summary>Сверить файл с КЭ схемы: номер КЭ в ЛИРЕ и признак «пластина» (иначе стержень).</summary>
   public static LiraAspSchemaMatch Check(LiraAspFile asp, IEnumerable<(int Id, bool IsPlate)> elements)
   {
      var kinds = new Dictionary<int, bool>();
      foreach (var (id, isPlate) in elements)
         kinds.TryAdd(id, isPlate);

      int plates = 0, bars = 0;
      var missing = new List<int>();
      var mismatch = new List<int>();
      foreach (int id in asp.Plates.Keys)
      {
         if (!kinds.TryGetValue(id, out bool isPlate)) missing.Add(id);
         else if (isPlate) plates++;
         else mismatch.Add(id);
      }
      foreach (int id in asp.Bars.Keys)
      {
         if (!kinds.TryGetValue(id, out bool isPlate)) missing.Add(id);
         else if (!isPlate) bars++;
         else mismatch.Add(id);
      }
      missing.Sort();
      mismatch.Sort();
      return new LiraAspSchemaMatch(plates, bars, missing, mismatch);
   }
}

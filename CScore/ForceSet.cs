using System.Collections.Generic;
using System.Linq;

namespace CScore
{
   /// <summary>
   /// Строка набора усилий (аналог ForceDef в GreenSectionPy).
   /// Для типа "bar": N, Mx, My, Vx, Vy, T.
   /// </summary>
   public class LoadItem
   {
      public int    Id  { get; set; }
      public int    Num { get; set; }

      /// <summary>Метка строки (например "1", "sec 3", "Cm max N").</summary>
      public string Label { get; set; } = "";

      public double N  { get; set; }   // продольная сила, кН
      public double Mx { get; set; }   // изгибающий момент Mx, кН·м
      public double My { get; set; }   // изгибающий момент My, кН·м
      public double Vx { get; set; }   // поперечная сила Vx, кН
      public double Vy { get; set; }   // поперечная сила Vy, кН
      public double T  { get; set; }   // крутящий момент T, кН·м

      /// <summary>Номер КЭ во внешней расчётной схеме (ЛИРА, SCAD), к которому относится строка.
      /// Null — строка не привязана к КЭ (ручной ввод, источник без номеров КЭ).</summary>
      public int? SourceElementNum { get; set; }
      /// <summary>Номер сечения КЭ во внешней схеме. Null — неизвестен.</summary>
      public int? SourceSectionNum { get; set; }

      /// <summary>Преобразует строку в структуру Load для расчёта CrossSection.</summary>
      public Load ToLoad() => new Load { N = N, Mx = Mx, My = My };

      public override string ToString() =>
         $"{Num:D3}: N={N:G4}  Mx={Mx:G4}  My={My:G4}  Vx={Vx:G4}  Vy={Vy:G4}  T={T:G4}";
   }

   /// <summary>
   /// Строка н��бора усилий для пластин (аналог ShellForces в GreenSectionPy).
   /// Погонные усилия и моменты на единицу ширины.
   /// </summary>
   public class ShellLoadItem
   {
      public int    Id    { get; set; }
      public int    Num   { get; set; }
      public string Label { get; set; } = "";

      public double Nx  { get; set; }   // нормальное погонное усилие по x, кН/м
      public double Ny  { get; set; }   // нормальное погонное усилие по y, кН/м
      public double Nxy { get; set; }   // касательное погонное усилие, кН/м
      public double Mx  { get; set; }   // изгибающий погонный момент Mx, кН·м/м
      public double My  { get; set; }   // изгибающий погонный момент My, кН·м/м
      public double Mxy { get; set; }   // крутящий погонный момент, кН·м/м
      public double Qx  { get; set; }   // поперечная погонная сила Qx, кН/м
      public double Qy  { get; set; }   // поперечная погонная сила Qy, кН/м

      /// <summary>Импортированное нормальное напряжение σx, кПа. Null — не источник-напряжение (Nx уже погонное усилие).</summary>
      public double? SigmaX { get; set; }
      /// <summary>Импортированное нормальное напряжение σy, кПа.</summary>
      public double? SigmaY { get; set; }
      /// <summary>Импортированное касательное напряжение τxy, кПа.</summary>
      public double? TauXY  { get; set; }

      /// <summary>Номер КЭ во внешней расчётной схеме (ЛИРА, SCAD), к которому относится строка.
      /// Null — строка не привязана к КЭ (ручной ввод, источник без номеров КЭ).</summary>
      public int? SourceElementNum { get; set; }
      /// <summary>Номер сечения (точки выдачи) КЭ во внешней схеме. Null — неизвестен или задан
      /// не числом (например, «Центр» у SCAD).</summary>
      public int? SourceSectionNum { get; set; }

      /// <summary>
      /// Возвращает погонные Nx/Ny/Nxy: если задан хотя бы один из SigmaX/SigmaY/TauXY — считает
      /// все три компонента заново как σ·h (кПа·м = кН/м; отсутствующий компонент = 0 напряжения),
      /// иначе возвращает хранимые Nx/Ny/Nxy как есть.
      /// ВАЖНО: пока Sigma заполнены, они — источник правды. Любое ручное значение Nx/Ny/Nxy
      /// в строке будет молча перезаписано этим методом при следующем вызове (кнопка «Напряжения
      /// → усилия» в наборе, автопересчёт в расчётной задаче) — редактирование Nx напрямую в этом
      /// состоянии имеет смысл только как временный просмотр, не как постоянное переопределение.
      /// UI делает Nx/Ny/Nxy визуально недоступными для правки, пока Sigma заданы.
      /// </summary>
      public (double Nx, double Ny, double Nxy) ResolveN(double thicknessM)
      {
         if (SigmaX is null && SigmaY is null && TauXY is null)
            return (Nx, Ny, Nxy);
         return ((SigmaX ?? 0) * thicknessM, (SigmaY ?? 0) * thicknessM, (TauXY ?? 0) * thicknessM);
      }
   }

   /// <summary>Чтение строк наборов усилий из хранилища (БД). Методы вызываются из любого потока.</summary>
   public interface IForceSetRowSource
   {
      /// <summary>Строки набора в порядке номеров.</summary>
      (List<LoadItem> Items, List<ShellLoadItem> ShellItems) LoadRows(int setId);

      /// <summary>Число строк набора по номерам КЭ: строки пластин (<paramref name="shell"/>) или стержней.</summary>
      ForceSetElementStats ElementStats(int setId, bool shell);

      /// <summary>Строки стержней набора по КЭ с номерами от <paramref name="fromElem"/> до <paramref name="toElem"/>
      /// в порядке номеров строк; оба null — строки без номера КЭ.</summary>
      List<LoadItem> LoadBarRows(int setId, int? fromElem, int? toElem);

      /// <summary>Строки пластин набора по КЭ с номерами от <paramref name="fromElem"/> до <paramref name="toElem"/>
      /// в порядке номеров строк; оба null — строки без номера КЭ.</summary>
      List<ShellLoadItem> LoadShellRows(int setId, int? fromElem, int? toElem);
   }

   /// <summary>Число строк набора по номерам КЭ.</summary>
   /// <param name="ByElement">Номер КЭ → число строк.</param>
   /// <param name="WithoutElement">Строк без номера КЭ.</param>
   public sealed record ForceSetElementStats(IReadOnlyDictionary<int, int> ByElement, int WithoutElement)
   {
      /// <summary>Всего строк.</summary>
      public int Total => WithoutElement + ByElement.Values.Sum();

      /// <summary>Есть строки с номером КЭ.</summary>
      public bool HasElementRows => ByElement.Count > 0;

      /// <summary>Статистика по номерам КЭ строк.</summary>
      public static ForceSetElementStats Of(IEnumerable<int?> elementNums)
      {
         var byElement = new Dictionary<int, int>();
         int without = 0;
         foreach (int? n in elementNums)
         {
            if (n is int num) byElement[num] = byElement.GetValueOrDefault(num) + 1;
            else without++;
         }
         return new ForceSetElementStats(byElement, without);
      }
   }

   /// <summary>
   /// Именованный набор усилий (аналог ForceSetDef в GreenSectionPy).
   /// Имя набора кодирует вид нагрузки: "G: ...", "L: ...", "Q: ...", "A: ...".
   /// </summary>
   public class ForceSet
   {
      public int     Id          { get; set; }
      public int     Num         { get; set; }
      public string  Tag         { get; set; } = "";
      public string? Description { get; set; }

      /// <summary>Тип набора: "bar" или "shell".</summary>
      public string Kind { get; set; } = "bar";

      /// <summary>Источник усилий: null (ручной ввод) | "fea" (результат МКЭ-расчёта).</summary>
      public string? SourceType       { get; set; }

      /// <summary>FK → fem_schemas.id. Null для ручного ввода.</summary>
      public int?    SourceSchemaId   { get; set; }

      /// <summary>Tag конструктивного элемента в расчётной схеме.</summary>
      public string? SourceElementTag { get; set; }

      /// <summary>FK → fem_member_groups.id. Набор усилий группы конструктивных элементов
      /// (импорт ЛИРА/SCAD на группу). Для ручного набора равен null.</summary>
      public int?    SourceMemberId   { get; set; }

      /// <summary>FK → fem_members.id. Набор усилий одного конструктивного элемента: усилия стержня
      /// из расчёта OpenSees или импорт на элемент (в отличие от SourceMemberId, который указывает
      /// на группу). Заполнено не более одного из двух.</summary>
      public int?    SourceElementId  { get; set; }

      // ── Строки набора: ленивая загрузка ─────────────────────────────────────────────────────
      // Наборы РСУ из МКЭ-программ бывают в миллионы строк. При открытии проекта читаются только
      // заголовки; строки — из БД при первом обращении к Items/ShellItems и могут быть выгружены
      // (UnloadRows), когда не нужны. Набор без RowSource (новый, импортированный, тестовый) —
      // обычный набор со строками в памяти.

      readonly object _rowsLock = new();
      List<LoadItem>?      _items      = [];
      List<ShellLoadItem>? _shellItems = [];
      int _storedRowCount;
      ForceSetElementStats? _barStats, _shellStats;

      /// <summary>Строки стержней (при первом обращении читаются из БД).</summary>
      public List<LoadItem> Items
      {
         get { EnsureRows(); return _items!; }
         set { lock (_rowsLock) { EnsureRowsLocked(); _items = value; } }
      }

      /// <summary>Строки пластин (при первом обращении читаются из БД).</summary>
      public List<ShellLoadItem> ShellItems
      {
         get { EnsureRows(); return _shellItems!; }
         set { lock (_rowsLock) { EnsureRowsLocked(); _shellItems = value; } }
      }

      /// <summary>Источник строк в БД; null — строки только в памяти.</summary>
      public IForceSetRowSource? RowSource { get; private set; }

      /// <summary>Строки в памяти (загружены или набор не связан с БД).</summary>
      public bool RowsLoaded => _items != null;

      /// <summary>Строки нужны в памяти постоянно (открыты в редакторе набора): не выгружаются.</summary>
      public bool RowsPinned { get; private set; }

      /// <summary>Число строк (стержней и пластин) без загрузки строк.</summary>
      public int RowCount
      {
         get
         {
            lock (_rowsLock)
               return _items != null ? _items.Count + _shellItems!.Count : _storedRowCount;
         }
      }

      /// <summary>
      /// Связывает набор с БД: строки не загружены, <paramref name="rowCount"/> — их число в БД.
      /// Вызывает слой БД при чтении заголовков наборов.
      /// </summary>
      public void AttachRowSource(IForceSetRowSource source, int rowCount)
      {
         lock (_rowsLock)
         {
            RowSource = source;
            _storedRowCount = rowCount;
            _items = null;
            _shellItems = null;
            _barStats = _shellStats = null;
         }
      }

      /// <summary>Связывает с БД набор, строки которого в памяти и только что записаны, — чтобы их можно
      /// было выгрузить. Строки остаются в памяти.</summary>
      public void BindRowSource(IForceSetRowSource source)
      {
         lock (_rowsLock)
         {
            RowSource = source;
            _barStats = _shellStats = null;
         }
      }

      /// <summary>Загружает строки из БД, если их ещё нет в памяти.</summary>
      public void EnsureRows()
      {
         if (_items != null) return;
         lock (_rowsLock) EnsureRowsLocked();
      }

      void EnsureRowsLocked()
      {
         if (_items != null) return;
         var (items, shellItems) = RowSource!.LoadRows(Id);
         _shellItems = shellItems;
         _items = items;
      }

      /// <summary>Строки нужны постоянно (редактор набора): загружает и запрещает выгрузку.</summary>
      public void PinRows()
      {
         lock (_rowsLock)
         {
            EnsureRowsLocked();
            RowsPinned = true;
         }
      }

      /// <summary>
      /// Выгружает строки из памяти (следующее обращение прочитает их из БД заново). Не выгружает набор
      /// без БД, с несохранёнными изменениями или закреплённый редактором; false — строки остались.
      /// Ссылки на списки строк, полученные раньше, остаются рабочими у тех, кто их держит.
      /// </summary>
      public bool UnloadRows()
      {
         lock (_rowsLock)
         {
            if (RowSource == null || IsModified || RowsPinned || Id == 0 || _items == null) return false;
            _storedRowCount = _items.Count + _shellItems!.Count;
            _barStats = _shellStats = null;
            _items = null;
            _shellItems = null;
            return true;
         }
      }

      /// <summary>
      /// Число строк по номерам КЭ (стержней или пластин) — без загрузки строк: у выгруженного набора
      /// считается в БД и запоминается.
      /// </summary>
      public ForceSetElementStats ElementStats(bool shell)
      {
         lock (_rowsLock)
         {
            if (_items != null)
               return shell ? ForceSetElementStats.Of(_shellItems!.Select(i => i.SourceElementNum))
                            : ForceSetElementStats.Of(_items.Select(i => i.SourceElementNum));
            return shell ? _shellStats ??= RowSource!.ElementStats(Id, shell: true)
                         : _barStats  ??= RowSource!.ElementStats(Id, shell: false);
         }
      }

      /// <summary>
      /// Строки стержней по КЭ с номерами от <paramref name="fromElem"/> до <paramref name="toElem"/>; оба null —
      /// строки без номера КЭ. Набор целиком не загружается: у выгруженного набора читаются только эти строки.
      /// </summary>
      public List<LoadItem> BarRowsOfElements(int? fromElem, int? toElem)
      {
         lock (_rowsLock)
            if (_items != null)
               return _items.Where(i => InElementRange(i.SourceElementNum, fromElem, toElem)).ToList();
         return RowSource!.LoadBarRows(Id, fromElem, toElem);
      }

      /// <summary>
      /// Строки пластин по КЭ с номерами от <paramref name="fromElem"/> до <paramref name="toElem"/>; оба null —
      /// строки без номера КЭ. Набор целиком не загружается: у выгруженного набора читаются только эти строки.
      /// </summary>
      public List<ShellLoadItem> ShellRowsOfElements(int? fromElem, int? toElem)
      {
         lock (_rowsLock)
            if (_items != null)
               return _shellItems!.Where(i => InElementRange(i.SourceElementNum, fromElem, toElem)).ToList();
         return RowSource!.LoadShellRows(Id, fromElem, toElem);
      }

      static bool InElementRange(int? elemNum, int? fromElem, int? toElem) =>
         fromElem is int from && toElem is int to ? elemNum >= from && elemNum <= to : elemNum == null;

      /// <summary>Признак изменений в памяти, ещё не записанных через SaveAll.</summary>
      public bool IsModified { get; set; }

      public override string ToString() => $"{Num:D3}#ForceSet : {Tag}";
   }
}

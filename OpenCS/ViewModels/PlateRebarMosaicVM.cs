using System.Globalization;
using System.IO;
using System.Windows.Media;
using CScore;
using CScore.Fem;
using CScore.Import;
using CScore.PlateRebar;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Что показывает мозаика.</summary>
public enum PlateRebarMosaicSourceKind
{
   None, Selected, Assigned, Difference, SelectedBars, AssignedBars, DifferenceBars, Forces, Utilization,
   /// <summary>Раскладка армирования OpenCS (фон + зоны конструктивных элементов).</summary>
   Layout,
   /// <summary>Раскладка OpenCS минус подобранная арматура.</summary>
   LayoutDifference,
}

/// <summary>Пункт списка источников мозаики.</summary>
public sealed record PlateRebarMosaicSourceOption(PlateRebarMosaicSourceKind Kind, string Label);

/// <summary>Пункт второго списка мозаики: набор усилий (<see cref="ForceSet"/>) или проверка
/// (<see cref="PlateRebarMosaicVM.CheckInfo"/>).</summary>
public sealed record PlateRebarMosaicSubjectOption(object Subject, string Label);

/// <summary>Пункт списка компонент мозаики. <paramref name="Component"/> — <see cref="PlateRebarMosaicComponent"/>,
/// <see cref="BarRebarComponent"/>, <see cref="ShellForceComponent"/>, <see cref="BarForceComponent"/> или ключ
/// источника армирования проверки.</summary>
public sealed record PlateRebarMosaicComponentOption(object Component, string Label);

/// <summary>Пункт списка «какую строку КЭ показывать».</summary>
public sealed record PlateRebarMosaicAggregateOption(ForceRowAggregate Aggregate, string Label);

/// <summary>Строка легенды мозаики: цвет, диапазон и число КЭ.</summary>
public sealed record PlateRebarMosaicLegendItem(Brush Brush, string Label, int Count);

/// <summary>Раскраска КЭ: цвет по тегу КЭ; КЭ без данных в словарь не попадают.</summary>
/// <param name="Bars">Раскрашены стержни (иначе — пластины).</param>
public sealed record PlateRebarMosaicColoring(IReadOnlyDictionary<string, Color> ColorByTag, bool Bars = false);

/// <summary>
/// Настройки и легенда мозаики по КЭ (общие для 3D-вида схемы и редактора пластинчатого КонЭ):
/// армирование пластин (ASP и RBT ЛИРЫ, раскладка OpenCS) и стержней (ASP), импортированные усилия наборов схемы, коэффициент
/// использования из результата проверки по КЭ. Вид, компонента, пороги шкалы.
/// </summary>
public sealed class PlateRebarMosaicVM : ViewModelBase
{
   /// <summary>Цвет КЭ, где программа-источник не смогла подобрать арматуру либо проверка не прошла
   /// без конечного коэффициента.</summary>
   public static Color FailureColor => Colors.Black;
   /// <summary>Цвет КЭ, не проверенных проверкой (нет усилий, сечения, армирования).</summary>
   public static Color NotCheckedColor => Color.FromRgb(150, 120, 170);
   /// <summary>Цвет полосы «0».</summary>
   public static Color ZeroColor => Color.FromRgb(250, 250, 250);

   /// <summary>Пороги шкалы коэффициента использования по умолчанию.</summary>
   public static IReadOnlyList<double> UtilizationThresholds { get; } = [0.25, 0.5, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 2.0];

   const int FailedWithoutUtilization = 1, NotChecked = 2;

   enum Palette { Rebar, Difference, Forces, Utilization }

   static readonly Color[] SequentialStops =
   [
      Color.FromRgb(49, 54, 149), Color.FromRgb(69, 117, 180), Color.FromRgb(116, 173, 209),
      Color.FromRgb(171, 217, 233), Color.FromRgb(254, 224, 144), Color.FromRgb(253, 174, 97),
      Color.FromRgb(244, 109, 67), Color.FromRgb(215, 48, 39), Color.FromRgb(165, 0, 38),
   ];
   static readonly Color[] DeficitStops = [Color.FromRgb(165, 0, 38), Color.FromRgb(252, 187, 161)];
   static readonly Color[] ReserveStops = [Color.FromRgb(199, 233, 192), Color.FromRgb(0, 109, 44)];
   static readonly Color[] NegativeForceStops = [Color.FromRgb(49, 54, 149), Color.FromRgb(171, 217, 233)];
   static readonly Color[] PositiveForceStops = [Color.FromRgb(254, 224, 144), Color.FromRgb(165, 0, 38)];
   static readonly Color[] PassedStops = [Color.FromRgb(26, 152, 80), Color.FromRgb(166, 217, 106), Color.FromRgb(254, 224, 139)];
   static readonly Color[] NotPassedStops = [Color.FromRgb(244, 109, 67), Color.FromRgb(128, 0, 38)];

   /// <summary>Проверка схемы с результатом по КЭ.</summary>
   /// <param name="Check">Проверка.</param>
   /// <param name="Label">Имя в списке.</param>
   /// <param name="LoadJson">Чтение <c>DataJson</c> результата (по требованию: он может быть большим).</param>
   public sealed record CheckInfo(FemCheck Check, string Label, Func<string?> LoadJson);

   /// <summary>Значения мозаики на КЭ и то, как их красить.</summary>
   sealed record Field(
      Dictionary<string, PlateRebarValue> Values, bool Bars, bool Diverging, Palette Palette,
      IReadOnlyList<double>? DefaultThresholds = null);

   readonly Action<string>? _warn;
   IPlateRebarFieldSource? _selected, _assigned, _layout;
   IBarRebarFieldSource? _selectedBars, _assignedBars;
   string? _selectedFile, _assignedFile;
   IReadOnlyDictionary<string, double> _thicknessByTag = new Dictionary<string, double>();
   IReadOnlyList<ForceSet> _forceSets = [];
   IReadOnlyList<CheckInfo> _checks = [];
   readonly Dictionary<int, IReadOnlyList<FemCheckElementResult>> _checkResults = [];
   readonly Dictionary<int, IReadOnlyList<FemCheckRowResult>> _checkRows = [];
   PlateRebarMosaicScale? _scale;
   Color[] _bandColors = [];
   readonly Dictionary<string, string> _thresholdsByKey = [];
   Field? _field;

   /// <summary>Настройки изменились — раскраску нужно пересчитать.</summary>
   public event EventHandler? Changed;

   /// <param name="warn">Куда сообщать об ошибках чтения файлов.</param>
   public PlateRebarMosaicVM(Action<string>? warn = null)
   {
      _warn = warn;
      AggregateOptions =
      [
         new(ForceRowAggregate.MaxAbs, Loc.S("MosaicAggregateMaxAbs")),
         new(ForceRowAggregate.Max, Loc.S("MosaicAggregateMax")),
         new(ForceRowAggregate.Min, Loc.S("MosaicAggregateMin")),
      ];
      _selectedAggregate = AggregateOptions[0];
   }

   /// <summary>Показывать только мозаики пластин (редактор пластинчатого элемента).</summary>
   public bool ShellOnly { get; init; }

   /// <summary>Теги КЭ, которыми ограничен вид; наборы усилий без строк по этим КЭ не предлагаются.
   /// Null — вся схема.</summary>
   public IReadOnlySet<string>? ScopeTags { get; set; }

   /// <summary>Отбор проверок для списка; null — все проверки схемы.</summary>
   public Func<FemCheck, bool>? CheckFilter { get; set; }

   public IReadOnlyList<PlateRebarMosaicSourceOption> SourceOptions { get; private set; } = [];
   public IReadOnlyList<PlateRebarMosaicSubjectOption> SubjectOptions { get; private set; } = [];
   public IReadOnlyList<PlateRebarMosaicComponentOption> ComponentOptions { get; private set; } = [];
   public IReadOnlyList<PlateRebarMosaicAggregateOption> AggregateOptions { get; }
   public IReadOnlyList<PlateRebarMosaicLegendItem> Legend { get; private set; } = [];

   /// <summary>Есть что показывать: файлы армирования, наборы усилий по КЭ или результаты проверок.</summary>
   public bool HasData => SourceOptions.Count > 1;

   PlateRebarMosaicSourceKind Kind => SelectedSource?.Kind ?? PlateRebarMosaicSourceKind.None;

   /// <summary>Нужен второй список — набор усилий или проверка.</summary>
   public bool HasSubject => Kind is PlateRebarMosaicSourceKind.Forces or PlateRebarMosaicSourceKind.Utilization;

   /// <summary>Нужен выбор строки КЭ (усилия: у КЭ в наборе может быть несколько строк).</summary>
   public bool HasAggregate => Kind == PlateRebarMosaicSourceKind.Forces;

   /// <summary>У мозаики есть значения по сечениям стержней — можно рисовать эпюры на схеме
   /// (усилия стержней, подобранная арматура стержней).</summary>
   public bool HasBarDiagrams => !ShellOnly && (SelectedComponent?.Component is BarForceComponent or BarRebarComponent
      || Kind == PlateRebarMosaicSourceKind.Utilization && SelectedSubject?.Subject is CheckInfo c && !FemCheckContext.IsPlate(c.Check));

   bool _showBarDiagrams = true;
   /// <summary>Рисовать эпюры на стержнях схемы.</summary>
   public bool ShowBarDiagrams
   {
      get => _showBarDiagrams;
      set
      {
         if (_showBarDiagrams == value) return;
         _showBarDiagrams = value;
         OnPropertyChanged();
         RaiseChanged();
      }
   }

   /// <summary>
   /// Значения текущей мозаики по сечениям стержней: точки (доля длины КЭ от начального узла, значение)
   /// по тегу КЭ. Null — эпюры выключены или у мозаики нет значений по сечениям.
   /// </summary>
   /// <param name="plane">Плоскость ординат: у изгиба и поперечной силы — плоскость их действия.</param>
   public Dictionary<string, IReadOnlyList<(double T, double V)>>? BarProfiles(IEnumerable<string> barTags, out BarDiagramPlane plane)
   {
      plane = BarDiagramPlane.Z1;
      if (!ShowBarDiagrams || !HasBarDiagrams) return null;
      var result = new Dictionary<string, IReadOnlyList<(double T, double V)>>(StringComparer.Ordinal);
      switch (SelectedComponent!.Component)
      {
         case BarForceComponent force when SelectedSubject?.Subject is ForceSet set:
            // My и Vx действуют в плоскости X1Y1 стержня (Mz и Qy ЛИРЫ), остальное — в X1Z1.
            if (force is BarForceComponent.My or BarForceComponent.Vx) plane = BarDiagramPlane.Y1;
            var byNum = BarDiagram.ForceProfiles(set, force, SelectedAggregate.Aggregate);
            foreach (string tag in barTags)
               if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                   && byNum.TryGetValue(n, out var profile))
                  result[tag] = profile;
            break;

         case BarRebarComponent rebar when BarRebarSource() is { } source:
            foreach (string tag in barTags)
               if (BarDiagram.RebarProfile(source.GetSections(tag, rebar)) is { Count: > 0 } profile)
                  result[tag] = profile;
            break;

         case string rebarSource when Kind == PlateRebarMosaicSourceKind.Utilization
                                      && SelectedSubject?.Subject is CheckInfo check:
            var util = BarDiagram.UtilizationProfiles(CheckRows(check), rebarSource);
            foreach (string tag in barTags)
               if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                   && util.TryGetValue(n, out var profile))
                  result[tag] = profile;
            break;
      }
      return result;
   }

   PlateRebarMosaicSourceOption? _selectedSource;
   public PlateRebarMosaicSourceOption? SelectedSource
   {
      get => _selectedSource;
      set
      {
         if (Equals(_selectedSource, value)) return;
         _selectedSource = value;
         OnPropertyChanged();
         RefreshSubjects();
         RaiseChanged();
      }
   }

   PlateRebarMosaicSubjectOption? _selectedSubject;
   /// <summary>Набор усилий или проверка.</summary>
   public PlateRebarMosaicSubjectOption? SelectedSubject
   {
      get => _selectedSubject;
      set
      {
         if (Equals(_selectedSubject, value)) return;
         _selectedSubject = value;
         OnPropertyChanged();
         RefreshComponents();
         RaiseChanged();
      }
   }

   PlateRebarMosaicComponentOption? _selectedComponent;
   public PlateRebarMosaicComponentOption? SelectedComponent
   {
      get => _selectedComponent;
      set
      {
         if (Equals(_selectedComponent, value)) return;
         _selectedComponent = value;
         OnPropertyChanged();
         OnPropertyChanged(nameof(IsActive));
         OnPropertyChanged(nameof(HasBarDiagrams));
         OnPropertyChanged(nameof(ThresholdsText));
         RaiseChanged();
      }
   }

   PlateRebarMosaicAggregateOption _selectedAggregate;
   public PlateRebarMosaicAggregateOption SelectedAggregate
   {
      get => _selectedAggregate;
      set
      {
         if (value == null || Equals(_selectedAggregate, value)) return;
         _selectedAggregate = value;
         OnPropertyChanged();
         RaiseChanged();
      }
   }

   /// <summary>Пороги шкалы через «;»; пусто — автошкала. Свои для армирования, разности,
   /// каждой компоненты усилий и коэффициента использования.</summary>
   public string ThresholdsText
   {
      get => _thresholdsByKey.GetValueOrDefault(ThresholdsKey(), "");
      set
      {
         value ??= "";
         if (ThresholdsText == value) return;
         _thresholdsByKey[ThresholdsKey()] = value;
         OnPropertyChanged();
         RaiseChanged();
      }
   }

   string ThresholdsKey() => Kind switch
   {
      PlateRebarMosaicSourceKind.Forces => "F:" + SelectedComponent?.Component,
      PlateRebarMosaicSourceKind.SelectedBars or PlateRebarMosaicSourceKind.AssignedBars =>
         "B:" + SelectedComponent?.Component,
      PlateRebarMosaicSourceKind.DifferenceBars => "BD:" + SelectedComponent?.Component,
      PlateRebarMosaicSourceKind.Utilization => "U",
      PlateRebarMosaicSourceKind.Difference => "D",
      PlateRebarMosaicSourceKind.LayoutDifference => "LD",
      _ => "R",
   };

   string _legendTitle = "";
   public string LegendTitle { get => _legendTitle; private set { _legendTitle = value; OnPropertyChanged(); } }

   string _hoverText = "";
   /// <summary>Значение КЭ под курсором («КЭ 4333: 363»); пусто — курсор не над КЭ.</summary>
   public string HoverText { get => _hoverText; private set { if (_hoverText == value) return; _hoverText = value; OnPropertyChanged(); } }

   /// <summary>Мозаика включена и есть что показывать.</summary>
   public bool IsActive => Kind != PlateRebarMosaicSourceKind.None && SelectedComponent != null;

   /// <summary>Данные мозаики схемы: файлы армирования и толщины КЭ (можно готовить в фоновом потоке),
   /// наборы усилий и проверки (только в UI-потоке — <see cref="WithProject"/>).</summary>
   public sealed record Data(
      IPlateRebarFieldSource? Selected, string? SelectedFile,
      IPlateRebarFieldSource? Assigned, string? AssignedFile,
      IReadOnlyList<(string File, string Error)> Errors,
      string Key = "")
   {
      /// <summary>Подобранное армирование стержней (ASP); null — файла нет или в нём нет стержней.</summary>
      public IBarRebarFieldSource? SelectedBars { get; init; }
      /// <summary>Заданное армирование стержней (ТЗА из RBT); null — файла нет либо стержням не назначены
      /// разобранные брусовые ТЗА.</summary>
      public IBarRebarFieldSource? AssignedBars { get; init; }
      /// <summary>Раскладка армирования OpenCS на КЭ плоских конструктивных элементов; null — таких КЭ нет.</summary>
      public IPlateRebarFieldSource? Layout { get; init; }
      /// <summary>КЭ сетки схемы (для наложения раскладки OpenCS).</summary>
      public IReadOnlyList<FemElement> MeshElements { get; init; } = [];
      /// <summary>Толщина пластинчатого КЭ по тегу, м (из схемы, иначе из подбора ASP).</summary>
      public IReadOnlyDictionary<string, double> ThicknessByTag { get; init; } = new Dictionary<string, double>();
      /// <summary>Наборы усилий схемы со строками по КЭ.</summary>
      public IReadOnlyList<ForceSet> ForceSets { get; init; } = [];
      /// <summary>Проверки схемы, у которых есть результат.</summary>
      public IReadOnlyList<CheckInfo> Checks { get; init; } = [];

      /// <summary>Дополнить наборами усилий и проверками схемы. Только в UI-потоке: читает коллекции проекта.</summary>
      public Data WithProject(DatabaseService db, int schemaId)
      {
         var sets = db.ForceSets
            .Where(fs => fs.SourceSchemaId == schemaId
                         && (ElementForceField.HasShellRows(fs) || ElementForceField.HasBarRows(fs)))
            .OrderBy(fs => fs.Tag, StringComparer.CurrentCulture)
            .ToList();

         IReadOnlyList<FemMember>? members = null;
         var (layout, layoutKey) = ReadLayout(db, schemaId, members ??= db.GetFemMembers(schemaId));

         var checks = new List<CheckInfo>();
         var withResult = db.FemChecks.Where(c => c.SchemaId == schemaId && c.ResultId != null).ToList();
         if (withResult.Count > 0)
         {
            var groups = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.MemberGroups;
            foreach (var c in withResult)
            {
               string? target = c.TargetsElement
                  ? (members ??= db.GetFemMembers(schemaId)).FirstOrDefault(m => m.Id == c.ElementId)?.ElemTag
                  : groups?.FirstOrDefault(g => g.Id == c.MemberId)?.Tag;
               int id = c.Id;
               checks.Add(new CheckInfo(c, target == null ? c.DisplayTag : $"{target} — {c.DisplayTag}",
                  () => db.GetCalcResultByFemCheck(id)?.DataJson));
            }
         }

         string key = Key
            + "|" + string.Join(',', sets.Select(s => $"{s.Id}:{s.ShellItems.Count}:{s.Items.Count}"))
            + "|" + string.Join(',', checks.Select(c => $"{c.Check.Id}:{c.Check.ResultId}"))
            + "|" + layoutKey;
         return this with { ForceSets = sets, Checks = checks, Layout = layout, Key = key };
      }

      /// <summary>Раскладка OpenCS схемы и её отпечаток (зоны регионов, слои и толщины сечений элементов).</summary>
      (IPlateRebarFieldSource? Source, string Key) ReadLayout(DatabaseService db, int schemaId, IReadOnlyList<FemMember> members)
      {
         var planar = members.Where(m => m.ElemType == "shell" && m.PlanarRegionId != null).ToList();
         if (planar.Count == 0) return (null, "");

         var regions = db.GetPlanarRegions(schemaId);
         var sections = new Dictionary<int, PlateSection>();
         foreach (var s in db.PlateSections) sections.TryAdd(s.Id, s);
         var resolver = new PlateLayoutResolver(members, regions, sections.GetValueOrDefault, MeshElements, db.GetFemMeshNodes(schemaId));
         if (!resolver.HasElements) return (null, "");

         var key = new System.Text.StringBuilder();
         foreach (var r in regions)
            key.Append(r.Id).Append(':').Append(System.Text.Json.JsonSerializer.Serialize(r.RebarZones)).Append(';');
         foreach (var m in planar)
         {
            key.Append(m.ElemTag).Append(':').Append(m.PlateSectionId);
            if (m.PlateSectionId is int id && sections.TryGetValue(id, out var section))
               key.Append(':').Append(section.H.ToString("R", CultureInfo.InvariantCulture))
                  .Append(':').Append(PlateRebarLayoutFingerprint.Compute(section.RebarLayers));
            key.Append(';');
         }
         string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key.ToString())));
         return (new PlateLayoutRebarSource(resolver), hash);
      }
   }

   /// <summary>Прочитать файлы армирования схемы (ASP/RBT ЛИРЫ), ТЗА и толщины КЭ. Без обращения к UI.</summary>
   public static Data Read(DatabaseService db, int schemaId)
   {
      IPlateRebarFieldSource? selected = null, assigned = null;
      IBarRebarFieldSource? selectedBars = null, assignedBars = null;
      string? selectedFile = null, assignedFile = null;
      var errors = new List<(string, string)>();
      var key = new System.Text.StringBuilder();
      var elements = db.GetFemMeshElements(schemaId);
      var shells = elements.Where(e => e.ElemType == "shell").ToList();
      // Номера ТЗА КЭ обновляются отдельной командой — файл при этом тот же.
      var typeHash = new HashCode();
      foreach (var e in elements) { typeHash.Add(e.ElemTag); typeHash.Add(e.ReinforcementTypeIds); typeHash.Add(e.LocalAxisAngleDeg); }
      key.Append(typeHash.ToHashCode()).Append('|');
      var thickness = new Dictionary<string, double>(StringComparer.Ordinal);
      foreach (var e in shells)
         if (e.ThicknessM is double h && h > 0) thickness[e.ElemTag.Trim()] = h;

      // Подбор SCAD (выгрузка плагина) — у схем SCAD вместо ASP; толщин в нём нет — только свои толщины КЭ.
      if (db.GetFemSchemaSourceType(schemaId) == "scad")
      {
         ScadSelectedRebarFile? selectedScad = null;
         if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSelectedRebar) is { } scad)
         {
            key.Append(scad.FileName).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(scad.Data))).Append('|');
            try
            {
               var file = selectedScad = ScadRebarExportReader.Read(scad.Data);
               selected = new ScadSelectedPlateRebarSource(file);
               if (file.Bars.Count > 0) selectedBars = new ScadSelectedBarRebarSource(file);
               selectedFile = scad.FileName;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
               errors.Add((scad.FileName, ex.Message));
            }
         }

         // Заданное армирование SCAD — вложение схемы, прочитанное из .SPR (не файл).
         if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAssignedRebar) is { } scadAssigned)
         {
            key.Append("scad-assigned:").Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(scadAssigned.Data)));
            string label = Loc.S("ScadAssignedRebarLabel");
            try
            {
               var file = ScadAssignedRebarFile.FromJson(System.Text.Encoding.UTF8.GetString(scadAssigned.Data));
               if (file.Plates.Count > 0) assigned = new ScadAssignedPlateRebarSource(file);
               if (file.Rods.Count > 0) assignedBars = new ScadAssignedBarRebarSource(file, ScadAssignedBarRebarSource.SectionCounts(selectedScad));
               if (!file.IsEmpty) assignedFile = label;
            }
            catch (InvalidDataException ex)
            {
               errors.Add((label, ex.Message));
            }
         }
      }
      else if (db.GetFemSchemaSelectedReinforcementFile(schemaId) is { } asp)
      {
         key.Append(asp.FileName).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(asp.Data))).Append('|');
         try
         {
            var file = LiraAspReader.Read(asp.Data);
            selected = new LiraAspPlateRebarSource(file);
            if (file.Bars.Count > 0) selectedBars = new LiraAspBarRebarSource(file);
            selectedFile = asp.FileName;
            foreach (var (num, plate) in file.Plates)
               if (plate.ThicknessM > 0)
                  thickness.TryAdd(num.ToString(CultureInfo.InvariantCulture), plate.ThicknessM);
         }
         catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
         {
            errors.Add((asp.FileName, ex.Message));
         }
      }

      if (db.GetFemSchemaReinforcementFile(schemaId) is { } rbt)
      {
         key.Append(rbt.FileName).Append(':').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rbt.Data)));
         try
         {
            var file = LiraRbtReader.Read(rbt.Data);
            var typeIds = shells.Select(e => new KeyValuePair<string, string?>(e.ElemTag, e.ReinforcementTypeIds));
            assigned = new LiraRbtPlateRebarSource(file, typeIds);
            var barSource = new LiraRbtBarRebarSource(file, elements
               .Where(e => e.ElemType == "beam")
               .Select(e => new KeyValuePair<string, string?>(e.ElemTag, e.ReinforcementTypeIds)));
            if (barSource.HasBars) assignedBars = barSource;
            assignedFile = rbt.FileName;
         }
         catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
         {
            errors.Add((rbt.FileName, ex.Message));
         }
      }
      return new Data(selected, selectedFile, assigned, assignedFile, errors, key.ToString())
      {
         SelectedBars = selectedBars, AssignedBars = assignedBars, ThicknessByTag = thickness, MeshElements = elements,
      };
   }

   /// <summary>Прочитать все данные мозаики схемы. Только в UI-потоке.</summary>
   public static Data ReadAll(DatabaseService db, int schemaId) => Read(db, schemaId).WithProject(db, schemaId);

   /// <summary>Прочитать и сразу применить (в UI-потоке).</summary>
   public void Load(DatabaseService db, int schemaId) => Apply(ReadAll(db, schemaId));

   string? _dataKey;

   /// <summary>Откуда перечитывать данные (задаёт владелец); null — перечитывание не поддерживается.</summary>
   public Func<Data>? Reader { get; set; }

   /// <summary>Перечитать данные, если они изменились с прошлого чтения (файлы дозагружены из меню схемы,
   /// импортированы усилия, выполнена проверка).</summary>
   public void Reload()
   {
      if (Reader == null) return;
      var data = Reader();
      if (data.Key == _dataKey) return;
      Apply(data);
   }

   /// <summary>
   /// Применить прочитанные данные. Выбор вида, набора и компоненты сохраняется, если он
   /// по-прежнему доступен.
   /// </summary>
   public void Apply(Data data)
   {
      _dataKey = data.Key;
      _selected = data.Selected; _selectedFile = data.SelectedFile;
      _assigned = data.Assigned; _assignedFile = data.AssignedFile;
      _layout = data.Layout;
      _selectedBars = data.SelectedBars;
      _assignedBars = data.AssignedBars;
      _thicknessByTag = data.ThicknessByTag;
      _checkResults.Clear();
      _checkRows.Clear();
      foreach (var (file, error) in data.Errors)
         _warn?.Invoke(string.Format(Loc.S("PlateRebarMosaicReadError"), file, error));

      HashSet<int>? scope = ScopeTags?.Select(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : -1).ToHashSet();
      _forceSets = data.ForceSets
         .Where(fs => !ShellOnly || ElementForceField.HasShellRows(fs))
         .Where(fs => scope == null
                      || fs.ShellItems.Any(i => i.SourceElementNum is int n && scope.Contains(n))
                      || (!ShellOnly && fs.Items.Any(i => i.SourceElementNum is int n && scope.Contains(n))))
         .ToList();
      _checks = data.Checks
         .Where(c => !ShellOnly || FemCheckContext.IsPlate(c.Check))
         .Where(c => CheckFilter == null || CheckFilter(c.Check))
         .ToList();

      var kind = Kind;
      var options = new List<PlateRebarMosaicSourceOption>
      {
         new(PlateRebarMosaicSourceKind.None, Loc.S("PlateRebarMosaicSourceNone")),
      };
      if (_selected != null) options.Add(new(PlateRebarMosaicSourceKind.Selected, Loc.S("PlateRebarMosaicSourceSelected")));
      if (_assigned != null) options.Add(new(PlateRebarMosaicSourceKind.Assigned, Loc.S("PlateRebarMosaicSourceAssigned")));
      if (_selected != null && _assigned != null)
         options.Add(new(PlateRebarMosaicSourceKind.Difference, Loc.S("PlateRebarMosaicSourceDifference")));
      if (_layout != null) options.Add(new(PlateRebarMosaicSourceKind.Layout, Loc.S("PlateRebarMosaicSourceLayout")));
      if (_layout != null && _selected != null)
         options.Add(new(PlateRebarMosaicSourceKind.LayoutDifference, Loc.S("PlateRebarMosaicSourceLayoutDifference")));
      if (!ShellOnly)
      {
         if (_selectedBars != null) options.Add(new(PlateRebarMosaicSourceKind.SelectedBars, Loc.S("MosaicSourceSelectedBars")));
         if (_assignedBars != null) options.Add(new(PlateRebarMosaicSourceKind.AssignedBars, Loc.S("MosaicSourceAssignedBars")));
         if (_selectedBars != null && _assignedBars != null)
            options.Add(new(PlateRebarMosaicSourceKind.DifferenceBars, Loc.S("MosaicSourceDifferenceBars")));
      }
      if (_forceSets.Count > 0) options.Add(new(PlateRebarMosaicSourceKind.Forces, Loc.S("MosaicSourceForces")));
      if (_checks.Count > 0) options.Add(new(PlateRebarMosaicSourceKind.Utilization, Loc.S("MosaicSourceUtilization")));
      SourceOptions = options;
      OnPropertyChanged(nameof(SourceOptions));
      OnPropertyChanged(nameof(HasData));

      _selectedSource = options.FirstOrDefault(o => o.Kind == kind) ?? options[0];
      OnPropertyChanged(nameof(SelectedSource));
      RefreshSubjects();
      RaiseChanged();
   }

   IPlateRebarFieldSource? RebarSource() => Kind switch
   {
      PlateRebarMosaicSourceKind.Selected => _selected,
      PlateRebarMosaicSourceKind.Assigned => _assigned,
      PlateRebarMosaicSourceKind.Difference when _selected != null && _assigned != null =>
         new PlateRebarDifferenceSource(_assigned, _selected),
      PlateRebarMosaicSourceKind.Layout => _layout,
      PlateRebarMosaicSourceKind.LayoutDifference when _selected != null && _layout != null =>
         new PlateRebarDifferenceSource(_layout, _selected),
      _ => null,
   };

   IBarRebarFieldSource? BarRebarSource() => Kind switch
   {
      PlateRebarMosaicSourceKind.SelectedBars => _selectedBars,
      PlateRebarMosaicSourceKind.AssignedBars => _assignedBars,
      PlateRebarMosaicSourceKind.DifferenceBars when _selectedBars != null && _assignedBars != null =>
         new BarRebarDifferenceSource(_assignedBars, _selectedBars),
      _ => null,
   };

   void RefreshSubjects()
   {
      object? previous = SelectedSubject?.Subject;
      SubjectOptions = Kind switch
      {
         PlateRebarMosaicSourceKind.Forces =>
            _forceSets.Select(fs => new PlateRebarMosaicSubjectOption(fs, fs.Tag)).ToList(),
         PlateRebarMosaicSourceKind.Utilization =>
            _checks.Select(c => new PlateRebarMosaicSubjectOption(c, c.Label)).ToList(),
         _ => [],
      };
      OnPropertyChanged(nameof(SubjectOptions));
      OnPropertyChanged(nameof(HasSubject));
      OnPropertyChanged(nameof(HasAggregate));
      _selectedSubject = SubjectOptions.FirstOrDefault(o => SameSubject(o.Subject, previous)) ?? SubjectOptions.FirstOrDefault();
      OnPropertyChanged(nameof(SelectedSubject));
      RefreshComponents();
   }

   // После перечитывания объекты наборов те же, а записи о проверках создаются заново.
   static bool SameSubject(object a, object? b) => (a, b) switch
   {
      (CheckInfo x, CheckInfo y) => x.Check.Id == y.Check.Id,
      _ => ReferenceEquals(a, b),
   };

   void RefreshComponents()
   {
      object? previous = SelectedComponent?.Component;
      ComponentOptions = BuildComponentOptions();
      OnPropertyChanged(nameof(ComponentOptions));
      _selectedComponent = ComponentOptions.FirstOrDefault(o => Equals(o.Component, previous)) ?? ComponentOptions.FirstOrDefault();
      OnPropertyChanged(nameof(SelectedComponent));
      OnPropertyChanged(nameof(IsActive));
      OnPropertyChanged(nameof(HasBarDiagrams));
      OnPropertyChanged(nameof(ThresholdsText));
   }

   List<PlateRebarMosaicComponentOption> BuildComponentOptions()
   {
      switch (Kind)
      {
         case PlateRebarMosaicSourceKind.Forces when SelectedSubject?.Subject is ForceSet set:
            if (ElementForceField.HasShellRows(set))
            {
               bool stresses = ElementForceField.HasStresses(set);
               return Enum.GetValues<ShellForceComponent>()
                  .Where(c => stresses || c < ShellForceComponent.SigmaX)
                  .Select(c => new PlateRebarMosaicComponentOption(c, Loc.S("MosaicShellForce" + c)))
                  .ToList();
            }
            return Enum.GetValues<BarForceComponent>()
               .Select(c => new PlateRebarMosaicComponentOption(c, Loc.S("MosaicBarForce" + c)))
               .ToList();

         case PlateRebarMosaicSourceKind.Utilization when SelectedSubject?.Subject is CheckInfo check:
            return CheckResults(check).Select(r => r.RebarSource).Distinct()
               .Select(key => new PlateRebarMosaicComponentOption(key,
                  key == "" ? Loc.S("MosaicUtilization") : FemCheckContext.SourceName(key)))
               .ToList();

         case PlateRebarMosaicSourceKind.SelectedBars or PlateRebarMosaicSourceKind.AssignedBars
            or PlateRebarMosaicSourceKind.DifferenceBars when BarRebarSource() is { } bars:
            return Enum.GetValues<BarRebarComponent>()
               .Where(bars.Supports)
               .Select(c => new PlateRebarMosaicComponentOption(c, Loc.S("MosaicBarRebar" + c)))
               .ToList();

         default:
            return RebarSource() is { } source
               ? Enum.GetValues<PlateRebarMosaicComponent>()
                  .Where(source.Supports)
                  .Select(c => new PlateRebarMosaicComponentOption(c, RebarComponentLabel(c)))
                  .ToList()
               : [];
      }
   }

   IReadOnlyList<FemCheckRowResult> CheckRows(CheckInfo check)
   {
      if (!_checkRows.TryGetValue(check.Check.Id, out var rows))
         _checkRows[check.Check.Id] = rows = FemCheckElementResults.ParseRows(check.LoadJson());
      return rows;
   }

   /// <summary>
   /// Цвет значения по шкале текущей мозаики — для окраски ступеней эпюры коэффициента использования
   /// на стержнях; null — мозаика не коэффициента использования или шкала не построена.
   /// </summary>
   public Color? UtilizationColor(double value) =>
      Kind == PlateRebarMosaicSourceKind.Utilization && _scale is { } scale && _bandColors.Length == scale.Bands.Count
         ? _bandColors[scale.BandOf(value)] : null;

   IReadOnlyList<FemCheckElementResult> CheckResults(CheckInfo check)
   {
      if (!_checkResults.TryGetValue(check.Check.Id, out var rows))
         _checkResults[check.Check.Id] = rows = FemCheckElementResults.Parse(check.LoadJson());
      return rows;
   }

   static string RebarComponentLabel(PlateRebarMosaicComponent c) => Loc.S(c switch
   {
      PlateRebarMosaicComponent.BottomX => "PlateRebarMosaicCompAs1",
      PlateRebarMosaicComponent.TopX => "PlateRebarMosaicCompAs2",
      PlateRebarMosaicComponent.BottomY => "PlateRebarMosaicCompAs3",
      PlateRebarMosaicComponent.TopY => "PlateRebarMosaicCompAs4",
      _ => "PlateRebarMosaicCompAsw",
   });

   void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

   /// <summary>Значения текущей мозаики на КЭ; null — мозаика выключена.</summary>
   Field? BuildField(IEnumerable<string> shellTags, IEnumerable<string> barTags)
   {
      if (SelectedComponent is not { } comp) return null;
      switch (Kind)
      {
         case PlateRebarMosaicSourceKind.Forces when SelectedSubject?.Subject is ForceSet set:
         {
            bool bars = comp.Component is BarForceComponent;
            Dictionary<int, double> byNum = comp.Component switch
            {
               ShellForceComponent c => ElementForceField.Shell(set, c, SelectedAggregate.Aggregate,
                  num => _thicknessByTag.TryGetValue(num.ToString(CultureInfo.InvariantCulture), out double h) ? h : null),
               BarForceComponent c => ElementForceField.Bar(set, c, SelectedAggregate.Aggregate),
               _ => [],
            };
            var values = new Dictionary<string, PlateRebarValue>(StringComparer.Ordinal);
            foreach (string tag in bars ? barTags : shellTags)
               values[tag] = int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                             && byNum.TryGetValue(n, out double v)
                  ? PlateRebarValue.Of(v) : PlateRebarValue.Missing;
            return new Field(values, bars, Diverging: true, Palette.Forces);
         }

         case PlateRebarMosaicSourceKind.Utilization when SelectedSubject?.Subject is CheckInfo check:
         {
            bool bars = !FemCheckContext.IsPlate(check.Check);
            var byTag = new Dictionary<string, PlateRebarValue>(StringComparer.Ordinal);
            foreach (var r in CheckResults(check))
            {
               if (!Equals(r.RebarSource, comp.Component)) continue;
               byTag[r.ElemTag] = r.UtilMax is double u && r.IsChecked ? PlateRebarValue.Of(u)
                  : r.Status == "failed" ? PlateRebarValue.Failure(FailedWithoutUtilization)
                  : PlateRebarValue.Failure(NotChecked);
            }
            var values = new Dictionary<string, PlateRebarValue>(StringComparer.Ordinal);
            foreach (string tag in bars ? barTags : shellTags)
               values[tag] = byTag.GetValueOrDefault(tag);
            return new Field(values, bars, Diverging: false, Palette.Utilization, UtilizationThresholds);
         }

         case PlateRebarMosaicSourceKind.SelectedBars or PlateRebarMosaicSourceKind.AssignedBars
            or PlateRebarMosaicSourceKind.DifferenceBars
            when BarRebarSource() is { } barSource && comp.Component is BarRebarComponent c:
         {
            var values = new Dictionary<string, PlateRebarValue>(StringComparer.Ordinal);
            foreach (string tag in barTags)
               values[tag] = barSource.Get(tag, c);
            bool barDifference = Kind == PlateRebarMosaicSourceKind.DifferenceBars;
            return new Field(values, Bars: true, barDifference, barDifference ? Palette.Difference : Palette.Rebar);
         }

         default:
            if (RebarSource() is not { } source || comp.Component is not PlateRebarMosaicComponent rebar) return null;
            bool difference = Kind is PlateRebarMosaicSourceKind.Difference or PlateRebarMosaicSourceKind.LayoutDifference;
            return new Field(PlateRebarMosaic.Evaluate(source, rebar, shellTags), Bars: false, difference,
               difference ? Palette.Difference : Palette.Rebar);
      }
   }

   /// <summary>
   /// Раскрасить КЭ по текущим настройкам и обновить легенду (шкала — по этим КЭ).
   /// Null — мозаика выключена.
   /// </summary>
   /// <param name="shellTags">Теги пластинчатых КЭ вида.</param>
   /// <param name="barTags">Теги стержневых КЭ вида; null — стержни не показываются.</param>
   public PlateRebarMosaicColoring? Compute(IEnumerable<string> shellTags, IEnumerable<string>? barTags = null)
   {
      _field = BuildField(shellTags, barTags ?? []);
      HoverText = "";
      if (_field is not { } field)
      {
         Legend = [];
         LegendTitle = "";
         OnPropertyChanged(nameof(Legend));
         return null;
      }

      var numbers = field.Values.Values.Where(v => v.Value != null).Select(v => v.Value!.Value).ToList();
      double min = numbers.Count > 0 ? numbers.Min() : 0, max = numbers.Count > 0 ? numbers.Max() : 0;
      var manual = ParseThresholds(ThresholdsText);
      // Пороги по умолчанию выше наибольшего значения отбрасываются (пустые верхние полосы); до 1,0 — остаются все.
      var scale = manual.Count > 0 ? PlateRebarMosaicScale.Manual(manual, field.Diverging)
         : field.DefaultThresholds != null
            ? PlateRebarMosaicScale.Manual(field.DefaultThresholds.Where(t => t <= 1 || t < max), field.Diverging)
         : PlateRebarMosaicScale.Auto(numbers, field.Diverging);
      var colors = BandColors(scale, field.Palette);
      _scale = scale;
      _bandColors = colors;

      var counts = new int[scale.Bands.Count];
      // Особые состояния (отказ подбора, не проверено) — по подписи: у ASP кодов отказа много, категория одна.
      var special = new Dictionary<string, (Color Color, int Count)>();
      int missing = 0;
      var byTag = new Dictionary<string, Color>(StringComparer.Ordinal);
      foreach (var (tag, v) in field.Values)
      {
         if (v.FailureCode is int code)
         {
            var (color, label) = FailureStyle(field.Palette, code);
            byTag[tag] = color;
            special[label] = (color, special.GetValueOrDefault(label).Count + 1);
         }
         else if (v.Value is { } x) { int b = scale.BandOf(x); counts[b]++; byTag[tag] = colors[b]; }
         else missing++;
      }

      var legend = new List<PlateRebarMosaicLegendItem>();
      for (int i = scale.Bands.Count - 1; i >= 0; i--)
         legend.Add(new(Freeze(colors[i]), BandLabel(scale.Bands[i], min, max), counts[i]));
      foreach (var (label, (color, count)) in special)
         legend.Add(new(Freeze(color), label, count));
      if (missing > 0) legend.Add(new(Freeze(Fem3DVM.ShellBgColor), Loc.S("PlateRebarMosaicNoData"), missing));
      Legend = legend;
      OnPropertyChanged(nameof(Legend));

      LegendTitle = Title();
      return new PlateRebarMosaicColoring(byTag, field.Bars);
   }

   string Title()
   {
      string comp = SelectedComponent!.Label;
      switch (Kind)
      {
         case PlateRebarMosaicSourceKind.Forces:
            return $"{comp}\n{SelectedSubject?.Label}\n{SelectedAggregate.Label}";
         case PlateRebarMosaicSourceKind.Utilization:
            return $"{Loc.S("MosaicUtilization")}\n{SelectedSubject?.Label}"
                   + (Equals(SelectedComponent.Component, "") ? "" : $"\n{comp}");
         case PlateRebarMosaicSourceKind.SelectedBars:
            return $"{comp}\n{SelectedSource!.Label}\n{_selectedFile}";
         case PlateRebarMosaicSourceKind.AssignedBars:
            return $"{comp}\n{SelectedSource!.Label}\n{_assignedFile}";
         case PlateRebarMosaicSourceKind.DifferenceBars:
            return $"{comp}\n{SelectedSource!.Label}\n{_assignedFile} − {_selectedFile}";
         default:
            string unit = Equals(SelectedComponent.Component, PlateRebarMosaicComponent.Transverse) ? "" : Loc.S("PlateRebarMosaicUnit");
            string file = Kind switch
            {
               PlateRebarMosaicSourceKind.Selected => _selectedFile ?? "",
               PlateRebarMosaicSourceKind.Assigned => _assignedFile ?? "",
               PlateRebarMosaicSourceKind.Layout => Loc.S("PlateRebarMosaicLayoutFile"),
               PlateRebarMosaicSourceKind.LayoutDifference => $"{Loc.S("PlateRebarMosaicLayoutFile")} − {_selectedFile}",
               _ => $"{_assignedFile} − {_selectedFile}",
            };
            return $"{comp}{unit}\n{SelectedSource!.Label}\n{file}";
      }
   }

   static (Color Color, string Label) FailureStyle(Palette palette, int code) => palette switch
   {
      Palette.Utilization when code == NotChecked => (NotCheckedColor, Loc.S("MosaicNotChecked")),
      Palette.Utilization => (FailureColor, Loc.S("MosaicFailedNoUtilization")),
      _ => (FailureColor, Loc.S("PlateRebarMosaicFailure")),
   };

   /// <summary>Показать значение КЭ под курсором; null — курсор не над КЭ.</summary>
   public void SetHover(string? elemTag)
   {
      if (elemTag == null || _field == null) { HoverText = ""; return; }
      string text;
      if (!_field.Values.TryGetValue(elemTag, out var v) || v.IsMissing) text = Loc.S("PlateRebarMosaicNoData");
      else if (v.FailureCode is int code) text = FailureStyle(_field.Palette, code).Label;
      else text = v.Value!.Value.ToString("0.###", CultureInfo.CurrentCulture);
      HoverText = $"{Loc.S("MosaicElement")} {elemTag}: {text}" + UtilizationBySections(elemTag);
   }

   /// <summary>Кисп стержня по его сечениям — строками подсказки; пусто — мозаика не Кисп стержней или сечение одно.</summary>
   string UtilizationBySections(string elemTag)
   {
      if (_field is not { Bars: true } || Kind != PlateRebarMosaicSourceKind.Utilization
          || SelectedSubject?.Subject is not CheckInfo check || SelectedComponent?.Component is not string source
          || !int.TryParse(elemTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num))
         return "";
      // Правило значения сечения — общее с эпюрой (BarDiagram.UtilizationSections): отказ без коэффициента
      // в любой строке сечения — «не проходит», а не наибольший Кисп остальных строк.
      var sections = BarDiagram.UtilizationSections(
         CheckRows(check).Where(r => r.ElemNum == num && r.RebarSource == source));
      if (sections.Count < 2 || sections.Any(s => s.Num == null)) return "";
      var sb = new System.Text.StringBuilder();
      foreach (var (sectionNum, _, _, value, failed) in sections)
      {
         string text = failed ? Loc.S("MosaicFailedNoUtilization")
            : value is double v ? v.ToString("0.###", CultureInfo.CurrentCulture) : Loc.S("MosaicNotChecked");
         sb.Append('\n').Append(string.Format(Loc.S("MosaicHoverSection"), sectionNum, text));
      }
      return sb.ToString();
   }

   static SolidColorBrush Freeze(Color c)
   {
      var b = new SolidColorBrush(c);
      b.Freeze();
      return b;
   }

   /// <summary>Пороги из строки «3,93; 5,65 7,85»: разделители «;» и пробел, запятая — десятичная.</summary>
   public static List<double> ParseThresholds(string? text)
   {
      var result = new List<double>();
      if (string.IsNullOrWhiteSpace(text)) return result;
      foreach (string token in text.Split([';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
         if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            result.Add(v);
      return result;
   }

   static Color[] BandColors(PlateRebarMosaicScale scale, Palette palette)
   {
      var bands = scale.Bands;
      var colors = new Color[bands.Count];
      if (palette == Palette.Utilization)
      {
         // Порог 1,0 делит шкалу на «проходит» и «не проходит»; полоса, накрывающая 1, — уже «не проходит».
         int passed = bands.Count(b => !b.IsZero && b.Upper <= 1 + 1e-9), failed = bands.Count - 1 - passed;
         int pi = 0, fi = 0;
         for (int i = 0; i < bands.Count; i++)
            colors[i] = bands[i].IsZero ? ZeroColor
               : bands[i].Upper <= 1 + 1e-9 ? Interpolate(PassedStops, passed <= 1 ? 0 : pi++ / (double)(passed - 1))
               : Interpolate(NotPassedStops, failed <= 1 ? 0 : fi++ / (double)(failed - 1));
         return colors;
      }
      if (!scale.IsDiverging)
      {
         colors[0] = ZeroColor;
         for (int i = 1; i < bands.Count; i++)
            colors[i] = Interpolate(SequentialStops, bands.Count <= 2 ? 1 : (i - 1) / (double)(bands.Count - 2));
         return colors;
      }
      var (negStops, posStops) = palette == Palette.Forces
         ? (NegativeForceStops, PositiveForceStops)
         : (DeficitStops, ReserveStops);
      int neg = bands.Count(b => b.IsNegative), pos = bands.Count(b => b.IsPositive);
      int ni = 0, pj = 0;
      for (int i = 0; i < bands.Count; i++)
      {
         if (bands[i].IsNegative) colors[i] = Interpolate(negStops, neg <= 1 ? 0 : ni++ / (double)(neg - 1));
         else if (bands[i].IsPositive) colors[i] = Interpolate(posStops, pos <= 1 ? 1 : pj++ / (double)(pos - 1));
         else colors[i] = ZeroColor;
      }
      return colors;
   }

   static Color Interpolate(Color[] stops, double t)
   {
      t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
      int i = Math.Min((int)t, stops.Length - 2);
      double f = t - i;
      byte L(byte a, byte b) => (byte)Math.Round(a + (b - a) * f);
      return Color.FromRgb(L(stops[i].R, stops[i + 1].R), L(stops[i].G, stops[i + 1].G), L(stops[i].B, stops[i + 1].B));
   }

   static string BandLabel(PlateRebarMosaicBand band, double min, double max)
   {
      static string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
      if (band.IsZero) return "0";
      if (double.IsNegativeInfinity(band.Lower)) return $"{F(Math.Min(min, band.Upper))} … {F(band.Upper)}";
      if (double.IsPositiveInfinity(band.Upper)) return $"{F(band.Lower)} … {F(Math.Max(max, band.Lower))}";
      return $"{F(band.Lower)} … {F(band.Upper)}";
   }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CScore;
using CScore.Import.Cad;
using OpenCS.Services;
using OpenCS.Services.Cad;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Роль слоя с подписью для выпадающего списка.</summary>
public sealed record CadRoleItem(CadLayerRole Role, string Text)
{
   public override string ToString() => Text;
}

/// <summary>Выбор материала слоя: материал проекта или создание из справочника по классу.</summary>
public sealed record CadMaterialChoice(string Text, Material? Existing, string? CreateClass)
{
   public override string ToString() => Text;
}

/// <summary>Строка таблицы слоёв: роль и материал.</summary>
public sealed class CadLayerRowVM : ViewModelBase
{
   readonly CadSectionImportVM _owner;
   CadRoleItem _role;
   CadMaterialChoice? _material;
   List<CadMaterialChoice> _materials = [];

   public CadLayerRowVM(CadSectionImportVM owner, CadLayerInfo info, CadLayerRole role)
   {
      _owner = owner;
      Info = info;
      _role = CadSectionImportVM.RoleItems.First(r => r.Role == role);
   }

   public CadLayerInfo Info { get; }
   public string Name => Info.Name;

   /// <summary>Состав слоя: «контуров 12, окружностей 96, текстов 4».</summary>
   public string Contents => string.Join(", ", new[]
   {
      Info.Polygons > 0 ? $"{Loc.S("CadImpContours")} {Info.Polygons}" : null,
      Info.Circles > 0 ? $"{Loc.S("CadImpCircles")} {Info.Circles}" : null,
      Info.Texts > 0 ? $"{Loc.S("CadImpTexts")} {Info.Texts}" : null,
      Info.Lines > 0 ? $"{Loc.S("CadImpLines")} {Info.Lines}" : null,
   }.Where(s => s != null));

   public IReadOnlyList<CadRoleItem> Roles => CadSectionImportVM.RoleItems;

   public CadRoleItem Role
   {
      get => _role;
      set
      {
         if (value == null || value == _role) return;
         _role = value;
         OnPropertyChanged();
         RefreshMaterials();
         _owner.Recognize();
      }
   }

   public bool NeedsMaterial => _role.Role is CadLayerRole.Concrete or CadLayerRole.Steel or CadLayerRole.Rebar or CadLayerRole.Stirrups;

   public List<CadMaterialChoice> Materials
   {
      get => _materials;
      private set { _materials = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedsMaterial)); }
   }

   public CadMaterialChoice? Material
   {
      get => _material;
      set { _material = value; OnPropertyChanged(); }
   }

   /// <summary>Список материалов под роль и выбор по умолчанию: запомненный, по классу в имени слоя, первый подходящий.</summary>
   public void RefreshMaterials(int? rememberedId = null)
   {
      Materials = _owner.MaterialChoices(Name, _role.Role);
      Material = Materials.FirstOrDefault(m => rememberedId != null && m.Existing?.Id == rememberedId)
                 ?? _owner.DefaultChoice(Name, _role.Role, Materials);
   }
}

/// <summary>Карточка распознанного сечения.</summary>
public sealed class CadSectionCardVM(CadSectionCandidate candidate) : ViewModelBase
{
   bool _selected = true;
   string _name = candidate.Name;
   int _shown;

   public CadSectionCandidate Candidate { get; } = candidate;

   public bool IsSelected { get => _selected; set { _selected = value; OnPropertyChanged(); } }

   public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

   /// <summary>«×5» для объединённых одинаковых сечений.</summary>
   public string CountText => Candidate.Count > 1 ? $"×{Candidate.Count}" : "";

   /// <summary>Имена всех экземпляров — во всплывающей подсказке.</summary>
   public string NamesText => string.Join(", ", Candidate.Names);

   public string Info
   {
      get
      {
         double a = Candidate.Regions.Sum(r => r.Area) * 1e4;
         double asum = Candidate.Bars.Sum(b => Math.PI * b.DiameterM * b.DiameterM / 4) * 1e4;
         var diam = Candidate.Bars.GroupBy(b => Math.Round(b.DiameterM * 1000, 1)).OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()}Ø{g.Key:0.#}");
         // Две строки: площадь и армирование — чтобы «см²» не отрывался переносом.
         string bars = Candidate.Bars.Count > 0 ? $"\n{string.Join(" + ", diam)}, As = {asum:0.##} см²" : "";
         return $"A = {a:0.#} см²{bars}";
      }
   }

   public string WarningsText => string.Join("\n", Candidate.Warnings);
   public bool HasWarnings => Candidate.Warnings.Count > 0;

   /// <summary>Следующий экземпляр для показа в САПР (повторный щелчок — следующий).</summary>
   public CadBounds NextInstance() => Candidate.Instances[_shown++ % Candidate.Instances.Count];
}

/// <summary>
/// Импорт множества сечений стержней из DXF-файла или открытого документа AutoCAD/nanoCAD: роли слоёв,
/// распознавание и предпросмотр (<see cref="CadSectionRecognizer"/>), пакетное создание сечений проекта.
/// </summary>
public sealed class CadSectionImportVM : ViewModelBase
{
   public static readonly IReadOnlyList<CadRoleItem> RoleItems =
   [
      new(CadLayerRole.Ignore, Loc.S("CadRoleIgnore")),
      new(CadLayerRole.Concrete, Loc.S("CadRoleConcrete")),
      new(CadLayerRole.Steel, Loc.S("CadRoleSteel")),
      new(CadLayerRole.Rebar, Loc.S("CadRoleRebar")),
      new(CadLayerRole.Hole, Loc.S("CadRoleHole")),
      new(CadLayerRole.Frame, Loc.S("CadRoleFrame")),
      new(CadLayerRole.Label, Loc.S("CadRoleLabel")),
      new(CadLayerRole.Stirrups, Loc.S("CadRoleStirrups")),
   ];

   static readonly double[] UnitMeters = [0.001, 0.01, 1.0];

   readonly AppViewModel _mvm;
   CadSnapshot? _snapshot;
   string? _file;
   CadInstance? _cad;
   CadBounds? _window;
   CadInstance? _selectedCad;
   int _unitIdx;
   bool _unitsByUser;
   int _arcModeIdx;
   bool _merge;
   bool _busy;
   string _source = "";
   string _status = "";

   public CadSectionImportVM(AppViewModel mvm)
   {
      _mvm = mvm;
      var s = mvm.AcadImportSettings;
      _unitIdx = Math.Max(0, Array.FindIndex(UnitMeters, u => Math.Abs(u - s.ScaleFactor) < 1e-12));
      _arcModeIdx = (int)s.SectionArcMode;
      _merge = s.SectionMergeIdentical;

      OpenFileCommand = new RelayCommand(_ => OpenFile(), _ => !_busy);
      RefreshCadCommand = new RelayCommand(_ => RefreshCad(), _ => !_busy);
      ReadAllCommand = new RelayCommand(_ => ReadCad(window: false), _ => !_busy && SelectedCad != null);
      ReadWindowCommand = new RelayCommand(_ => ReadCad(window: true), _ => !_busy && SelectedCad != null);
      ShowInCadCommand = new RelayCommand(p => ShowInCad(p as CadSectionCardVM));
      SelectAllCommand = new RelayCommand(p => { foreach (var c in Sections) c.IsSelected = p is not false and not "false"; });
      CreateCommand = new RelayCommand(_ => Create(), _ => !_busy && Sections.Any(c => c.IsSelected));
      RefreshCad();
   }

   // ── Привязки ────────────────────────────────────────────────────────────

   public ICommand OpenFileCommand { get; }
   public ICommand RefreshCadCommand { get; }
   public ICommand ReadAllCommand { get; }
   public ICommand ReadWindowCommand { get; }
   public ICommand ShowInCadCommand { get; }
   public ICommand SelectAllCommand { get; }
   public ICommand CreateCommand { get; }

   public ObservableCollection<CadInstance> CadInstances { get; } = [];
   public ObservableCollection<CadLayerRowVM> Layers { get; } = [];
   public ObservableCollection<CadSectionCardVM> Sections { get; } = [];
   public ObservableCollection<string> Warnings { get; } = [];

   public CadInstance? SelectedCad { get => _selectedCad; set { _selectedCad = value; OnPropertyChanged(); } }

   public List<string> Units { get; } = ["мм", "см", "м"];

   public int UnitIdx
   {
      get => _unitIdx;
      set
      {
         if (value == _unitIdx || value < 0) return;
         _unitIdx = value;
         _unitsByUser = true;
         OnPropertyChanged();
         Reload();
      }
   }

   public List<string> ArcModes { get; } = [Loc.S("CadArcDeviation"), Loc.S("CadArcChord"), Loc.S("CadArcFixed")];

   public int ArcModeIdx
   {
      get => _arcModeIdx;
      set
      {
         if (value == _arcModeIdx || value < 0) return;
         _arcModeIdx = value;
         OnPropertyChanged();
         Reload();
      }
   }

   public bool MergeIdentical
   {
      get => _merge;
      set { _merge = value; OnPropertyChanged(); Recognize(); }
   }

   public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }

   /// <summary>Источник снимка: файл или документ САПР (и рамка).</summary>
   public string SourceText { get => _source; private set { _source = value; OnPropertyChanged(); } }

   public string StatusText { get => _status; private set { _status = value; OnPropertyChanged(); } }

   public string CreateText => string.Format(Loc.S("CadImpCreateN"), Sections.Count(c => c.IsSelected));

   double MetersPerUnit => UnitMeters[_unitIdx];

   CadImportOptions Options()
   {
      var s = _mvm.AcadImportSettings;
      var o = new CadImportOptions
      {
         MetersPerUnit = MetersPerUnit,
         MergeIdentical = _merge,
         ArcMode = (CadArcMode)_arcModeIdx,
         ArcDeviationM = s.SectionArcDeviation / 1000,
         ArcChordM = s.ArcChordLength / 1000,
         ArcSegments = s.ArcSegments,
      };
      foreach (var row in Layers) o.Roles[row.Name] = row.Role.Role;
      return o;
   }

   /// <summary>Допуск сшивки концов — 0,01 мм в единицах чертежа.</summary>
   double Tolerance => 1e-5 / MetersPerUnit;

   // ── Источники ───────────────────────────────────────────────────────────

   void OpenFile()
   {
      string? file = _mvm.FileDialogService.OpenFile(
         filter: "Файл обмена чертежами (*.dxf)|*.dxf",
         title: Loc.S("CadImpOpenDxfTitle"));
      if (string.IsNullOrEmpty(file)) return;
      _file = file;
      _cad = null;
      _window = null;
      _unitsByUser = false;
      Reload();
   }

   async void RefreshCad()
   {
      try
      {
         var found = await Task.Run(CadComConnector.Find);
         var keep = SelectedCad;
         CadInstances.Clear();
         foreach (var c in found) CadInstances.Add(c);
         SelectedCad = CadInstances.FirstOrDefault(c => c == keep) ?? CadInstances.FirstOrDefault();
         if (CadInstances.Count == 0) StatusText = Loc.S("CadImpNoCad");
      }
      catch (Exception e) { StatusText = e.Message; }
   }

   async void ReadCad(bool window)
   {
      if (SelectedCad is not { } cad) return;
      CadBounds? w = null;
      if (window)
      {
         var main = Application.Current?.MainWindow;
         try
         {
            IsBusy = true;
            StatusText = Loc.S("CadImpPickInCad");
            if (main != null) main.WindowState = WindowState.Minimized;
            w = await Task.Run(() => CadComConnector.PickWindow(cad));
         }
         catch (Exception e) { StatusText = e.Message; return; }
         finally
         {
            if (main != null) { main.WindowState = WindowState.Normal; main.Activate(); }
            IsBusy = false;
         }
         if (w == null) { StatusText = Loc.S("CadImpPickCancelled"); return; }
      }
      _cad = cad;
      _file = null;
      _window = w;
      _unitsByUser = false;
      Reload();
   }

   /// <summary>Перечитать текущий источник (после смены единиц или дискретизации дуг).</summary>
   async void Reload()
   {
      if (_file == null && _cad == null) return;
      try
      {
         IsBusy = true;
         StatusText = Loc.S("CadImpReading");
         var sw = System.Diagnostics.Stopwatch.StartNew();
         var snap = await Task.Run(Read);
         // Единицы чертежа ($INSUNITS) — если пользователь не выбрал свои: перечитать с ними (хорда дуг в единицах чертежа).
         if (!_unitsByUser && snap.MetersPerUnit is double mpu && Math.Abs(mpu - MetersPerUnit) > 1e-12
             && Array.FindIndex(UnitMeters, u => Math.Abs(u - mpu) < 1e-12) is int idx and >= 0)
         {
            _unitIdx = idx;
            OnPropertyChanged(nameof(UnitIdx));
            snap = await Task.Run(Read);
         }
         _snapshot = snap;
         SourceText = _window is { } w
            ? $"{snap.Source}, {Loc.S("CadImpWindow")} {w.Width:0.#}×{w.Height:0.#}"
            : snap.Source;
         BuildLayers();
         Recognize();
         StatusText = string.Format(Loc.S("CadImpReadDone"), sw.ElapsedMilliseconds,
            snap.Polygons.Count, snap.Circles.Count, snap.Texts.Count);
      }
      catch (Exception e)
      {
         StatusText = e.Message;
         _mvm.LogService.Error(e.Message);
      }
      finally { IsBusy = false; }

      CadSnapshot Read()
      {
         var arcs = Options().Arcs;
         return _cad is { } cad
            ? CadComConnector.Read(cad, arcs, Tolerance, _window)
            : DxfSnapshotReader.Read(_file!, arcs, Tolerance);
      }
   }

   // ── Слои и материалы ────────────────────────────────────────────────────

   void BuildLayers()
   {
      var s = _mvm.AcadImportSettings;
      var prev = Layers.ToDictionary(l => l.Name, l => (l.Role.Role, l.Material?.Existing?.Id), StringComparer.OrdinalIgnoreCase);
      Layers.Clear();
      foreach (var info in _snapshot!.Layers())
      {
         CadLayerRole role;
         int? matId = null;
         if (prev.TryGetValue(info.Name, out var p)) (role, matId) = p;
         else if (Lookup(s.SectionLayerRoles, info.Name) is { } r) role = r;
         else role = CadLayerRules.Guess(info);
         matId ??= Lookup(s.SectionLayerMaterials, info.Name);
         var row = new CadLayerRowVM(this, info, role);
         row.RefreshMaterials(matId);
         Layers.Add(row);
      }
   }

   static T? Lookup<T>(Dictionary<string, T> map, string key) where T : struct =>
      map.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) is { Key: not null } kv ? kv.Value : null;

   internal List<CadMaterialChoice> MaterialChoices(string layer, CadLayerRole role)
   {
      bool Fits(Material m) => role switch
      {
         CadLayerRole.Concrete => m.Type == MatType.Concrete,
         CadLayerRole.Rebar or CadLayerRole.Stirrups => m.Type is MatType.ReSteelF or MatType.ReSteelU,
         CadLayerRole.Steel => m.Type is MatType.Steel or MatType.ReSteelF or MatType.ReSteelU,
         _ => false,
      };
      var list = _mvm.Materials.Where(Fits).Select(m => new CadMaterialChoice(m.Tag, m, null)).ToList();
      if (CadLayerRules.GuessClass(layer, role) is { } cls && !list.Any(c => SameClass(c.Existing!, cls, role)))
         list.Insert(0, new CadMaterialChoice(string.Format(Loc.S("CadImpCreateMaterial"), cls), null, cls));
      return list;
   }

   static bool SameClass(Material m, string cls, CadLayerRole role) => role == CadLayerRole.Steel
      ? m.Tag.StartsWith(MaterialCatalog.NormalizeSteelMark(cls), StringComparison.OrdinalIgnoreCase)
      : MaterialCatalog.ClassKey(m.Tag) == MaterialCatalog.ClassKey(cls);

   internal CadMaterialChoice? DefaultChoice(string layer, CadLayerRole role, List<CadMaterialChoice> list)
   {
      if (CadLayerRules.GuessClass(layer, role) is { } cls)
         return list.FirstOrDefault(c => c.Existing != null && SameClass(c.Existing, cls, role))
                ?? list.FirstOrDefault(c => c.CreateClass != null);
      return list.FirstOrDefault();
   }

   // ── Распознавание ───────────────────────────────────────────────────────

   internal void Recognize()
   {
      if (_snapshot == null) return;
      var keep = Sections.ToDictionary(c => c.Candidate.Signature, c => (c.IsSelected, c.Name));
      var r = CadSectionRecognizer.Recognize(_snapshot, Options());
      Sections.Clear();
      foreach (var c in r.Sections)
      {
         var card = new CadSectionCardVM(c);
         if (keep.TryGetValue(c.Signature, out var k)) { card.IsSelected = k.IsSelected; card.Name = k.Name; }
         card.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CadSectionCardVM.IsSelected)) OnPropertyChanged(nameof(CreateText)); };
         Sections.Add(card);
      }
      Warnings.Clear();
      foreach (var w in _snapshot.Warnings.Concat(r.Warnings)) Warnings.Add(w);
      OnPropertyChanged(nameof(CreateText));
      OnPropertyChanged(nameof(SummaryText));
   }

   public string SummaryText => Sections.Count == 0 ? Loc.S("CadImpNoSections")
      : string.Format(Loc.S("CadImpSummary"), Sections.Sum(c => c.Candidate.Count), Sections.Count);

   async void ShowInCad(CadSectionCardVM? card)
   {
      if (card == null || _cad is not { } cad) return;
      var b = card.NextInstance();
      try { await Task.Run(() => CadComConnector.ZoomTo(cad, b)); }
      catch (Exception e) { StatusText = e.Message; }
   }

   // ── Создание ────────────────────────────────────────────────────────────

   void Create()
   {
      var chosen = Sections.Where(c => c.IsSelected).ToList();
      var created = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
      var errors = new List<string>();

      Material? MaterialOf(string layer, CadLayerRole role)
      {
         var row = Layers.FirstOrDefault(l => string.Equals(l.Name, layer, StringComparison.OrdinalIgnoreCase));
         var choice = row?.Material;
         if (choice?.Existing is { } m) return m;
         if (choice?.CreateClass is not { } cls) return null;
         string key = $"{(role == CadLayerRole.Stirrups ? CadLayerRole.Rebar : role)}:{cls}"; // хомуты и стержни одного класса — один материал
         if (created.TryGetValue(key, out var made)) return made;
         made = role switch
         {
            CadLayerRole.Concrete => MaterialCatalog.CreateHeavyConcrete(cls),
            CadLayerRole.Rebar or CadLayerRole.Stirrups => MaterialCatalog.CreateRebar(cls),
            _ => MaterialCatalog.CreateStructuralSteel(MaterialCatalog.NormalizeSteelMark(cls)),
         };
         if (made == null) { errors.Add(string.Format(Loc.S("CadImpNoCatalogClass"), cls)); return null; }
         _mvm.db.AddMaterial(made);
         created[key] = made;
         return made;
      }

      var sections = new List<CrossSection>();
      var names = new HashSet<string>(_mvm.db.CrossSections.Select(s => s.Tag), StringComparer.OrdinalIgnoreCase);
      foreach (var card in chosen)
      {
         var (section, reason) = CadSectionBuilder.Build(card.Candidate, MaterialOf);
         if (section == null) { errors.Add($"{card.Name}: {reason}"); continue; }
         string tag = string.IsNullOrWhiteSpace(card.Name) ? card.Candidate.Name : card.Name.Trim();
         for (int i = 2; names.Contains(tag); i++) tag = $"{card.Name.Trim()} ({i})";
         names.Add(tag);
         section.Tag = tag;
         sections.Add(section);
      }

      try
      {
         _mvm.db.SaveNewCrossSections(sections);
         _mvm.RefreshSectionLiveCollections(); // дерево проекта строится из производных списков
      }
      catch (Exception e)
      {
         _mvm.LogService.Error(string.Format(Loc.S("CadImpSaveFailed"), e.Message));
         return;
      }
      RefreshLayerMaterials(); // созданные по классу материалы — теперь материалы проекта, их Id запоминаются
      RememberSettings();

      foreach (var e in errors.Distinct()) _mvm.LogService.Warning(e);
      string msg = string.Format(Loc.S("CadImpCreated"), sections.Count, SourceText);
      if (created.Count > 0) msg += " " + string.Format(Loc.S("CadImpMaterialsCreated"), string.Join(", ", created.Values.Select(m => m.Tag)));
      _mvm.LogService.Info(msg);
      StatusText = msg;
      foreach (var c in chosen) c.IsSelected = false;
   }

   void RememberSettings()
   {
      var s = _mvm.AcadImportSettings;
      s.ScaleFactor = MetersPerUnit;
      s.SectionArcMode = (CadArcMode)_arcModeIdx;
      s.SectionMergeIdentical = _merge;
      foreach (var row in Layers)
      {
         s.SectionLayerRoles[row.Name] = row.Role.Role;
         if (row.Material?.Existing is { } m) s.SectionLayerMaterials[row.Name] = m.Id;
      }
      _mvm.db.SaveAcadImportSettings(s);
   }

   /// <summary>После создания материалов по классу — обновить выбор в таблице (теперь это материалы проекта).</summary>
   public void RefreshLayerMaterials()
   {
      foreach (var row in Layers) row.RefreshMaterials(row.Material?.Existing?.Id);
   }
}

using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Text.Json;

using CScore;
using CScore.PlateStrip;
using CScore.Import;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.ViewModels;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;

using CsvHelper;
using CsvHelper.Configuration;

using netDxf;
using netDxf.Entities;
using netDxf.Tables;

namespace OpenCS
{
   /// <summary>
   /// Главная модель представления приложения. Центральный узел MVVM-архитектуры,
   /// обеспечивающий навигацию, управление коллекциями доменных объектов и доступ
   /// к сервисам логирования и файловых диалогов. Все дочерние ViewModel
   /// ссылаются на экземпляр данного класса для доступа к базе данных и общим данным.
   /// </summary>
   public class AppViewModel : ViewModelBase
   {
      /// <summary>
      /// Сервис работы с базой данных SQLite, используемый для загрузки, сохранения
      /// и удаления доменных объектов. Доступен внутри сборки для дочерних ViewModel.
      /// </summary>
      internal DatabaseService db = null!;

      /// <summary>
      /// Реестр поставщиков отчётов. Новый тип отчёта добавляется реализацией
      /// <see cref="OpenCS.Reporting.IReportProvider"/> и включением его в этот список.
      /// </summary>
      public OpenCS.Reporting.ReportProviderRegistry ReportProviders { get; } =
         new([
            new OpenCS.Reporting.StrainStateReportProvider(),
            new OpenCS.Reporting.LimitForceReportProvider(),
            new OpenCS.Reporting.CrackingReportProvider(),
            new OpenCS.Reporting.CrackWidthReportProvider(),
            new OpenCS.Reporting.TotalCurvatureReportProvider(),
            new OpenCS.Reporting.Sp63NormalReportProvider(),
            new OpenCS.Reporting.ShearInclinedReportProvider(),
            new OpenCS.Reporting.Sp63CrackWidthReportProvider(),
            new OpenCS.Reporting.Sp63DeflectionReportProvider(),
            new OpenCS.Reporting.ShellLayeredCrackWidthReportProvider()
         ]);

      readonly string databasePath;

      /// <summary>
      /// Коллекция материалов типа «Бетон», отфильтрованная из <see cref="Materials"/>.
      /// Используется для привязки в представлениях, где требуется выбор только бетонных материалов.
      /// </summary>
      ObservableCollection<Material> concretes = [];

      /// <summary>
      /// Коллекция материалов типа «Арматурная сталь» (физический или условный предел текучести),
      /// отфильтрованная из <see cref="Materials"/>.
      /// Используется для привязки в представлениях выбора арматуры.
      /// </summary>
      ObservableCollection<Material> armatures = [];

      /// <summary>
      /// Коллекция материалов типа «Сталь для строительных конструкций»,
      /// отфильтрованная из <see cref="Materials"/>.
      /// </summary>
      ObservableCollection<Material> steels = [];

      /// <summary>
      /// Активная (выделенная) коллекция точек контура, отображаемая в текущем представлении.
      /// </summary>
      ObservableCollection<StressPoint> pointsLive = null!;

      /// <summary>
      /// Активная (выделенная) коллекция окружностей, отображаемая в текущем представлении.
      /// </summary>
      ObservableCollection<CircleP> circlesLive = null!;

      /// <summary>
      /// Активная (выделенная) коллекция волокон, отображаемая в текущем представлении.
      /// </summary>
      ObservableCollection<Fiber> fibersLive = null!;

      /// <summary>
      /// Активная (выделенная) коллекция контуров, отображаемая в текущем представлении.
      /// </summary>
      ObservableCollection<ContourVM> contoursLive = null!;

      /// <summary>
      /// Текущая страница (UserControl), отображаемая в области содержимого главного окна.
      /// Используется для навигации между представлениями.
      /// </summary>
      UserControl currentPage = null!;

      /// <summary>
      /// Текущий выбранный материал. При изменении открывает страницу редактирования материала.
      /// </summary>
      Material? currentMaterial;

      /// <summary>
      /// Текущий выбранный контур (ViewModel). При изменении открывает страницу контура.
      /// </summary>
      ContourVM? currentContour;

      /// <summary>
      /// Элемент дерева навигации, связанный с текущим представлением.
      /// Используется внутренне для синхронизации выделения в TreeView.
      /// </summary>
      internal TreeViewItem treeItem = null!;

      /// <summary>
      /// Текущая выбранная диаграмма. При установке значения открывает страницу диаграммы.
      /// </summary>
      Diagramm? currentDiagram;

      CrossSection? currentCrossSection;
      ObservableCollection<CrossSection> crossSectionsLive = [];
      readonly ObservableCollection<ParametricCrossSectionTreeItem> parametricFiberSectionsLive = [];
      readonly ObservableCollection<ParametricSteelSectionTreeItem> parametricSteelSectionsLive = [];
      MaterialArea? currentMaterialArea;
       ForceSet? currentBarForceSet;
       ForceSet? currentShellForceSet;
       PlateSection? currentPlateSection;
       EquivalentSection? currentEquivalentSection;
       FireSectionDef? currentFireSection;
      CScore.Fem.FemSchema? currentFemSchema;
      ViewModels.FemSchemaEditorVM? activeFemSchemaEditor;
      CScore.Fem.FemMemberGroup? currentFemMember;
      CScore.Fem.FemCheck?  currentFemCheck;
      Views.FemSectionStateWindow? _femSectionStateWindow;

      /// <summary>
      /// Путь к текущему файлу проекта. null если проект ещё не был сохранён.
      /// </summary>
      public string? CurrentProjectPath { get; private set; }

      /// <summary>
      /// Признак несохранённых изменений (данные в памяти, требующие SaveAll).
      /// </summary>
      public bool IsDirty => db.NeedsSave;

      /// <summary>Пометить категорию данных для SaveAll и обновить привязки.</summary>
      public void MarkDirty(SaveCategory category = SaveCategory.None)
      {
         if (category != SaveCategory.None)
            db.MarkPending(category);
         NotifyDirtyChanged();
      }

      /// <summary>Обновить привязку IsDirty без изменения состояния.</summary>
      public void NotifyDirtyChanged() => OnPropertyChanged(nameof(IsDirty));

      /// <summary>Сбросить все признаки несохранённых изменений.</summary>
      public void ClearDirty()
      {
         db.ClearPendingSave();
         NotifyDirtyChanged();
      }

      /// <summary>Пометить набор усилий изменённым (отложенное SaveAll).</summary>
      public void TouchForceSet(ForceSet fs)
      {
         fs.IsModified = true;
         NotifyDirtyChanged();
      }

      /// <summary>Обновить отображение набора усилий в TreeView после смены имени.</summary>
      public void RefreshForceSetInTree(ForceSet fs)
      {
         var col = fs.Kind == "shell" ? ShellForceSets : BarForceSets;
         int idx = col.IndexOf(fs);
         if (idx >= 0) { col.RemoveAt(idx); col.Insert(idx, fs); }
      }

      /// <summary>
      /// Генерируется когда <see cref="MaterialArea.SigSp"/> изменяется извне (например,
      /// через «Применить» результатов потерь преднапряжения). Аргумент — Id области.
      /// </summary>
      public event EventHandler<int>? MaterialAreaSigSpChanged;
      public void RaiseAreaSigSpChanged(int areaId) =>
          MaterialAreaSigSpChanged?.Invoke(this, areaId);

      /// <summary>
      /// Заголовок окна приложения. Содержит имя текущего файла проекта.
      /// </summary>
      public string ProjectTitle
      {
         get
         {
             var name = string.IsNullOrEmpty(CurrentProjectPath) ? Loc.S("Untitled") : Path.GetFileName(CurrentProjectPath);
             return string.Format(Loc.S("TitleFormat"), name);
         }
      }

      string _statusMessage = "";
      public string StatusMessage
      {
         get => _statusMessage;
         set { _statusMessage = value; OnPropertyChanged(); }
      }

      bool _isBusy;
      public bool IsBusy
      {
         get => _isBusy;
         set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCancelBusy)); }
      }

      /// <summary>Текущую длительную операцию можно отменить (запущена через <see cref="BeginBusyWithCancellation"/>):
      /// только тогда в строке состояния видна кнопка «Отмена».</summary>
      public bool CanCancelBusy => _isBusy && _busyCts != null;

      double _busyProgress;
      /// <summary>Прогресс длительной операции (0…1) для StatusBar.</summary>
      public double BusyProgress
      {
         get => _busyProgress;
         set { _busyProgress = value; OnPropertyChanged(); }
      }

      bool _isBusyProgressIndeterminate = true;
      /// <summary>true — бегущий индикатор; false — определённый BusyProgress.</summary>
      public bool IsBusyProgressIndeterminate
      {
         get => _isBusyProgressIndeterminate;
         set { _isBusyProgressIndeterminate = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Имя файла проекта для отображения в заголовке дерева.
      /// </summary>
      public string ProjectFileName =>
          string.IsNullOrEmpty(CurrentProjectPath)
              ? Loc.S("Untitled")
              : Path.GetFileNameWithoutExtension(CurrentProjectPath);

      /// <summary>
      /// Сервис логирования. Предоставляет методы <c>Info</c>, <c>Warning</c>, <c>Error</c>
      /// для вывода сообщений в журнал приложения. Инжектируется через конструктор.
      /// </summary>
      public ILogService LogService { get; }

      /// <summary>
      /// Сервис файловых диалогов. Предоставляет методы <c>OpenFile</c> и <c>SaveFile</c>
      /// для выбора файлов пользователем. Инжектируется через конструктор.
      /// </summary>
      public IFileDialogService FileDialogService { get; }

      /// <summary>
      /// Коллекция характеристик материалов (MaterialChars), загруженных из базы данных.
      /// Используется в привязках для отображения справочных данных по материалам.
      /// </summary>
      public ObservableCollection<MaterialChars> MaterialChars { get; set; } = null!;

      /// <summary>
      /// Полная коллекция всех материалов проекта, загруженных из базы данных.
      /// Включает бетон, арматурную сталь и сталь конструкций.
      /// Используется для привязки в представлениях списков материалов.
      /// </summary>
      public ObservableCollection<Material> Materials { get; set; } = null!;

      /// <summary>
      /// Коллекция всех точек контура проекта, загруженных из базы данных.
      /// </summary>
      public ObservableCollection<StressPoint> Points { get; set; } = null!;

      /// <summary>
      /// Коллекция всех окружностей проекта, загруженных из базы данных.
      /// Используется для привязки в представлениях окружностей (в том числе из DXF).
      /// </summary>
      public ObservableCollection<CircleP> Circles { get; set; } = null!;

      /// <summary>
      /// Коллекция всех волокон проекта, загруженных из базы данных.
      /// </summary>
      public ObservableCollection<Fiber> Fibers { get; set; } = null!;

      /// <summary>
      /// Коллекция всех контуров проекта, загруженных из базы данных.
      /// Используется для привязки в TreeView и представлениях выбора контура.
      /// </summary>
      public ObservableCollection<Contour> Contours { get; set; } = null!;

      /// <summary>
      /// Коллекция всех диаграмм работы материалов проекта.
      /// </summary>
      public ObservableCollection<Diagramm> Diagrams { get; set; } = null!;

      /// <summary>Коллекция поперечных сечений проекта.</summary>
      public ObservableCollection<CrossSection> CrossSections { get; set; } = null!;

      /// <summary>Отфильтрованная коллекция сечений для отображения в TreeView.</summary>
      public ObservableCollection<CrossSection> CrossSectionsLive
      {
         get => crossSectionsLive;
         set { crossSectionsLive = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Текущее выбранное поперечное сечение. Открывает редактор в зависимости от типа.
      /// </summary>
      public CrossSection? CurrentCrossSection
      {
         get => currentCrossSection;
         set
         {
            currentCrossSection = value;
            if (value is TwoStageSection tss)
               CurrentPage = new Views.TwoStageSectionEditorPage(tss, this);
            else if (value != null)
               CurrentPage = new Views.CrossSectionPage(value, this);
            else
               CurrentPage = null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Коллекция самостоятельных MaterialArea проекта.</summary>
      public ObservableCollection<MaterialArea> MaterialAreas { get; set; } = null!;

      /// <summary>Области с полигональной геометрией (Category == Region).</summary>
      public ObservableCollection<MaterialArea> AreasLive { get; set; } = [];

      /// <summary>Группы арматурных стержней (Category == RebarGroup).</summary>
      public ObservableCollection<MaterialArea> RebarGroupsLive { get; set; } = [];

      /// <summary>Области поперечного армирования проекта.</summary>
      public ObservableCollection<MaterialArea> StirrupGroupsLive { get; private set; } = [];

      /// <summary>Простые фибровые сечения (не TwoStageSection).</summary>
      public ObservableCollection<CrossSection> FiberSectionsLive { get; } = [];

      /// <summary>Обычные и stale фибровые сечения для существующего дерева.</summary>
      public ObservableCollection<CrossSection> OrdinaryFiberSectionsLive { get; } = [];

      /// <summary>Поддержанные параметрические сечения; фактический CrossSection остаётся общим.</summary>
      public ObservableCollection<ParametricCrossSectionTreeItem> ParametricFiberSectionsLive => parametricFiberSectionsLive;

      /// <summary>Поддержанные неустаревшие параметрические МК-сечения; CrossSection остаётся общим.</summary>
      public ObservableCollection<ParametricSteelSectionTreeItem> ParametricSteelSectionsLive => parametricSteelSectionsLive;

      /// <summary>Двухстадийные сечения (TwoStageSection).</summary>
      public ObservableCollection<CrossSection> TwoStageSectionsLive { get; } = [];

       /// <summary>Плитные сечения для дерева (синхронизируется с PlateSections).</summary>
       public ObservableCollection<PlateSection> PlateSectionsLive { get; } = [];

       /// <summary>Эквивалентные сечения полос плиты для дерева проекта.</summary>
       public ObservableCollection<EquivalentSection> EquivalentSectionsLive { get; } = [];

      /// <summary>Объединённая коллекция для дерева сечений: обычные + Усиление + Пластины.</summary>
      public System.Windows.Data.CompositeCollection SectionTreeItems { get; }

      /// <summary>Наборы расчётных усилий.</summary>
      public ObservableCollection<ForceSet> ForceSets { get; set; } = null!;

      /// <summary>Расчётные задачи проекта.</summary>
      public ObservableCollection<CalcTask> CalcTasks { get; set; } = null!;

      /// <summary>Результаты расчётных задач.</summary>
      public ObservableCollection<CalcResult> CalcResults { get; set; } = null!;

      /// <summary>МКЭ-расчётные схемы проекта.</summary>
      public ObservableCollection<CScore.Fem.FemSchema> FemSchemas { get; set; } = null!;

      /// <summary>Нормативные проверки по МКЭ-пайплайну.</summary>
      public ObservableCollection<CScore.Fem.FemCheck> FemChecks { get; set; } = null!;

      /// <summary>Корневые узлы дерева МКЭ: «Расчётные схемы» и «Проверки».</summary>
      public ObservableCollection<object> FemRootNodes { get; } = [];

      ViewModels.FemSchemasGroupNode? femSchemasGroup;
      ViewModels.FemChecksRootNode?   femChecksRoot;

      /// <summary>Наборы усилий для стержней (Kind="bar").</summary>
      public ObservableCollection<ForceSet> BarForceSets { get; set; } = null!;

      /// <summary>Наборы усилий для пластин (Kind="shell").</summary>
      public ObservableCollection<ForceSet> ShellForceSets { get; set; } = null!;

       /// <summary>Плитные сечения.</summary>
       public ObservableCollection<PlateSection> PlateSections { get; set; } = null!;

       /// <summary>Сохранённые эквивалентные сечения полос плиты.</summary>
       public ObservableCollection<EquivalentSection> EquivalentSections { get; set; } = null!;

      /// <summary>Огневые сечения проекта.</summary>
      public ObservableCollection<FireSectionDef> FireSections { get; set; } = null!;

      /// <summary>Текущее выбранное плитное сечение. При установке открывает PlateSectionPage.</summary>
       public PlateSection? CurrentPlateSection
      {
         get => currentPlateSection;
         set
         {
            currentPlateSection = value;
            CurrentPage = value != null
               ? new Views.PlateSectionPage(value, this)
               : null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Текущее выбранное огневое сечение. При установке открывает FireSectionView.</summary>
      public FireSectionDef? CurrentFireSection
      {
         get => currentFireSection;
         set
         {
            currentFireSection = value;
            CurrentPage = value != null
               ? new Views.FireSectionView(value, this)
               : null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Текущая МКЭ-расчётная схема. При установке открывает FemSchemaPage.</summary>
      public CScore.Fem.FemSchema? CurrentFemSchema
      {
         get => currentFemSchema;
         set
         {
            if (ReferenceEquals(value, currentFemSchema) && currentPage is Views.FemSchemaPage)
               return;
            if (!TryLeaveFemSchemaEditor())
               return;

            currentFemSchema = value;
            CurrentPage = value != null ? new Views.FemSchemaPage(value, this) : null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Регистрирует редактор FEM, пока его сессия существует в памяти.</summary>
      public void RegisterFemSchemaEditor(ViewModels.FemSchemaEditorVM editor) => activeFemSchemaEditor = editor;

      bool openSubmodelTabRequested;

      /// <summary>Открывает схему; у субмодели — сразу на вкладке «Субмодель» (после извлечения).</summary>
      public void OpenFemSchema(CScore.Fem.FemSchema schema, bool openSubmodelTab)
      {
         openSubmodelTabRequested = openSubmodelTab;
         CurrentFemSchema = schema;
         openSubmodelTabRequested = false;
      }

      /// <summary>
      /// Пересоздаёт страницу текущей схемы в обход проверки <see cref="CurrentFemSchema"/> на ту же схему:
      /// после замены конструктивного слоя (материализация субмодели) новая сессия редактора читает его из БД.
      /// Прежний редактор снимается с регистрации без вопроса о несохранённых правках — их потерю подтверждает
      /// вызывающий. Счётчики дерева обновляются.
      /// </summary>
      public void ReloadFemSchemaPage(bool openSubmodelTab = false)
      {
         if (currentFemSchema is not { } schema) return;
         activeFemSchemaEditor = null;
         openSubmodelTabRequested = openSubmodelTab;
         CurrentPage = new Views.FemSchemaPage(schema, this);
         openSubmodelTabRequested = false;
         RefreshFemSchemaTreeCounts(schema);
      }

      /// <summary>Открывает редактор схемы и в нём — свойства узла (из дерева схемы).</summary>
      public void OpenFemNodeInEditor(CScore.Fem.FemSchema schema, string nodeTag)
      {
         CurrentFemSchema = schema;
         // Диалог — после показа страницы: у только что созданной ещё нет окна-владельца.
         if (currentFemSchema == schema && CurrentPage is Views.FemSchemaPage page)
            page.Dispatcher.BeginInvoke(() => page.OpenNodeProperties(nodeTag),
               System.Windows.Threading.DispatcherPriority.Loaded);
      }

      /// <summary>Страница схемы спрашивает при создании, открыть ли вкладку «Субмодель».</summary>
      public bool OpenSubmodelTabRequested => openSubmodelTabRequested;

      /// <summary>Снимает регистрацию редактора FEM при закрытии его страницы.</summary>
      public void UnregisterFemSchemaEditor(ViewModels.FemSchemaEditorVM editor)
      {
         if (ReferenceEquals(activeFemSchemaEditor, editor))
            activeFemSchemaEditor = null;
      }

      bool TryLeaveFemSchemaEditor()
      {
         var editor = activeFemSchemaEditor;
         if (editor == null || !editor.Session.IsDirty)
            return true;

         var result = MessageBox.Show(
            Loc.S("ConfirmSaveOnExit"),
            Loc.S("Confirmation"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

         if (result == MessageBoxResult.Cancel)
            return false;
         if (result == MessageBoxResult.Yes)
         {
            editor.Save();
            if (editor.Session.IsDirty)
               return false;
         }

         activeFemSchemaEditor = null;
         return true;
      }

      /// <summary>Текущая группа конструктивных элементов МКЭ. При установке открывает FemMemberEditorPage.</summary>
      public CScore.Fem.FemMemberGroup? CurrentFemMember
      {
         get => currentFemMember;
         set
         {
            currentFemMember = value;
            if (value != null)
               CurrentPage = new Views.FemMemberEditorPage(value, this);
            OnPropertyChanged();
         }
      }

      /// <summary>Текущая нормативная проверка МКЭ.</summary>
      public CScore.Fem.FemCheck? CurrentFemCheck
      {
         get => currentFemCheck;
         set
         {
            currentFemCheck = value;
            OnPropertyChanged();
         }
      }

      /// <summary>Команда создания новой МКЭ-схемы.</summary>
      public ICommand NewFemSchemaCommand    { get; set; } = null!;
      /// <summary>Команда удаления МКЭ-схемы.</summary>
      public ICommand DeleteFemSchemaCommand { get; set; } = null!;
      /// <summary>Команда дублирования МКЭ-схемы (топология, нагрузки, сетка; без постановок расчётов).</summary>
      public ICommand DuplicateFemSchemaCommand { get; set; } = null!;
      /// <summary>Команда переименования МКЭ-схемы.</summary>
      public ICommand RenameFemSchemaCommand { get; set; } = null!;
      /// <summary>Дозагрузить к схеме файл подобранной ЛИРОЙ арматуры (*.asp).</summary>
      public ICommand LoadLiraAspCommand { get; set; } = null!;
      /// <summary>Создать сечения пластинчатых целей схемы по данным ЛИРЫ (ASP + ТЗА).</summary>
      public ICommand CreateLiraPlateSectionsCommand { get; set; } = null!;
      public ICommand CreateImportedBarSectionsCommand { get; set; } = null!;
      public ICommand ConvertLiraBlocksCommand { get; set; } = null!;
      /// <summary>Дозагрузить к схеме файл описаний ТЗА ЛИРЫ (.RBT).</summary>
      public ICommand LoadLiraRbtCommand { get; set; } = null!;
      /// <summary>Обновить номера ТЗА у КЭ схемы из открытой в ЛИРЕ схемы (таблица «Элементы - ТЗА»).</summary>
      public ICommand RefreshLiraReinforcementTypesCommand { get; set; } = null!;
      public ICommand RefreshLiraPlateAxesCommand { get; set; } = null!;
      public ICommand RefreshLiraStiffnessesCommand { get; set; } = null!;
      /// <summary>Команда создания нового конструктивного элемента МКЭ (без диалога).</summary>
      public ICommand NewFemMemberCommand       { get; set; } = null!;
      /// <summary>Команда создания нового конструктивного элемента через диалог ввода имени/типа/КЭ.</summary>
      public ICommand NewFemMemberDialogCommand { get; set; } = null!;
      /// <summary>Команда создания пустой группы КонЭ (узел «Группы КонЭ» дерева).</summary>
      public ICommand NewFemMembersGroupCommand { get; set; } = null!;
      /// <summary>Команда переименования группы КЭ или КонЭ.</summary>
      public ICommand RenameFemMemberGroupCommand { get; set; } = null!;
      public ICommand CreateFemMembersFromMeshGroupCommand { get; set; } = null!;
      /// <summary>Команда включения режима создания плиты кликами по узлам в 3D-виде схемы.</summary>
      public ICommand CreatePlateModeCommand { get; set; } = null!;
      /// <summary>Команда включения режима создания стены кликами по узлам в 3D-виде схемы.</summary>
      public ICommand CreateWallModeCommand { get; set; } = null!;
      /// <summary>Команда включения режима создания произвольной пластины кликами по узлам.</summary>
      public ICommand CreateSpatialPlateModeCommand { get; set; } = null!;
      /// <summary>Команда удаления конструктивного элемента МКЭ.</summary>
      public ICommand DeleteFemMemberCommand { get; set; } = null!;
      /// <summary>Команда добавления нормативной проверки к элементу.</summary>
      public ICommand AddFemCheckCommand     { get; set; } = null!;
      /// <summary>Команда создания постановки линейного OpenSees-расчёта схемы.</summary>
      public ICommand CreateFemAnalysisCommand { get; set; } = null!;
      public ICommand EditFemAnalysisCommand { get; set; } = null!;
      public ICommand ViewFemAnalysisResultCommand { get; set; } = null!;
      /// <summary>Команда запуска линейного OpenSees-расчёта схемы.</summary>
      public ICommand RunFemAnalysisCommand    { get; set; } = null!;
      /// <summary>Команда удаления постановки линейного расчёта.</summary>
      public ICommand DeleteFemAnalysisCommand { get; set; } = null!;
      /// <summary>Команда запуска нормативной проверки.</summary>
      public ICommand RunFemCheckCommand     { get; set; } = null!;
      /// <summary>Команда редактирования нормативной проверки.</summary>
      public ICommand EditFemCheckCommand    { get; set; } = null!;
      /// <summary>Команда удаления нормативной проверки.</summary>
      public ICommand DeleteFemCheckCommand     { get; set; } = null!;
      /// <summary>Команда удаления всех нормативных проверок.</summary>
      public ICommand DeleteAllFemChecksCommand { get; set; } = null!;
      /// <summary>Команда добавления проверки 2-й ГПС (SLS-диалог).</summary>
      public ICommand AddSlsFemCheckCommand     { get; set; } = null!;
      /// <summary>Команда добавления проверки по ключу группы (диспетчер uls/sls).</summary>
      public ICommand AddFemCheckByGroupCommand { get; set; } = null!;
      /// <summary>Команда показа эпюр вдоль стержней группы или конструктивного элемента.</summary>
      public ICommand ShowBarDiagramsCommand    { get; set; } = null!;

      /// <summary>Команда удаления всех наборов усилий схемы МКЭ.</summary>
       public ICommand DeleteFemSchemaForceSetsCommand { get; set; } = null!;
       public ICommand DeleteSelectedForceSetsCommand { get; set; } = null!;

      /// <summary>Команда импорта расчётной схемы из CSV-файлов ЛираСАПР.</summary>
      public ICommand ImportLiraSchemaFromCsvCommand { get; set; } = null!;

      /// <summary>Команда импорта расчётной схемы из .lir файла ЛираСАПР.</summary>
      public ICommand ImportLiraSchemaFromFileCommand { get; set; } = null!;

      /// <summary>Команда импорта топологии расчётной схемы из текстового формата SCAD.</summary>
      public ICommand ImportScadSchemaFromApiCommand { get; set; } = null!;
      public ICommand ImportScadTopologyFromTxtCommand { get; set; } = null!;

      /// <summary>Команда импорта стержневых усилий (загружения) из XLS-отчёта SCAD.</summary>
      public ICommand ImportScadForcesLoadCasesCommand { get; set; } = null!;

      /// <summary>Команда импорта стержневых усилий (РСУ) из XLS-отчёта SCAD.</summary>
      public ICommand ImportScadForcesRsuCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий РСН (комбинации) из XLS-отчёта SCAD.</summary>
      public ICommand ImportScadForcesCombinationsCommand { get; set; } = null!;

      /// <summary>Команда импорта расчётных сочетаний из бинарного файла SCAD RSU2.</summary>
      public ICommand ImportScadRsu2Command { get; set; } = null!;

      /// <summary>Команда импорта расчётной схемы из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraSchemaFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий ЗН из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraForcesFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий РСН из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraRsnFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий РСУ из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraRsuFromApiCommand { get; set; } = null!;

      /// <summary>Усилия загружений из проекта SCAD (.SPR) через SCADAPIX.dll (параметр — группа, элемент или схема).</summary>
      public ICommand ImportScadLoadCasesFromApiCommand { get; set; } = null!;

      /// <summary>Усилия комбинаций загружений (РСН) из проекта SCAD (.SPR) через SCADAPIX.dll.</summary>
      public ICommand ImportScadCombinationsFromApiCommand { get; set; } = null!;

      /// <summary>РСУ из проекта SCAD (.SPR) через SCADAPIX.dll.</summary>
      public ICommand ImportScadRsuFromApiCommand { get; set; } = null!;

      /// <summary>Указать файл проекта SCAD (.SPR) для схемы (параметр FemSchema).</summary>
      public ICommand SetScadProjectPathCommand { get; set; } = null!;
      /// <summary>Загрузить подбор арматуры SCAD (выгрузка плагина) к схеме SCAD; параметр — схема или null (текущая).</summary>
      public ICommand LoadScadSelectedRebarCommand { get; set; } = null!;
      /// <summary>Установить плагин «Экспорт для OpenCS» в SCAD.</summary>
      public ICommand InstallScadPluginCommand { get; set; } = null!;
      /// <summary>Перечитать ЖБ-группы и заданное армирование схемы SCAD из .SPR (параметр FemSchema).</summary>
      public ICommand RefreshScadRebarDataCommand { get; set; } = null!;
      /// <summary>Перенести нагрузки из вложения SCAD в нагрузки сеточного уровня схемы.</summary>
      public ICommand TransferScadLoadsCommand { get; set; } = null!;
      public ICommand RefreshScadBoundaryCommand { get; set; } = null!;

      /// <summary>Команда создания нового плитного сечения.</summary>
      public ICommand NewPlateSectionCommand { get; set; } = null!;
      /// <summary>Команда удаления плитного сечения (параметр PlateSection или текущее).</summary>
      public ICommand DeletePlateSectionCommand { get; set; } = null!;
       /// <summary>Команда дублирования плитного сечения (параметр PlateSection).</summary>
       public ICommand DuplicatePlateSectionCommand { get; set; } = null!;
       /// <summary>Команда удаления эквивалентного сечения.</summary>
       public ICommand DeleteEquivalentSectionCommand { get; set; } = null!;
       /// <summary>Команда повторного расчёта эквивалентного сечения.</summary>
       public ICommand RecalculateEquivalentSectionCommand { get; set; } = null!;

      /// <summary>Команда открытия страницы расчётных задач.</summary>
      public ICommand OpenCalcTasksCommand { get; set; } = null!;
      /// <summary>Команда отмены текущей длительной операции (StatusBar).</summary>
      public ICommand CancelBusyCommand { get; set; } = null!;
      /// <summary>Команда создания новой задачи из контекстного меню дерева.</summary>
      public ICommand NewCalcTaskCommand    { get; set; } = null!;
      /// <summary>Команда запуска задачи (параметр CalcTask).</summary>
      public ICommand RunCalcTaskCommand    { get; set; } = null!;
      /// <summary>Команда редактирования задачи (параметр CalcTask).</summary>
      public ICommand EditCalcTaskCommand   { get; set; } = null!;
      /// <summary>Команда удаления задачи (параметр CalcTask).</summary>
      public ICommand DeleteCalcTaskCommand { get; set; } = null!;
      /// <summary>Команда удаления всех результатов задачи (параметр CalcTask).</summary>
      public ICommand DeleteCalcResultsCommand { get; set; } = null!;
      /// <summary>Команда экспорта отчёта по результату расчёта (параметр CalcResult).</summary>
      public ICommand ExportCalcResultReportCommand { get; set; } = null!;

      /// <summary>Поднимается при изменении свойств существующей задачи (не добавлении/удалении).</summary>
      public event Action? CalcTaskModified;

      /// <summary>Команда создания нового огневого сечения.</summary>
      public ICommand NewFireSectionCommand { get; set; } = null!;
      /// <summary>Команда удаления выбранного огневого сечения.</summary>
      public ICommand DeleteFireSectionCommand { get; set; } = null!;
      /// <summary>Команда переименования/редактирования выбранного огневого сечения.</summary>
      public ICommand RenameFireSectionCommand { get; set; } = null!;

      /// <summary>Текущий выбранный набор усилий стержня. При установке открывает BarForceSetPage.</summary>
      public ForceSet? CurrentBarForceSet
      {
         get => currentBarForceSet;
         set
         {
            currentBarForceSet = value;
            CurrentPage = value != null
               ? new Views.BarForceSetPage(value, this)
               : null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Текущий выбранный набор усилий пластины. При установке открывает ShellForceSetPage.</summary>
      public ForceSet? CurrentShellForceSet
      {
         get => currentShellForceSet;
         set
         {
            currentShellForceSet = value;
            CurrentPage = value != null
               ? new Views.ShellForceSetPage(value, this)
               : null!;
            OnPropertyChanged();
         }
      }

      /// <summary>Текущая выбранная MaterialArea. Открывает MaterialAreaPage.</summary>
      public MaterialArea? CurrentMaterialArea
      {
         get => currentMaterialArea;
         set
         {
            currentMaterialArea = value;
            if (value != null)
               CurrentPage = value.Category switch
               {
                  AreaCategory.RebarGroup => new Views.RebarGroupEditorPage(value, this),
                  AreaCategory.Stirrups => new Views.StirrupGroupPage(value, this),
                  _ => new Views.MaterialAreaPage(value, this)
               };
            OnPropertyChanged();
         }
      }

      /// <summary>Команда создания новой полигональной области.</summary>
      public ICommand NewAreaCommand { get; set; } = null!;

      /// <summary>Команда удаления текущей MaterialArea.</summary>
      public ICommand DeleteMaterialAreaCommand { get; set; } = null!;

      /// <summary>Команда создания новой группы арматурных стержней.</summary>
      public ICommand NewRebarGroupCommand { get; set; } = null!;

      /// <summary>Команда создания новой группы поперечного армирования.</summary>
      public ICommand NewStirrupGroupCommand { get; set; } = null!;

      /// <summary>Команда создания нового поперечного сечения.</summary>
      public ICommand NewCrossSectionCommand { get; set; } = null!;

      /// <summary>Команда создания параметрического ЖБ-сечения.</summary>
      public ICommand NewParametricCrossSectionCommand { get; set; } = null!;
      /// <summary>Команда пересборки параметрического ЖБ-сечения.</summary>
      public ICommand RebuildParametricCrossSectionCommand { get; set; } = null!;
      /// <summary>Команда редактирования поддержанного параметрического сечения.</summary>
      public ICommand EditParametricCrossSectionCommand { get; set; } = null!;
      /// <summary>Команда открытия сформированного сечения без изменения параметров.</summary>
      public ICommand OpenParametricCrossSectionCommand { get; set; } = null!;

      /// <summary>Команда создания параметрического МК-сечения.</summary>
      public ICommand NewParametricSteelSectionCommand { get; set; } = null!;
      /// <summary>Команда пересборки параметрического МК-сечения.</summary>
      public ICommand RebuildParametricSteelSectionCommand { get; set; } = null!;
      /// <summary>Команда редактирования параметров МК-сечения.</summary>
      public ICommand EditParametricSteelSectionCommand { get; set; } = null!;
      /// <summary>Команда открытия сформированного МК-сечения.</summary>
      public ICommand OpenParametricSteelSectionCommand { get; set; } = null!;

      /// <summary>Команда редактирования выбранного поперечного сечения.</summary>
      public ICommand EditCrossSectionCommand { get; set; } = null!;

      /// <summary>Команда удаления выбранного поперечного сечения.</summary>
      public ICommand DeleteCrossSectionCommand { get; set; } = null!;

      /// <summary>Команда создания нового двухстадийного сечения.</summary>
      public ICommand NewTwoStageSectionCommand { get; set; } = null!;

      /// <summary>Команда создания нового набора усилий стержня.</summary>
      public ICommand NewBarForceSetCommand { get; set; } = null!;

      /// <summary>Команда создания нового набора усилий пластины.</summary>
      public ICommand NewShellForceSetCommand { get; set; } = null!;

      /// <summary>Команда формирования сочетаний СП20 для наборов усилий стержней.</summary>
      public ICommand SP20BarCombinationsCommand { get; set; } = null!;

      /// <summary>Команда формирования сочетаний СП20 для наборов усилий пластин.</summary>
      public ICommand SP20ShellCombinationsCommand { get; set; } = null!;

      /// <summary>Команда удаления набора усилий (параметр ForceSet).</summary>
      public ICommand DeleteForceSetCommand { get; set; } = null!;

      /// <summary>Команда дублирования набора усилий (параметр ForceSet).</summary>
      public ICommand DuplicateForceSetCommand { get; set; } = null!;

      /// <summary>Команда удаления выбранных наборов усилий стержней.</summary>
      public ICommand DeleteSelectedBarForceSetsCommand { get; set; } = null!;
      /// <summary>Команда удаления выбранных наборов усилий пластин.</summary>
      public ICommand DeleteSelectedShellForceSetsCommand { get; set; } = null!;
      /// <summary>Команда удаления всех наборов усилий стержней.</summary>
      public ICommand DeleteAllBarForceSetsCommand { get; set; } = null!;
      /// <summary>Команда удаления всех наборов усилий пластин.</summary>
      public ICommand DeleteAllShellForceSetsCommand { get; set; } = null!;

      /// <summary>
      /// Отфильтрованная коллекция диаграмм для отображения в TreeView.
      /// </summary>
       public ObservableCollection<Diagramm> DiagramsLive
       {
          get => diagramsLive;
          set { diagramsLive = value; OnPropertyChanged(); }
       }
       ObservableCollection<Diagramm> diagramsLive = [];

      /// <summary>
      /// Текущая страница содержимого, отображаемая в главном окне.
      /// При изменении значения вызывается <c>OnPropertyChanged()</c> для обновления привязки.
      /// Используется для навигации между представлениями (контур, материал, область и т.д.).
      /// </summary>
      public UserControl CurrentPage
      {
         get => currentPage;
         set
         {
            if (ReferenceEquals(currentPage, value)) return;
            if (!TryLeaveFemSchemaEditor()) return;
            currentPage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPageTitle));
         }
      }

      /// <summary>
      /// Заголовок текущей вьюхи для отображения в GroupBox центральной области.
      /// </summary>
      public string CurrentPageTitle => currentPage switch
      {
          Views.ContourPlot               => Loc.S("VT_Contour"),
          Views.MaterialPage              => Loc.S("VT_Material"),
          Views.MaterialAreaPage          => Loc.S("VT_MaterialArea"),
          Views.RebarGroupEditorPage      => Loc.S("VT_RebarGroup"),
          Views.StirrupGroupPage          => Loc.S("VT_StirrupGroup"),
          Views.CrossSectionPage          => Loc.S("VT_CrossSection"),
          Views.TwoStageSectionEditorPage => Loc.S("VT_TwoStageSection"),
          Views.PlateSectionPage          => Loc.S("VT_PlateSection"),
          Views.BarForceSetPage           => Loc.S("VT_BarForceSet"),
          Views.ShellForceSetPage         => Loc.S("VT_ShellForceSet"),
          Views.FromDxfPage               => Loc.S("VT_FromDxf"),
          Views.DiagramPage               => Loc.S("VT_Diagram"),
          Views.CirclesView               => Loc.S("VT_Circles"),
          Views.CalcTasksPage             => Loc.S("VT_CalcTasks"),
          Views.CalcResultView            => Loc.S("VT_CalcResult"),
          Views.FireSectionView           => Loc.S("VT_FireSection"),
          Views.FemNodesView              => Loc.S("FemNodes"),
          Views.FemBarsView               => Loc.S("FemBars"),
          Views.FemShellsView             => Loc.S("FemShells"),
          Views.FemAnalysisResultView     => Loc.S("VT_FemAnalysisResult"),
          Views.FemMemberForceView        => Loc.S("VT_FemMemberForce"),
          _                               => ""
      };
      /// <summary>
      /// Текущий выбранный материал. При установке значения автоматически открывает
      /// страницу редактирования материала через <see cref="OnSelectMaterial"/>.
      /// </summary>
      public Material? CurrentMaterial
      {
         get => currentMaterial;
         set { currentMaterial = value; OnSelectMaterial(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Текущий выбранный контур (ViewModel). При установке значения автоматически
      /// открывает страницу отображения контура (<see cref="ContourPlot"/>).
      /// </summary>
      public ContourVM? CurrentContour
      {
         get => currentContour;
         set
         {
            currentContour = value;
            if (value != null)
               CurrentPage = new ContourPlot(this, isSaved: value.Contour.Points.Count >= 4);
            else
               CurrentPage = null!;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Текущая выбранная диаграмма. При установке значения открывает страницу диаграммы.
      /// </summary>
      public Diagramm? CurrentDiagram
      {
         get => currentDiagram;
         set
         {
            currentDiagram = value;
            if (value != null)
               CurrentPage = new DiagramPage(value, this);
            OnPropertyChanged();
         }
      }

      /// <summary>Команда создания новой пустой диаграммы σ(ε).</summary>
      public ICommand AddDiagramCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для создания нового контура. Открывает пустую страницу контура.
      /// </summary>
      public ICommand NewContourCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для создания нового материала. Открывает пустую страницу материала.
      /// </summary>
      public ICommand NewMaterialCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для добавления материала из справочника с автоматическим
      /// выбором вкладки (бетон/арматура/сталь) в окне хранилища.
      /// </summary>
      public ICommand NewMaterialFromSourceCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для добавления арматуры из справочника (вкладка 1).
      /// </summary>
      public ICommand AddRebarCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для добавления конструкционной стали из справочника (вкладка 2).
      /// </summary>
      public ICommand AddSteelCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для удаления выбранного материала
      /// с подтверждением через диалоговое окно.
      /// </summary>
      public ICommand DelMaterialCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для удаления выбранного контура и всех связанных с ним областей
      /// с подтверждением через диалоговое окно.
      /// </summary>
      public ICommand DelContourCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для импорта геометрии из DXF-файла.
      /// Открывает страницу <see cref="FromDxfPage"/>.
      /// </summary>
      public ICommand FromDxfCommand { get; set; } = null!;

      /// <summary>Команда прямого импорта замкнутых контуров из DXF без мастера.</summary>
      public ICommand ImportContoursFromDxfCommand { get; set; } = null!;

      /// <summary>Команда импорта областей из запущенного AutoCAD.</summary>
      public ICommand ImportAcadRegionsCommand { get; set; } = null!;
      /// <summary>Команда импорта групп арматуры из запущенного AutoCAD.</summary>
      public ICommand ImportAcadRebarGroupsCommand { get; set; } = null!;

      public ICommand ImportLiraLoadCasesCommand { get; set; } = null!;
      public ICommand ImportLiraRsnCommand { get; set; } = null!;
      public ICommand ImportLiraRsuCommand { get; set; } = null!;

      /// <summary>Команда добавления новой окружности вручную.</summary>
      public ICommand AddCircleCommand { get; set; } = null!;
      /// <summary>Команда удаления окружности (параметр CircleP).</summary>
      public ICommand DeleteCircleCommand { get; set; } = null!;
      /// <summary>Команда быстрого импорта окружностей из DXF (все объекты, имена по слоям).</summary>
      public ICommand ImportCirclesFromDxfCommand { get; set; } = null!;
      /// <summary>Команда экспорта окружностей проекта в DXF-файл.</summary>
      public ICommand ExportCirclesToDxfCommand { get; set; } = null!;
      /// <summary>Команда импорта окружностей из CSV-файла.</summary>
      public ICommand ImportCirclesFromCsvCommand { get; set; } = null!;
      /// <summary>Команда экспорта окружностей проекта в CSV-файл.</summary>
      public ICommand ExportCirclesToCsvCommand { get; set; } = null!;

      /// <summary>Команда создания контура из шаблона прямоугольника.</summary>
      public ICommand NewContourFromTemplateRectCommand { get; set; } = null!;
      /// <summary>Команда создания контура из шаблона тавра.</summary>
      public ICommand NewContourFromTemplateTeeCommand { get; set; } = null!;
      /// <summary>Команда создания контура из шаблона двутавра.</summary>
      public ICommand NewContourFromTemplateIBeamCommand { get; set; } = null!;
      /// <summary>Команда создания контура из шаблона уголка.</summary>
      public ICommand NewContourFromTemplateAngleCommand { get; set; } = null!;
      /// <summary>Команда создания контура из шаблона окружности.</summary>
      public ICommand NewContourFromTemplateCircleCommand { get; set; } = null!;
      /// <summary>Команда создания контура из сортамента металлопроката.</summary>
      public ICommand NewContourFromSortamentCommand { get; set; } = null!;

      /// <summary>
      /// Команда создания нового проекта. Сбрасывает все данные и создаёт пустую базу данных.
      /// </summary>
      public ICommand NewProjectCommand { get; set; } = null!;

      /// <summary>
      /// Команда открытия существующего проекта из файла базы данных SQLite.
      /// </summary>
      public ICommand OpenProjectCommand { get; set; } = null!;

      /// <summary>
      /// Команда сохранения проекта в текущий файл. Если файл не задан — выполняет SaveAs.
      /// </summary>
      public ICommand SaveProjectCommand { get; set; } = null!;

      /// <summary>
      /// Команда сохранения проекта в новый файл (Save As).
      /// </summary>
      public ICommand SaveAsProjectCommand { get; set; } = null!;

      /// <summary>
      /// Команда выхода из приложения.
      /// </summary>
      public ICommand ExitCommand { get; set; } = null!;

      /// <summary>Команда открытия единого окна настроек.</summary>
      public ICommand OpenSettingsCommand { get; set; } = null!;

      /// <summary>Команда сжатия БД (SQLite VACUUM).</summary>
      public ICommand VacuumDbCommand { get; set; } = null!;

      /// <summary>Одноразовая миграция наборов усилий ЛИРА, где Nx/Ny/Nxy содержат напряжения.</summary>
      public ICommand MigrateLiraStressForceSetsCommand { get; set; } = null!;

      /// <summary>
      /// Глобальные настройки отображения графиков (цвета, сетка, подписи).
      /// </summary>
      public Utilites.PlotSettings PlotSettings { get; set; } = Utilites.PlotSettings.Default;

      /// <summary>
      /// Вызывается при применении настроек. Передаёт новый цвет фона DXF-канваса.
      /// Подключается из <see cref="Views.FromDxfPage"/>.
      /// </summary>
      public Action<string>? DxfBgApplied { get; set; }

      /// <summary>
      /// Настройки экспорта CSV (разделитель, кодировка).
      /// </summary>
      public Utilites.CsvExportSettings CsvSettings { get; set; } = Utilites.CsvExportSettings.Default;

      /// <summary>Настройки численного расчёта (сетка, Ньютон).</summary>
      public Utilites.CalcSettings CalcSettings { get; set; } = Utilites.CalcSettings.Default;

      /// <summary>Срабатывает после сохранения настроек расчёта (Настройки → Применить/OK).</summary>
      public event Action? CalcSettingsApplied;

      /// <summary>Уведомляет открытые страницы об изменении <see cref="CalcSettings"/>.</summary>
      public void NotifyCalcSettingsApplied() => CalcSettingsApplied?.Invoke();

      /// <summary>Срабатывает после сохранения настроек графики (Настройки → Применить/OK).</summary>
      public event Action? PlotSettingsApplied;

      /// <summary>Уведомляет открытые страницы об изменении <see cref="PlotSettings"/>.</summary>
      public void NotifyPlotSettingsApplied() => PlotSettingsApplied?.Invoke();

      /// <summary>Настройки импорта усилий LIRA SAPR (HTML).</summary>
      public Utilites.LiraImportSettings LiraImportSettings { get; set; } = Utilites.LiraImportSettings.Default;

      /// <summary>Настройки прямого импорта из AutoCAD.</summary>
       public Utilites.AcadImportSettings AcadImportSettings { get; set; } = Utilites.AcadImportSettings.Default;

       /// <summary>Глобальные настройки внешнего генератора сетки Gmsh.</summary>
       public Utilites.GmshSettings GmshSettings { get; set; } = new();

      /// <summary>
      /// Перечитывает все группы глобальных настроек из текущей БД (<see cref="db"/>) — вызывается
      /// при старте приложения и при смене проекта (создание/открытие), чтобы настройки, сохранённые
      /// в файле проекта, автоматически восстанавливались вместе с ним. Если в БД настроек нет
      /// (новый/старый проект без сохранённых настроек) — используются значения по умолчанию.
      /// </summary>
      void LoadSettingsFromDb()
      {
         PlotSettings = db.LoadPlotSettings() ?? Utilites.PlotSettings.Default;
         CsvSettings = db.LoadCsvSettings() ?? Utilites.CsvExportSettings.Default;
         CalcSettings = db.LoadCalcSettings() ?? Utilites.CalcSettings.Default;
         LiraImportSettings = db.LoadLiraImportSettings() ?? Utilites.LiraImportSettings.Default;
          AcadImportSettings = db.LoadAcadImportSettings() ?? Utilites.AcadImportSettings.Default;
          GmshSettings = db.LoadGmshSettings();
         ApplyPlotSettings();
         NotifyCalcSettingsApplied();
      }

      private int langID = 0;
      /// <summary>
      /// Идентификатор текущего языка: 0 — русский, 1 — английский.
      /// </summary>
      public int LangID { get => langID; set { langID = value; OnPropertyChanged(); } }

      /// <summary>
      /// Команда переключения языка интерфейса.
      /// Параметр: 0 — русский, 1 — английский.
      /// </summary>
      public ICommand SetLanguageCommand { get; set; } = null!;

      /// <summary>
      /// Переключает словарь ресурсов приложения на указанный язык.
      /// Удаляет все языковые словари из MergedDictionaries и добавляет нужный.
      /// </summary>
      void SetLanguageDictionary(int lang)
      {
         var dicts = Application.Current.Resources.MergedDictionaries
             .Where(d => d.Source != null &&
                        (d.Source.OriginalString.Contains("Strings.en-US") ||
                         d.Source.OriginalString.Contains("Strings.ru-RU")))
             .ToList();

         foreach (var d in dicts)
            Application.Current.Resources.MergedDictionaries.Remove(d);

         ResourceDictionary dict = new();
         switch (lang)
         {
            case 0:
               dict.Source = new Uri("Resources/Strings.ru-RU.xaml", UriKind.Relative);
               break;
            case 1:
               dict.Source = new Uri("Resources/Strings.en-US.xaml", UriKind.Relative);
               break;
         }
         Application.Current.Resources.MergedDictionaries.Add(dict);
         LangID = lang;
      }

      /// <summary>
      /// Инициализирует экземпляр <see cref="AppViewModel"/>, загружает все коллекции
      /// из базы данных, настраивает обработчики изменения коллекций
      /// и создаёт команды привязки для навигации.
      /// </summary>
      /// <param name="logService">Сервис логирования, инжектируемый в ViewModel.</param>
      /// <param name="fileDialogService">Сервис файловых диалогов, инжектируемый в ViewModel.</param>
       public AppViewModel(ILogService logService, IFileDialogService fileDialogService,
                           string? databasePath = null)
       {
          LogService = logService;
          FileDialogService = fileDialogService;
          this.databasePath = string.IsNullOrWhiteSpace(databasePath)
             ? GetTempDbPath() : databasePath;
          SectionTreeItems = new System.Windows.Data.CompositeCollection
          {
              new System.Windows.Data.CollectionContainer { Collection = FiberSectionsLive },
              new ParametricCrossSectionTreeGroup(parametricFiberSectionsLive),
              new ParametricSteelSectionTreeGroup(parametricSteelSectionsLive),
              new SectionTreeGroup(TwoStageSectionsLive),
              new PlateSectionTreeGroup(PlateSectionsLive),
              new EquivalentSectionTreeGroup(EquivalentSectionsLive),
           };

          db = new DatabaseService(this.databasePath);
          InitNewDatabase();
          LoadSettingsFromDb();
          InitializeCollections();
           InitializeCommands();
        }

      static string GetTempDbPath() =>
         Path.Combine(Path.GetTempPath(), "opencs_new.db");

      /// <summary>
      /// Сбрасывает базу данных до пустого состояния (новый проект).
      /// </summary>
      void InitNewDatabase()
      {
         db.ReinitializeDatabase(databasePath);
      }

      /// <summary>
      /// Завершает работу приложения.
      /// </summary>
      private void Exit(object? o = null)
      {
         if (Application.Current.MainWindow != null)
            Application.Current.MainWindow.Close();
         else
            Application.Current.Shutdown();
      }

      void VacuumDb()
      {
         long sizeBefore = db.GetDbSizeBytes();
         db.Vacuum();
         long sizeAfter = db.GetDbSizeBytes();
         long savedKb = (sizeBefore - sizeAfter) / 1024;
         LogService.Info(string.Format(Loc.S("VacuumDbDone"), sizeBefore / 1024, sizeAfter / 1024, savedKb));
      }

      /// <summary>
      /// Одноразовая миграция: наборы усилий, импортированные из ЛИРЫ — либо напрямую
      /// (ForceSet.SourceType == "lira", HTML-путь), либо через COM API (SourceType == "fea"
      /// + SourceSchemaId → FemSchema.SourceType == "lira") — ДО фикса σ→N, где Nx/Ny/Nxy сейчас
      /// фактически содержат напряжения. Переносит текущие Nx/Ny/Nxy → SigmaX/SigmaY/TauXY и
      /// обнуляет Nx/Ny/Nxy — дальше пользователь жмёт «Напряжения → усилия» в наборе или
      /// полагается на автопересчёт в расчётных задачах.
      /// </summary>
      void MigrateLiraStressForceSets()
      {
         var liraSchemaIds = FemSchemas.Where(s => s.SourceType == "lira").Select(s => s.Id).ToHashSet();
         bool IsLiraSourced(ForceSet fs) =>
            fs.SourceType == "lira"
            || (fs.SourceType == "fea" && fs.SourceSchemaId.HasValue && liraSchemaIds.Contains(fs.SourceSchemaId.Value));

         var candidates = ForceSets
            .Where(fs => fs.Kind == "shell"
                      && IsLiraSourced(fs)
                      && fs.ShellItems.Any(i => i.SigmaX is null && i.SigmaY is null && i.TauXY is null
                                              && (i.Nx != 0 || i.Ny != 0 || i.Nxy != 0)))
            .ToList();

         if (candidates.Count == 0)
         {
            System.Windows.MessageBox.Show(Loc.S("MigrateLiraStressForceSetsNone"),
               Loc.S("MigrateLiraStressForceSets"),
               MessageBoxButton.OK, MessageBoxImage.Information);
            return;
         }

         var confirm = System.Windows.MessageBox.Show(
            string.Format(Loc.S("MigrateLiraStressForceSetsConfirm"), candidates.Count),
            Loc.S("MigrateLiraStressForceSets"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (confirm != MessageBoxResult.Yes) return;

         int rowsFixed = 0;
         foreach (var fs in candidates)
         {
            bool touched = false;
            foreach (var item in fs.ShellItems)
            {
               if (item.SigmaX is not null || item.SigmaY is not null || item.TauXY is not null) continue;
               if (item.Nx == 0 && item.Ny == 0 && item.Nxy == 0) continue;
               item.SigmaX = item.Nx;
               item.SigmaY = item.Ny;
               item.TauXY  = item.Nxy;
               item.Nx = 0; item.Ny = 0; item.Nxy = 0;
               touched = true;
               rowsFixed++;
            }
            if (touched) db.SaveForceSet(fs);
         }

         LogService.Info(string.Format(Loc.S("MigrateLiraStressForceSetsDone"), candidates.Count, rowsFixed));
      }

      /// <summary>
      /// Проверяет, нужно ли сохранять проект, и показывает диалог при необходимости.
      /// Возвращает true, если можно продолжить закрытие; false, если пользователь отменил.
      /// </summary>
      public bool ConfirmSaveIfNeeded()
      {
         if (CurrentProjectPath != null && !IsDirty)
            return true;

         if (CurrentProjectPath != null && IsDirty)
         {
            try
            {
               BeginBusy(Loc.S("SavingProject"));
               db.SaveAll();
               ClearDirty();
            }
            catch { }
            finally { EndBusy(); }
            return true;
         }

         var result = MessageBox.Show(
            Loc.S("ConfirmSaveOnExit"),
            Loc.S("Confirmation"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

         if (result == MessageBoxResult.Yes)
         {
            SaveProjectInternal();
            return CurrentProjectPath != null;
         }
         if (result == MessageBoxResult.No)
            return true;

         return false;
      }

      /// <summary>
      /// Сохраняет проект. Если путь не задан — показывает диалог «Сохранить как».
      /// </summary>
      void SaveProjectInternal()
      {
         if (CurrentProjectPath == null)
         {
            SaveAsProject();
            return;
         }
         try
         {
            BeginBusy(Loc.S("SavingProject"));
            db.SaveAll();
            ClearDirty();
            LogService.Info(Loc.S("ProjectSaved"));
         }
         catch (Exception ex)
         {
            LogService.Error(string.Format(Loc.S("ProjectSaveError"), ex.Message));
         }
         finally
         {
            EndBusy();
         }
      }

      void InitializeCollections()
      {
         MaterialChars = db.MaterialChars;
         Materials = db.Materials;
         Points = db.Points;
         Circles = db.Circles;
         Fibers = db.Fibers;
         Contours = db.Contours;
         Diagrams = db.Diagrams;
         CrossSections = db.CrossSections;
         CrossSections.CollectionChanged += (_, _) => MarkDirty(SaveCategory.CrossSections);
         ForceSets = db.ForceSets;
         BarForceSets   = new ObservableCollection<ForceSet>(ForceSets.Where(fs => fs.Kind == "bar"));
         ShellForceSets = new ObservableCollection<ForceSet>(ForceSets.Where(fs => fs.Kind == "shell"));
         ForceSets.CollectionChanged += (_, e) =>
         {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
               BarForceSets.Clear();
               ShellForceSets.Clear();
               return;
            }
            if (e.NewItems != null)
            {
               foreach (ForceSet fs in e.NewItems)
               {
                  if (fs.Id == 0 || fs.IsModified)
                     TouchForceSet(fs);
                  if (fs.Kind == "shell") { if (!ShellForceSets.Contains(fs)) ShellForceSets.Add(fs); }
                  else                    { if (!BarForceSets.Contains(fs))   BarForceSets.Add(fs); }
               }
            }
            if (e.OldItems != null)
               foreach (ForceSet fs in e.OldItems)
               {
                  BarForceSets.Remove(fs);
                  ShellForceSets.Remove(fs);
               }
         };
          PlateSections = db.PlateSections;
          PlateSections.CollectionChanged += (_, _) => { RefreshPlateSectionsLive(); MarkDirty(SaveCategory.PlateSections); };
          RefreshPlateSectionsLive();
          EquivalentSections = db.EquivalentSections;
          EquivalentSections.CollectionChanged += (_, _) => { RefreshEquivalentSectionsLive(); MarkDirty(SaveCategory.EquivalentSections); };
          RefreshEquivalentSectionsLive();
          RefreshEquivalentSectionsStale();
          FireSections = db.FireSections;
         FireSections.CollectionChanged += (_, _) =>
         {
            RenumberFireSections();
            MarkDirty(SaveCategory.FireSections);
         };
         RenumberFireSections();
         CalcTasks   = db.CalcTasks;
         CalcResults = db.CalcResults;
         FemSchemas  = db.FemSchemas;
         FemChecks   = db.FemChecks;
         BuildFemRootNodes();
         CalcTasks.CollectionChanged += (_, _) => MarkDirty(SaveCategory.CalcTasks);
         MaterialAreas = db.MaterialAreas;
         MaterialAreas.CollectionChanged += (_, _) => RefreshMaterialAreaLiveCollections();

         Materials.CollectionChanged += Concretes_CollectionChanged;
         Contours.CollectionChanged += Contours_CollectionChanged;
         Materials.CollectionChanged += (_, _) => MarkDirty(SaveCategory.Materials);
         Contours.CollectionChanged += (_, _) => MarkDirty(SaveCategory.Contours);
         Circles.CollectionChanged += (_, _) => MarkDirty(SaveCategory.Circles);
         Diagrams.CollectionChanged += (_, _) => MarkDirty(SaveCategory.Diagrams);
         MaterialsSort();

         this.ContoursRenumber();
         CirclesLive = new(Circles); this.CirclesRenumber();
         DiagramsLive = [.. Diagrams];
         CrossSectionsLive = new(CrossSections); CrossSectionsRenumber();
         RefreshMaterialAreaLiveCollections();
         RefreshSectionLiveCollections();
      }

      void InitializeCommands()
      {
         AddDiagramCommand = new RelayCommand(_ => AddDiagram());
         NewContourCommand = new RelayCommand(NewContour);
          NewMaterialCommand = new RelayCommand(NewMaterial);
           NewMaterialFromSourceCommand = new RelayCommand(_ => NewMaterialFromSource(0));
          AddRebarCommand = new RelayCommand(_ => NewMaterialFromSource(1));
          AddSteelCommand = new RelayCommand(_ => NewMaterialFromSource(2));
         DelMaterialCommand = new RelayCommand(DelMaterial);
         FromDxfCommand = new RelayCommand(FromDxf);
         DelContourCommand = new RelayCommand(DelContour);
         NewProjectCommand = new RelayCommand(NewProject);
         OpenProjectCommand = new RelayCommand(OpenProject);
         SaveProjectCommand = new RelayCommand(SaveProject);
         SaveAsProjectCommand = new RelayCommand(SaveAsProject);
          ExitCommand = new RelayCommand(Exit);
          VacuumDbCommand = new RelayCommand(_ => VacuumDb());
         MigrateLiraStressForceSetsCommand = new RelayCommand(_ => MigrateLiraStressForceSets());
         OpenSettingsCommand = new RelayCommand(_ => new Views.SettingsWindow(this).ShowDialog());
         SetLanguageCommand = new RelayCommand(SetLanguage);
         NewCrossSectionCommand    = new RelayCommand(_ => NewCrossSection());
         NewParametricCrossSectionCommand = new RelayCommand(_ => NewParametricCrossSection());
         RebuildParametricCrossSectionCommand = new RelayCommand(p => RebuildParametricCrossSection(p as ParametricCrossSectionTreeItem));
         EditParametricCrossSectionCommand = new RelayCommand(p => EditParametricCrossSection(p as ParametricCrossSectionTreeItem));
         OpenParametricCrossSectionCommand = new RelayCommand(p => OpenParametricCrossSection(p as ParametricCrossSectionTreeItem));
         NewParametricSteelSectionCommand = new RelayCommand(_ => NewParametricSteelSection());
         RebuildParametricSteelSectionCommand = new RelayCommand(p => RebuildParametricSteelSection(p as ParametricSteelSectionTreeItem));
         EditParametricSteelSectionCommand = new RelayCommand(p => EditParametricSteelSection(p as ParametricSteelSectionTreeItem));
         OpenParametricSteelSectionCommand = new RelayCommand(p =>
         {
            if (p is ParametricSteelSectionTreeItem item) CurrentCrossSection = item.Section;
         });
         EditCrossSectionCommand   = new RelayCommand(_ => EditCrossSection());
         DeleteCrossSectionCommand = new RelayCommand(_ => DeleteCrossSection());
         NewTwoStageSectionCommand = new RelayCommand(_ => NewTwoStageSection());
         NewAreaCommand            = new RelayCommand(_ => NewArea());
         DeleteMaterialAreaCommand = new RelayCommand(_ => DeleteMaterialArea());
         NewRebarGroupCommand      = new RelayCommand(_ => NewRebarGroup());
         NewStirrupGroupCommand    = new RelayCommand(_ => NewStirrupGroup());
         NewBarForceSetCommand        = new RelayCommand(_ => NewBarForceSet());
         NewShellForceSetCommand      = new RelayCommand(_ => NewShellForceSet());
         SP20BarCombinationsCommand   = new RelayCommand(_ => OpenSP20CombinationsDialog("bar"));
         SP20ShellCombinationsCommand = new RelayCommand(_ => OpenSP20CombinationsDialog("shell"));
         DeleteForceSetCommand        = new RelayCommand(p => DeleteForceSet(p as CScore.ForceSet));
         DuplicateForceSetCommand     = new RelayCommand(p => DuplicateForceSet(p as CScore.ForceSet));
         DeleteSelectedBarForceSetsCommand   = new RelayCommand(_ => DeleteSelectedForceSets(kind: "bar"));
         DeleteSelectedShellForceSetsCommand = new RelayCommand(_ => DeleteSelectedForceSets(kind: "shell"));
         DeleteAllBarForceSetsCommand   = new RelayCommand(_ => DeleteAllForceSets(kind: "bar"));
         DeleteAllShellForceSetsCommand = new RelayCommand(_ => DeleteAllForceSets(kind: "shell"));
          NewPlateSectionCommand       = new RelayCommand(_ => NewPlateSection());
          DeletePlateSectionCommand    = new RelayCommand(p => DeletePlateSection(p as CScore.PlateSection));
          DuplicatePlateSectionCommand = new RelayCommand(p => DuplicatePlateSection(p as CScore.PlateSection));
          DeleteEquivalentSectionCommand = new RelayCommand(p => DeleteEquivalentSection(p as EquivalentSection));
          RecalculateEquivalentSectionCommand = new RelayCommand(p => RecalculateEquivalentSection(p as EquivalentSection));
         NewFireSectionCommand        = new RelayCommand(_ => NewFireSection());
         DeleteFireSectionCommand     = new RelayCommand(_ => DeleteFireSection());
         RenameFireSectionCommand     = new RelayCommand(_ => RenameFireSection());
         OpenCalcTasksCommand         = new RelayCommand(_ => CurrentPage = new Views.CalcTasksPage(this));
         CancelBusyCommand            = new RelayCommand(_ => CancelBusy(), _ => IsBusy);
         NewCalcTaskCommand    = new RelayCommand(p => NewCalcTask(p as string));
         RunCalcTaskCommand    = new RelayCommand(p => _ = RunCalcTaskAsync(p as CalcTask), p => p is CalcTask && !IsBusy);
         EditCalcTaskCommand   = new RelayCommand(p => EditCalcTask(p as CalcTask),   p => p is CalcTask);
         DeleteCalcTaskCommand = new RelayCommand(p => DeleteCalcTask(p as CalcTask), p => p is CalcTask);
         DeleteCalcResultsCommand = new RelayCommand(p => DeleteCalcResults(p as CalcTask), p => p is CalcTask);
         ExportCalcResultReportCommand = new RelayCommand(
            p => _ = CalcReportExporter.ExportAsync(this, p as CalcResult),
            p => p is CalcResult result
                 && CalcTasks.FirstOrDefault(task => task.Id == result.TaskId) is { } task
                 && ReportProviders.TryResolve(task, out _));
          ImportContoursFromDxfCommand = new RelayCommand(_ => ImportContoursFromDxf());
          ImportAcadRegionsCommand     = new RelayCommand(_ => ImportAcadRegions());
          ImportAcadRebarGroupsCommand = new RelayCommand(_ => ImportAcadRebarGroups());
          AddCircleCommand             = new RelayCommand(_ => AddCircle());
         DeleteCircleCommand          = new RelayCommand(p => DeleteCircle(p as CircleP));
         ImportCirclesFromDxfCommand  = new RelayCommand(_ => ImportCirclesFromDxf());
         ExportCirclesToDxfCommand    = new RelayCommand(_ => ExportCirclesToDxf());
         ImportCirclesFromCsvCommand  = new RelayCommand(_ => ImportCirclesFromCsv());
          ExportCirclesToCsvCommand    = new RelayCommand(_ => ExportCirclesToCsv());
          NewContourFromTemplateRectCommand   = new RelayCommand(_ => NewContourFromTemplateRect());
          NewContourFromTemplateTeeCommand    = new RelayCommand(_ => NewContourFromTemplateTee());
          NewContourFromTemplateIBeamCommand  = new RelayCommand(_ => NewContourFromTemplateIBeam());
          NewContourFromTemplateAngleCommand  = new RelayCommand(_ => NewContourFromTemplateAngle());
          NewContourFromTemplateCircleCommand = new RelayCommand(_ => NewContourFromTemplateCircle());
          NewContourFromSortamentCommand      = new RelayCommand(_ => NewContourFromSortament());
          ImportLiraLoadCasesCommand   = new RelayCommand(_ => ImportLiraHtml(LiraImportMode.LoadCases));
         ImportLiraRsnCommand         = new RelayCommand(_ => ImportLiraHtml(LiraImportMode.Rsn));
         ImportLiraRsuCommand         = new RelayCommand(_ => ImportLiraHtml(LiraImportMode.Rsu));

         NewFemSchemaCommand    = new RelayCommand(_ => NewFemSchema());
         DeleteFemSchemaCommand = new RelayCommand(p => DeleteFemSchema(p as CScore.Fem.FemSchema));
         DuplicateFemSchemaCommand = new RelayCommand(p => DuplicateFemSchema(p as CScore.Fem.FemSchema));
         RenameFemSchemaCommand    = new RelayCommand(p => RenameFemSchema(p as CScore.Fem.FemSchema));
         LoadLiraAspCommand        = new RelayCommand(p => LoadLiraAsp(p as CScore.Fem.FemSchema));
         CreateLiraPlateSectionsCommand = new RelayCommand(p => CreateLiraPlateSections(p as CScore.Fem.FemSchema));
         CreateImportedBarSectionsCommand = new RelayCommand(p => CreateImportedBarSections(p as CScore.Fem.FemSchema));
         ConvertLiraBlocksCommand  = new RelayCommand(p => ConvertLiraBlocksToMembers(p as CScore.Fem.FemSchema));
         LoadLiraRbtCommand        = new RelayCommand(p => LoadLiraRbt(p as CScore.Fem.FemSchema));
         RefreshLiraReinforcementTypesCommand = new RelayCommand(p => RefreshLiraReinforcementTypes(p as CScore.Fem.FemSchema));
         RefreshLiraPlateAxesCommand = new RelayCommand(p => RefreshLiraPlateAxes(p as CScore.Fem.FemSchema));
         RefreshLiraStiffnessesCommand = new RelayCommand(p => RefreshLiraStiffnesses(p as CScore.Fem.FemSchema));
         NewFemMemberCommand       = new RelayCommand(p => NewFemMember(p as CScore.Fem.FemSchema));
         NewFemMemberDialogCommand = new RelayCommand(p => NewFemMemberDialog(p as CScore.Fem.FemSchema));
         NewFemMembersGroupCommand = new RelayCommand(p => NewFemMembersGroup(p as CScore.Fem.FemSchema));
         RenameFemMemberGroupCommand = new RelayCommand(p => RenameFemMemberGroup(p as CScore.Fem.FemMemberGroup));
         CreateFemMembersFromMeshGroupCommand = new RelayCommand(p => CreateFemMembersFromMeshGroup(p as CScore.Fem.FemMemberGroup));
         CreatePlateModeCommand = new RelayCommand(p => StartPlanarRegionCreateMode(p as CScore.Fem.FemSchema, "plate"));
         CreateWallModeCommand = new RelayCommand(p => StartPlanarRegionCreateMode(p as CScore.Fem.FemSchema, "wall"));
         CreateSpatialPlateModeCommand = new RelayCommand(p => StartPlanarRegionCreateMode(p as CScore.Fem.FemSchema, "spatial"));
         DeleteFemMemberCommand    = new RelayCommand(p => DeleteFemMember(p as CScore.Fem.FemMemberGroup));
         AddFemCheckCommand     = new RelayCommand(p => AddFemCheck(p as CScore.Fem.IFemCheckable));
         CreateFemAnalysisCommand = new RelayCommand(p => CreateFemAnalysis(p as CScore.Fem.FemSchema));
         EditFemAnalysisCommand = new RelayCommand(p => EditFemAnalysis(p as CScore.Fem.FemAnalysis));
         ViewFemAnalysisResultCommand = new RelayCommand(
            p => ViewFemAnalysisResult(p as CScore.Fem.FemAnalysis),
            p => p is CScore.Fem.FemAnalysis a && a.ResultId is > 0);
         RunFemAnalysisCommand    = new RelayCommand(p => _ = RunFemAnalysis(p as CScore.Fem.FemAnalysis));
         DeleteFemAnalysisCommand = new RelayCommand(p => DeleteFemAnalysis(p as CScore.Fem.FemAnalysis));
         RunFemCheckCommand     = new RelayCommand(p => _ = RunFemCheck(p as CScore.Fem.FemCheck));
         EditFemCheckCommand       = new RelayCommand(p => EditFemCheck(p as CScore.Fem.FemCheck));
         DeleteFemCheckCommand     = new RelayCommand(p => DeleteFemCheck(p as CScore.Fem.FemCheck));
         DeleteAllFemChecksCommand = new RelayCommand(_ => DeleteAllFemChecks());
         AddSlsFemCheckCommand     = new RelayCommand(p => AddSlsFemCheck(p as CScore.Fem.IFemCheckable));
         ShowBarDiagramsCommand    = new RelayCommand(p => ShowBarDiagrams(p as CScore.Fem.IFemCheckable));
         AddFemCheckByGroupCommand = new RelayCommand(p =>
         {
             if (p is string g && g == "sls") AddSlsFemCheck();
             else                             AddFemCheck(null);
         });
          DeleteFemSchemaForceSetsCommand = new RelayCommand(p => DeleteFemSchemaForceSets(p as CScore.Fem.FemSchema));
          DeleteSelectedForceSetsCommand = new RelayCommand(p => DeleteSelectedForceSets(p as CScore.Fem.FemSchema));
         ImportLiraSchemaFromCsvCommand  = new RelayCommand(_ => ImportLiraSchemaFromCsv());
         ImportLiraSchemaFromFileCommand = new RelayCommand(_ => ImportLiraSchemaFromFile());
         ImportScadSchemaFromApiCommand   = new RelayCommand(_ => ImportScadSchemaFromApi(), _ => !IsBusy);
         ImportScadTopologyFromTxtCommand = new RelayCommand(_ => ImportScadTopologyFromTxt());
         ImportScadForcesLoadCasesCommand = new RelayCommand(_ => ImportScadForces(CScore.Import.ScadXlsImportMode.LoadCases));
         ImportScadForcesRsuCommand       = new RelayCommand(_ => ImportScadForces(CScore.Import.ScadXlsImportMode.Rsu));
         ImportScadForcesCombinationsCommand = new RelayCommand(_ => ImportScadForces(CScore.Import.ScadXlsImportMode.Combinations));
         ImportScadRsu2Command            = new RelayCommand(_ => ImportScadRsu2());
         ImportLiraSchemaFromApiCommand  = new RelayCommand(_ => ImportLiraSchemaFromApi());
         ImportLiraForcesFromApiCommand  = new RelayCommand(p => ImportLiraForcesFromApi(p as CScore.Fem.IFemCheckable));
         ImportLiraRsnFromApiCommand     = new RelayCommand(p => ImportLiraRsnFromApi(p as CScore.Fem.IFemCheckable));
         ImportLiraRsuFromApiCommand     = new RelayCommand(p => ImportLiraRsuFromApi(p as CScore.Fem.IFemCheckable));
         ImportScadLoadCasesFromApiCommand = new RelayCommand(
            p => ImportScadForcesFromApi(p, Services.Scad.ScadForceReadKind.LoadCases), _ => !IsBusy);
         ImportScadCombinationsFromApiCommand = new RelayCommand(
            p => ImportScadForcesFromApi(p, Services.Scad.ScadForceReadKind.Combinations), _ => !IsBusy);
         ImportScadRsuFromApiCommand = new RelayCommand(
            p => ImportScadForcesFromApi(p, Services.Scad.ScadForceReadKind.Rsu), _ => !IsBusy);
         LoadScadSelectedRebarCommand = new RelayCommand(p => LoadScadSelectedRebar(p as CScore.Fem.FemSchema), _ => !IsBusy);
         InstallScadPluginCommand = new RelayCommand(_ => InstallScadPlugin());
         RefreshScadRebarDataCommand = new RelayCommand(async p =>
         {
            if (p is CScore.Fem.FemSchema s) await RefreshScadRebarData(s);
         }, _ => !IsBusy);
         TransferScadLoadsCommand = new RelayCommand(p =>
         {
            if (p is not CScore.Fem.FemSchema s) return;
            bool editorOpen = ReferenceEquals(currentFemSchema, s) && currentPage is Views.FemSchemaPage;
            if (editorOpen && !TryLeaveFemSchemaEditor()) return;
            if (LoadScadAnalysisModel(s.Id) is { } model) TransferScadLoads(s, model);
            else LogService.Warning(string.Format(Loc.S("ScadLoadsNoModel"), s.Tag));
         }, _ => !IsBusy);
         RefreshScadBoundaryCommand = new RelayCommand(async p =>
         {
            if (p is CScore.Fem.FemSchema s) await RefreshScadBoundary(s);
         }, _ => !IsBusy);
         SetScadProjectPathCommand = new RelayCommand(p =>
         {
            if (p is CScore.Fem.FemSchema s && ChooseScadProjectPath(s) is { } path)
               LogService.Info(string.Format(Loc.S("ScadForcesProjectSet"), s.Tag, path));
         });
      }

      void SetLanguage(object? param)
      {
         if (param != null && int.TryParse(param.ToString(), out int lang))
            SetLanguageDictionary(lang);
      }

      /// <summary>
      /// Применяет текущие настройки графиков ко всем активным IPlotService.
      /// Вызывается при изменении настроек в SettingsWindow.
      /// </summary>
      public void ApplyPlotSettings()
      {
         if (CurrentPage is Views.ContourPlot cp && cp.DataContext is ViewModels.ContourVM cvm)
            cvm.PlotService?.ApplySettings(PlotSettings);
         if (CurrentPage is Views.MaterialAreaPage map)
            map.RefreshPlotSettings();
         if (CurrentPage is Views.CrossSectionPage csp)
            csp.RefreshPlotSettings();
         if (CurrentPage is Views.RebarGroupEditorPage rgp)
            rgp.RefreshPlotSettings();
         DxfBgApplied?.Invoke(PlotSettings.DxfCanvasBackground);
         NotifyPlotSettingsApplied();
      }

      /// <summary>
      /// Обработчик команды <see cref="NewMaterialCommand"/>. Создаёт новый
      /// пустой материал и открывает страницу его редактирования.
      /// </summary>
       void NewMaterial(object? o = null)
       {
          CurrentPage = new MaterialPage(new Material(0), this);
       }

       /// <summary>
       /// Обработчик команды <see cref="NewMaterialFromSourceCommand"/>.
       /// Создаёт новый материал, открывает страницу редактирования и сразу
       /// открывает окно хранилища материалов с вкладкой, соответствующей
       /// типу материала (0=бетон, 1=арматура, 2=сталь).
       /// </summary>
       internal void NewMaterialFromSource(int tabIndex)
       {
          var material = new Material(0);
          var vm = new MaterialVM() { Material = material, mvm = this };
          CurrentPage = new MaterialPage(material, this, vm);

          var window = new Views.FromDataSourceWindow(vm, tabIndex);
          window.ShowDialog();
       }

      /// <summary>
      /// <summary>Создаёт новую пустую диаграмму σ(ε) и открывает её страницу редактирования.</summary>
      void AddDiagram()
      {
         var d = new CScore.Diagramm
         {
            Tag          = Loc.S("NewDiagram"),
            Type         = CScore.DiagrammType.Custom,
            CalcType     = CScore.CalcType.C,
            MaterialType = CScore.MatType.Concrete,
            Ic           = new CSmath.LSpline(new[] { -0.003, 0.0 }, new[] { -30.0, 0.0 }),
            It           = new CSmath.LSpline(new[] { 0.0, 0.001  }, new[] {   0.0, 15.0 })
         };
         CurrentPage = new Views.DiagramPage(d, this, isNew: true);
      }

      /// <summary>
      /// Обработчик команды <see cref="NewContourCommand"/>. Создаёт новую пустую
      /// ViewModel контура и открывает страницу редактирования контура.
      /// </summary>
      void NewContour(object? o = null)
      {
         CurrentContour = new ContourVM { mvm = this };
      }

      void NewContourFromTemplateRect()
      {
         var dlg = new Views.Dialogs.TemplateRectDialog();
         if (dlg.ShowDialog() != true) return;
         var pts = TemplatePoints.RectPoints(dlg.WidthMm / 1000.0, dlg.HeightMm / 1000.0);
         var contour = MakeContourFromPoints(pts, dlg.ContourName);
         LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
      }

      void NewContourFromTemplateTee()
      {
         var dlg = new Views.Dialogs.TemplateTeeDialog();
         if (dlg.ShowDialog() != true) return;
         var pts = TemplatePoints.TeePoints(dlg.WidthMm / 1000.0, dlg.HeightMm / 1000.0,
             dlg.TwMm / 1000.0, dlg.TfMm / 1000.0);
         var contour = MakeContourFromPoints(pts, dlg.ContourName);
         LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
      }

      void NewContourFromTemplateIBeam()
      {
         var dlg = new Views.Dialogs.TemplateIBeamDialog();
         if (dlg.ShowDialog() != true) return;
         var pts = TemplatePoints.IBeamPoints(dlg.HeightMm / 1000.0, dlg.WidthMm / 1000.0,
             dlg.TwMm / 1000.0, dlg.TfMm / 1000.0);
         var contour = MakeContourFromPoints(pts, dlg.ContourName);
         LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
      }

      void NewContourFromTemplateAngle()
      {
         var dlg = new Views.Dialogs.TemplateAngleDialog();
         if (dlg.ShowDialog() != true) return;
         var pts = TemplatePoints.AnglePoints(dlg.WidthMm / 1000.0, dlg.HeightMm / 1000.0,
             dlg.TwMm / 1000.0, dlg.TfMm / 1000.0);
         var contour = MakeContourFromPoints(pts, dlg.ContourName);
         LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
      }

      void NewContourFromTemplateCircle()
      {
         var dlg = new Views.Dialogs.TemplateCircleDialog();
         if (dlg.ShowDialog() != true) return;
         var pts = TemplatePoints.CirclePoints(dlg.DiameterMm / 1000.0, dlg.Segments);
         var contour = MakeContourFromPoints(pts, dlg.ContourName);
         LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
      }

      void NewContourFromSortament()
      {
         var dlg = new Views.Dialogs.ProfilePolyDialog();
         if (dlg.ShowDialog() != true) return;

         var pdb = new Utilites.ProfileDB();
         var profile = pdb.GetProfile(dlg.ShapeType, dlg.ProfileId);
         string name = dlg.ContourName;

         if (dlg.IsHollow)
         {
            List<(double X, double Y)> outerPts, holePts;
            if (profile is RectTubeProfile rtp)
            {
               outerPts = rtp.OuterPoints(dlg.NArc);
               holePts = rtp.HolePoints(dlg.NArc);
            }
            else if (profile is RoundTubeProfile rtp2)
            {
               outerPts = rtp2.OuterPoints(dlg.NArc);
               holePts = rtp2.HolePoints(dlg.NArc);
            }
            else return;

            var outer = MakeContourFromPoints(outerPts, name, ContourType.Hull);
            var holeContour = BuildContour(holePts, $"{name} (отв.)", ContourType.Hole);
            db.SaveContour(holeContour);
            Contours.Add(holeContour);
            LogService.Info(string.Format(Loc.S("ContourCreated"), outer.Tag));
         }
         else
         {
            List<(double X, double Y)> pts;
            if (profile is IBeamProfile ib)
               pts = ib.ToPolygonPoints(dlg.NArc, dlg.Slope);
            else if (profile is ChannelProfile ch)
               pts = ch.ToPolygonPoints(dlg.NArc, dlg.Slope);
            else if (profile is AngleProfile ang)
               pts = ang.ToPolygonPoints(dlg.NArc);
            else
               return;
            var contour = MakeContourFromPoints(pts, name, ContourType.Hull);
            LogService.Info(string.Format(Loc.S("ContourCreated"), contour.Tag));
         }
      }

      Contour BuildContour(List<(double X, double Y)> pts, string name, ContourType type)
      {
         var stressPoints = new List<StressPoint>();
         int k = 1;
         foreach (var (x, y) in pts)
            stressPoints.Add(new StressPoint(x, y) { Num = k++ });
         stressPoints.Add(new StressPoint(pts[0].X, pts[0].Y) { Num = k });
         return new Contour(stressPoints, string.IsNullOrWhiteSpace(name) ? Loc.S("Contour") : name)
         {
            Type = type
         };
      }

      ContourVM MakeContourFromPoints(List<(double X, double Y)> pts, string name, ContourType type = ContourType.Hull)
      {
         var contour = BuildContour(pts, name, type);
         db.SaveContour(contour);
         Contours.Add(contour);
         var vm = new ContourVM(contour) { mvm = this };
         CurrentContour = vm;
         return vm;
      }

      /// <summary>
      /// Обработчик команды <see cref="DelMaterialCommand"/>. Запрашивает подтверждение
      /// удаления и удаляет выбранный материал из базы данных.
      /// </summary>
      private void DelMaterial(object? o = null)
      {
         CurrentPage = null!;
         if (CurrentMaterial == null) return;
         System.Windows.MessageBoxImage ic = System.Windows.MessageBoxImage.Warning;
         System.Windows.MessageBoxButton mbb = System.Windows.MessageBoxButton.YesNo;
          var res = System.Windows.MessageBox.Show(Loc.S("ConfirmDeleteMaterial"), Loc.S("Warning"), mbb, ic);
          if (res == System.Windows.MessageBoxResult.No || res == System.Windows.MessageBoxResult.Cancel) return;

          string t = currentMaterial!.Tag;
          db.DeleteMaterial(CurrentMaterial);

          LogService.Info(string.Format(Loc.S("MaterialDeleted"), t));
      }

      /// <summary>
      /// Обработчик команды <see cref="DelContourCommand"/>. Запрашивает подтверждение
      /// и удаляет выбранный контур вместе со всеми связанными областями материалов
      /// из базы данных.
      /// </summary>
      private void DelContour(object? o = null)
      {
         CurrentPage = null!;
         if (CurrentContour == null) return;
         System.Windows.MessageBoxImage ic = System.Windows.MessageBoxImage.Warning;
         System.Windows.MessageBoxButton mbb = System.Windows.MessageBoxButton.YesNo;
          var res = System.Windows.MessageBox.Show(Loc.S("ConfirmDeleteContour"), Loc.S("Warning"), mbb, ic);
          if (res == System.Windows.MessageBoxResult.No || res == System.Windows.MessageBoxResult.Cancel) return;

         string t = currentContour!.Tag;
         db.DeleteContour(currentContour.Contour);

          LogService.Info(string.Format(Loc.S("ContourDeleted"), t));
      }

      /// <summary>
      /// Обработчик команды <see cref="FromDxfCommand"/>. Открывает страницу
      /// импорта геометрии из DXF-файла.
      /// </summary>
      private void FromDxf(object? o = null)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");
         if (string.IsNullOrEmpty(fileName)) return;
         CurrentPage = new FromDxfPage(this, fileName);
      }

      private void AddCircle(object? _ = null)
      {
         var dlg = new Views.Dialogs.CircleDialog();
         if (dlg.ShowDialog() != true) return;
         var cp = new CircleP(dlg.X, dlg.Y, dlg.Radius);
         db.SaveCircle(cp);
         Circles.Add(cp);
         this.CirclesRenumber();
         LogService.Info(Loc.S("CircleAdded"));
      }

      private void DeleteCircle(CircleP? cp)
      {
         if (cp == null) return;
         var res = MessageBox.Show(Loc.S("ConfirmDeleteCircle"), Loc.S("Confirmation"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         db.DeleteCircle(cp);
         Circles.Remove(cp);
         this.CirclesRenumber();
         LogService.Info(string.Format(Loc.S("CircleDeleted"), cp.Tag));
      }

      private void ImportContoursFromDxf(object? _ = null)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: Loc.S("ImportContoursFromDxfTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var dxf = DxfDocument.Load(fileName);
         const double scale = 0.001; // мм → м
         string geoSet = Path.GetFileNameWithoutExtension(fileName);
         int added = 0;
         int skipped = 0;

         foreach (var pline in dxf.Entities.Polylines2D)
         {
            var verts = pline.Vertexes;
            if (verts.Count < 2) continue;

            bool firstEqualsLast =
               Math.Abs(verts[0].Position.X - verts[^1].Position.X) < 1e-4 &&
               Math.Abs(verts[0].Position.Y - verts[^1].Position.Y) < 1e-4;

            bool isClosed = pline.IsClosed || firstEqualsLast;
            if (!isClosed) { skipped++; continue; }

            var pts = new List<StressPoint>();
            int j = 1;
            foreach (var v in verts)
               pts.Add(new StressPoint(v.Position.X * scale, v.Position.Y * scale) { Num = j++ });

            // IsClosed-флаг без совпадающих вершин → добавляем замыкающую точку
            if (pline.IsClosed && !firstEqualsLast)
               pts.Add(new StressPoint(verts[0].Position.X * scale, verts[0].Position.Y * scale) { Num = j });

            if (pts.Count < 4) { skipped++; continue; }

            var contour = new Contour(pts, pline.Layer.Name) { GeometrySet = geoSet };
            db.SaveContour(contour);
            Contours.Add(contour); // CollectionChanged → ContoursRenumber
            added++;
         }

         if (added > 0)
            LogService.Info(string.Format(Loc.S("ContoursImportedFromDxf"), added, Path.GetFileName(fileName)));
         if (skipped > 0)
            LogService.Warning(string.Format(Loc.S("ContoursSkippedDxf"), skipped));
         if (added == 0 && skipped == 0)
            LogService.Warning(string.Format(Loc.S("NoDxfPolylines"), Path.GetFileName(fileName)));
      }

      private void ImportCirclesFromDxf(object? _ = null)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: Loc.S("ImportCirclesFromDxfTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var dxf = DxfDocument.Load(fileName);
         const double scale = 0.001; // мм → м
         string geoSet = Path.GetFileNameWithoutExtension(fileName);
         int added = 0;

         foreach (var c in dxf.Entities.Circles)
         {
            var cp = new CircleP(c.Center.X * scale, c.Center.Y * scale, c.Radius * scale)
            {
               Tag = c.Layer.Name,
               GeometrySet = geoSet
            };
            db.SaveCircle(cp);
            Circles.Add(cp);
            added++;
         }

         if (added > 0)
         {
            this.CirclesRenumber();
            LogService.Info(string.Format(Loc.S("CirclesImportedFromDxf"), added, Path.GetFileName(fileName)));
         }
          else
          {
             LogService.Warning(string.Format(Loc.S("NoDxfCircles"), Path.GetFileName(fileName)));
          }
       }

       /// <summary>Текущее выбранное эквивалентное сечение.</summary>
       public EquivalentSection? CurrentEquivalentSection
       {
          get => currentEquivalentSection;
          set
          {
             currentEquivalentSection = value;
             CurrentPage = value != null
                ? new Views.EquivalentSectionPage(value, this)
                : null!;
             OnPropertyChanged();
          }
       }

      private void ImportAcadRegions(object? _ = null)
      {
         var s = AcadImportSettings;
         using var importer = new Services.AcadImporter(
            s.ScaleFactor,
            s.ArcDiscretizationMode == ArcDiscretization.ChordLength,
            s.ArcChordLength,
            s.ArcSegments);
         try
         {
            importer.Connect();
         }
         catch (InvalidOperationException ex)
         {
            LogService.Error(ex.Message);
            return;
         }

         List<CScore.MaterialArea> regions;
         List<CScore.Contour> contours;
         try
         {
            string? filter = string.IsNullOrWhiteSpace(AcadImportSettings.DefaultLayerFilter)
               ? null : AcadImportSettings.DefaultLayerFilter;
            (regions, contours) = importer.ImportRegions(filter);
         }
         catch (Exception ex)
         {
            LogService.Error($"Ошибка при импорте областей из AutoCAD: {ex.Message}");
            return;
         }

         if (regions.Count == 0)
         {
            LogService.Warning(Loc.S("AcadNoClosedPolylines"));
            return;
         }

         int nextCtNum = Contours.Count > 0 ? Contours.Max(c => c.Num) + 1 : 1;
         foreach (var ct in contours)
         {
            ct.Num = nextCtNum++;
            int pi = 1;
            foreach (var p in ct.Points)
               p.Num = pi++;
            db.SaveContour(ct);
            if (!Contours.Contains(ct))
               Contours.Add(ct);
         }

         int nextMaNum = MaterialAreas.Count > 0 ? MaterialAreas.Max(a => a.Num) + 1 : 1;
         foreach (var ma in regions)
         {
            ma.Num = nextMaNum++;
            db.SaveMaterialArea(ma);
         }

         LogService.Info(string.Format(Loc.S("AcadImportRegionsSuccess"), regions.Count));
      }

      private void ImportAcadRebarGroups(object? _ = null)
      {
         var s = AcadImportSettings;
         using var importer = new Services.AcadImporter(
            s.ScaleFactor,
            s.ArcDiscretizationMode == ArcDiscretization.ChordLength,
            s.ArcChordLength,
            s.ArcSegments);
         try
         {
            importer.Connect();
         }
         catch (InvalidOperationException ex)
         {
            LogService.Error(ex.Message);
            return;
         }

         Dictionary<string, List<CScore.Fiber>> groups;
         List<CScore.CircleP> circles;
         try
         {
            string? filter = string.IsNullOrWhiteSpace(AcadImportSettings.DefaultLayerFilter)
               ? null : AcadImportSettings.DefaultLayerFilter;
            (groups, circles) = importer.ImportCirclesByLayer(filter);
         }
         catch (Exception ex)
         {
            LogService.Error($"Ошибка при импорте групп арматуры из AutoCAD: {ex.Message}");
            return;
         }

         if (groups.Count == 0)
         {
            LogService.Warning(Loc.S("AcadNoCircles"));
            return;
         }

         int nextCircNum = Circles.Count > 0 ? Circles.Max(c => c.Num) + 1 : 1;
         foreach (var cp in circles)
         {
            cp.Num = nextCircNum++;
            db.SaveCircle(cp);
            if (!Circles.Contains(cp))
               Circles.Add(cp);
         }

         this.CirclesRenumber();
         int totalBars = 0;
         foreach (var kv in groups)
         {
            var ma = new CScore.MaterialArea
            {
               Tag = kv.Key,
               Category = CScore.AreaCategory.RebarGroup,
               Fibers = kv.Value
            };
            int newNum = MaterialAreas.Count > 0 ? MaterialAreas.Max(a => a.Num) + 1 : 1;
            ma.Num = newNum;
            db.SaveMaterialArea(ma); // SaveMaterialArea сам добавляет в MaterialAreas
            totalBars += kv.Value.Count;
         }
         this.CirclesRenumber();
         LogService.Info(string.Format(Loc.S("AcadImportRebarGroupsSuccess"), groups.Count, totalBars));
      }

      private void ExportCirclesToDxf(object? _ = null)
      {
         if (Circles.Count == 0)
         {
            LogService.Warning(Loc.S("NoCirclesToExport"));
            return;
         }

         string? fileName = FileDialogService.SaveFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            defaultExt: "*.dxf",
            title: Loc.S("ExportCirclesToDxfTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var dxfDoc = new DxfDocument();
         const double scale = 1000.0; // м → мм

         foreach (var cp in Circles)
         {
            string layerName = string.IsNullOrWhiteSpace(cp.Tag) ? "0" : cp.Tag;
            if (!dxfDoc.Layers.Contains(layerName))
               dxfDoc.Layers.Add(new Layer(layerName));
            var circle = new Circle(
               new Vector3(cp.X * scale, cp.Y * scale, 0),
               cp.Radius * scale)
            {
               Layer = dxfDoc.Layers[layerName]
            };
            dxfDoc.Entities.Add(circle);
         }

         dxfDoc.Save(fileName);
         LogService.Info(string.Format(Loc.S("CirclesExportedToDxf"), Circles.Count, Path.GetFileName(fileName)));
      }

      private void ImportCirclesFromCsv(object? _ = null)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: "Текстовый файл (*.csv)|*.csv",
            title: Loc.S("ImportCirclesFromCsvTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
         using var reader = new StreamReader(fileName);
         using var csv = new CsvReader(reader, config);
         var records = csv.GetRecords<CircleCsvRow>().ToList();
         int added = 0;

         foreach (var r in records)
         {
            var cp = new CircleP(r.X, r.Y, r.Radius) { Tag = r.Tag };
            db.SaveCircle(cp);
            Circles.Add(cp);
            added++;
         }

         if (added > 0)
         {
            this.CirclesRenumber();
            LogService.Info(string.Format(Loc.S("CirclesImportedFromCsv"), added, Path.GetFileName(fileName)));
         }
      }

      private void ExportCirclesToCsv(object? _ = null)
      {
         if (Circles.Count == 0)
         {
            LogService.Warning(Loc.S("NoCirclesToExport"));
            return;
         }

         string? fileName = FileDialogService.SaveFile(
            filter: "Текстовый файл (*.csv)|*.csv",
            defaultExt: "*.csv",
            title: Loc.S("ExportCirclesToCsvTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" };
         using var writer = new StreamWriter(fileName);
         using var csv = new CsvWriter(writer, config);
         csv.WriteRecords(Circles.Select(c => new CircleCsvRow
            { Tag = c.Tag, X = c.X, Y = c.Y, Radius = c.Radius }));
         LogService.Info(string.Format(Loc.S("CirclesExportedToCsv"), Circles.Count, Path.GetFileName(fileName)));
      }

      void ImportLiraHtml(LiraImportMode mode)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: "HTML LIRA SAPR (*.htm;*.html)|*.htm;*.html",
            title: mode switch
            {
               LiraImportMode.LoadCases => Loc.S("ImportLiraLoadCasesTitle"),
               LiraImportMode.Rsn       => Loc.S("ImportLiraRsnTitle"),
               _                        => Loc.S("ImportLiraRsuTitle"),
            });
         if (string.IsNullOrEmpty(fileName)) return;

         var import = LiraImporter.ImportFile(fileName, mode, LiraImportSettings.ToOptions());
         if (!import.Success)
         {
            System.Windows.MessageBox.Show(
               import.Error ?? Loc.S("ImportLiraFailed"),
               Loc.S("ImportLiraErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

         int nextNum = ForceSets.Count > 0 ? ForceSets.Max(f => f.Num) + 1 : 1;
         foreach (var fs in import.ForceSets)
         {
            fs.Num = nextNum++;
            db.SaveForceSet(fs);
            ForceSets.Add(fs);
         }
         LogService.Info(string.Format(Loc.S("ImportLiraSuccess"),
            import.ForceSets.Count, Path.GetFileName(fileName)));
      }

      void RefreshAfterLoad()
      {
         CurrentPage = null!;
         CurrentMaterial = null;
         CurrentContour = null;
         currentCrossSection = null;
         currentMaterialArea = null;
          currentBarForceSet   = null;
          currentShellForceSet = null;
          currentPlateSection  = null;
          currentEquivalentSection = null;
          currentFireSection   = null;
         OnPropertyChanged(nameof(CurrentCrossSection));
         OnPropertyChanged(nameof(CurrentMaterialArea));
         OnPropertyChanged(nameof(CurrentBarForceSet));
          OnPropertyChanged(nameof(CurrentShellForceSet));
          OnPropertyChanged(nameof(CurrentPlateSection));
          OnPropertyChanged(nameof(CurrentEquivalentSection));
          OnPropertyChanged(nameof(CurrentFireSection));
         CalcTasks   = db.CalcTasks;
         CalcResults = db.CalcResults;
         FemSchemas  = db.FemSchemas;
         FemChecks   = db.FemChecks;
         BuildFemRootNodes();
         MaterialsSort();
         this.ContoursRenumber();
         CirclesLive = new(Circles); this.CirclesRenumber();
         DiagramsLive = [.. Diagrams];
         CrossSectionsLive = new(CrossSections); CrossSectionsRenumber();
         RefreshMaterialAreaLiveCollections();
          RefreshSectionLiveCollections();
          RefreshPlateSectionsLive();
          RefreshEquivalentSectionsLive();
          ClearDirty();
          RefreshEquivalentSectionsStale();
      }

      /// <summary>
      /// Обработчик команды <see cref="NewProjectCommand"/>.
      /// Создаёт новый пустой проект во временном файле.
      /// </summary>
      private void NewProject(object? o = null)
      {
         try
         {
            InitNewDatabase();
            db.ClearCollections();
            RefreshAfterLoad();
            LoadSettingsFromDb();
            CurrentProjectPath = null;
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectFileName));
            LogService.Info(Loc.S("ProjectCreated"));
         }
         catch (Exception ex)
         {
            LogService.Error(string.Format(Loc.S("ProjectCreatedError"), ex.Message));
         }
      }

      /// <summary>
      /// Обработчик команды <see cref="OpenProjectCommand"/>.
      /// Открывает существующий проект из файла .db.
      /// </summary>
      private void OpenProject(object? o = null)
      {
          var path = FileDialogService.OpenFile(
             Loc.S("SqliteDbFilter"),
             Loc.S("OpenProject"));
         if (path == null) return;
         try
         {
            if (!IsSqliteDatabase(path))
               throw new Exception(Loc.S("NotSqliteDatabase"));
            db.SaveAll();
            db.ChangeDatabase(path);
            db.ClearCollections();
            db.LoadAll();
            RefreshAfterLoad();
            LoadSettingsFromDb();
            CurrentProjectPath = path;
            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectFileName));
            LogService.Info(string.Format(Loc.S("ProjectOpened"), path));
          }
          catch (Exception ex)
          {
             LogService.Error(string.Format(Loc.S("ProjectOpenError"), ex.Message));
          }
      }

      static bool IsSqliteDatabase(string path)
      {
         try
         {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[16];
            if (fs.Read(buf, 0, 16) < 16) return false;
            var header = System.Text.Encoding.ASCII.GetString(buf, 0, 15);
            return header == "SQLite format 3";
         }
         catch { return false; }
      }

      /// <summary>
      /// Обработчик команды <see cref="SaveProjectCommand"/>.
      /// Сохраняет проект в текущий файл. Если файл не задан — вызывает SaveAs.
      /// </summary>
      private void SaveProject(object? o = null)
      {
         SaveProjectInternal();
      }

      /// <summary>
      /// Обработчик команды <see cref="SaveAsProjectCommand"/>.
      /// Сохраняет копию текущей базы данных в новый файл.
      /// </summary>
      private void SaveAsProject(object? o = null)
      {
          var path = FileDialogService.SaveFile(
             Loc.S("SqliteDbFilter"),
             ".db",
             Loc.S("SaveProjectAs"));
         if (path == null) return;
         try
         {
             db.SaveAs(path);
             CurrentProjectPath = path;
             ClearDirty();
             OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(ProjectFileName));
            LogService.Info(string.Format(Loc.S("ProjectSavedPath"), path));
          }
          catch (Exception ex)
          {
             LogService.Error(string.Format(Loc.S("ProjectSaveError"), ex.Message));
         }
      }

      /// <summary>
      /// Обработчик изменения коллекции <see cref="Materials"/>. Вызывает
      /// <see cref="MaterialsSort"/> для повторной фильтрации по типам материалов.
      /// </summary>
      private void Concretes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
      {
         MaterialsSort();
      }

      /// <summary>
      /// Обработчик изменения коллекции <see cref="Contours"/>. Вызывает
      /// метод <c>ContoursRenumber</c> для перенумерации контуров.
      /// </summary>
      private void Contours_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
      {
         this.ContoursRenumber();
      }

      /// <summary>
      /// Сортирует и фильтрует <see cref="Materials"/> по типу: заполняет коллекции
      /// <see cref="Concretes"/>, <see cref="Armatures"/> и <see cref="Steels"/>,
      /// затем перенумеровывает материалы.
      /// </summary>
      internal void MaterialsSort()
      {
         var c = from m in Materials where m.Type == MatType.Concrete select m;
         Concretes.Clear(); Concretes.AddRange(c);
         var a = from m in Materials
                 where m.Type == MatType.ReSteelF || m.Type == MatType.ReSteelU
                 select m;
         Armatures.Clear(); Armatures.AddRange(a);
         var s = from m in Materials where m.Type == MatType.Steel select m;
         Steels.Clear(); Steels.AddRange(s);
         this.MaterialsRenumber();
      }

      /// <summary>
      /// Вызывается при выборе материала в навигации. Открывает страницу
      /// редактирования выбранного материала и устанавливает флаг <c>IsSaved</c> в true.
      /// </summary>
      public void OnSelectMaterial()
      {
         if (CurrentMaterial == null) return;
         CurrentPage = new MaterialPage(CurrentMaterial, this);
         MaterialVM vm = (MaterialVM)CurrentPage.DataContext;
         vm.IsSaved = true;
      }

      /// <summary>
      /// Активная коллекция точек, привязанная к текущему представлению.
      /// Обновляется при навигации между контурами.
      /// </summary>
      public ObservableCollection<StressPoint> PointsLive
      {
         get { return pointsLive; }
         set { pointsLive = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Активная коллекция окружностей, привязанная к текущему представлению.
      /// Обновляется при навигации между контурами и DXF-импортом.
      /// </summary>
      public ObservableCollection<CircleP> CirclesLive
      {
         get { return circlesLive; }
         set { circlesLive = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Активная коллекция волокон, привязанная к текущему представлению.
      /// </summary>
      public ObservableCollection<Fiber> FibersLive
      {
         get { return fibersLive; }
         set { fibersLive = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Активная коллекция ViewModel контуров, привязанная к текущему представлению.
      /// </summary>
      public ObservableCollection<ContourVM> ContoursLive
      {
         get { return contoursLive; }
         set { contoursLive = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Отфильтрованная коллекция бетонных материалов из <see cref="Materials"/>.
      /// Используется для привязки в ComboBox выбора бетона.
      /// </summary>
      public ObservableCollection<Material> Concretes
      {
         get { return concretes; }
         set { concretes = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Отфильтрованная коллекция арматурных сталей (физический и условный предел текучести)
      /// из <see cref="Materials"/>. Используется для привязки в ComboBox выбора арматуры.
      /// </summary>
      public ObservableCollection<Material> Armatures
      {
         get { return armatures; }
         set { armatures = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Отфильтрованная коллекция сталей для строительных конструкций из <see cref="Materials"/>.
      /// </summary>
      public ObservableCollection<Material> Steels
      {
         get { return steels; }
         set { steels = value; OnPropertyChanged(); }
      }

      void CrossSectionsRenumber()
      {
         for (int i = 0; i < CrossSections.Count; i++)
            CrossSections[i].Num = i + 1;
      }

      void NewCrossSection()
      {
         currentCrossSection = null;
         CurrentPage = new Views.CrossSectionPage(this);
      }

      void NewParametricCrossSection()
      {
         var vm = new ParametricRcSectionVM(Concretes, Armatures);
         var dialog = new Views.Dialogs.ParametricRcSectionDialog(vm)
         {
            Owner = Application.Current?.MainWindow
         };
         if (dialog.ShowDialog() != true) return;

         var section = new CrossSection
         {
            Num = CrossSections.Count > 0 ? CrossSections.Max(s => s.Num) + 1 : 1,
            Tag = vm.BuildDefinition().Tag
         };
         var result = new ParametricRcSectionProjectService(db)
            .GenerateAndSave(section, vm.BuildDefinition());
         if (result.Diagnostics.Count != 0)
         {
            LogService.Error(string.Join("; ", result.Diagnostics));
            return;
         }
         RefreshSectionLiveCollections();
         CurrentCrossSection = section;
         MarkDirty(SaveCategory.CrossSections);
      }

      void RebuildParametricCrossSection(ParametricCrossSectionTreeItem? item)
      {
         if (item is null) return;
         if (MessageBox.Show(Loc.S("ParametricRcConfirmRebuild"),
               Loc.S("ParametricRcDialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
         var state = new ParametricRcSectionProjectService(db).Restore(item.Section);
         if (state.LoadStatus == ParametricRcDefinitionLoadStatus.Supported)
         {
            RefreshSectionLiveCollections();
            CurrentCrossSection = item.Section;
            MarkDirty(SaveCategory.CrossSections);
         }
      }

      void OpenParametricCrossSection(ParametricCrossSectionTreeItem? item)
      {
         if (item is null) return;
         CurrentCrossSection = item.Section;
      }

      void EditParametricCrossSection(ParametricCrossSectionTreeItem? item)
      {
         if (item is null) return;
         var service = new ParametricRcSectionProjectService(db);
         if (!service.TryGetDefinition(item.Section, out var definition)) return;
         var vm = new ParametricRcSectionVM(Concretes, Armatures);
         vm.LoadDefinition(definition);
         var dialog = new Views.Dialogs.ParametricRcSectionDialog(vm)
         {
            Owner = Application.Current?.MainWindow
         };
         if (dialog.ShowDialog() != true) return;
         var result = service.GenerateAndSave(item.Section, vm.BuildDefinition());
         if (result.Diagnostics.Count != 0)
         {
            LogService.Error(string.Join("; ", result.Diagnostics));
            return;
         }
         RefreshSectionLiveCollections();
         CurrentCrossSection = item.Section;
         MarkDirty(SaveCategory.CrossSections);
      }

      ParametricSteelSectionVM CreateParametricSteelVM()
      {
         // По умолчанию выбирается первый материал Steel (VM), Custom допускается как у задачи СП 16.
         var materials = Materials.Where(m => m.Type is MatType.Steel or MatType.Custom)
            .OrderBy(m => m.Type == MatType.Steel ? 0 : 1);
         return new ParametricSteelSectionVM(materials, new ProfileDB());
      }

      bool ShowParametricSteelDialog(ParametricSteelSectionVM vm) =>
         new Views.Dialogs.ParametricSteelSectionDialog(vm) { Owner = Application.Current?.MainWindow }
            .ShowDialog() == true;

      void NewParametricSteelSection()
      {
         var vm = CreateParametricSteelVM();
         if (!ShowParametricSteelDialog(vm)) return;
         var definition = vm.BuildDefinition();
         var section = new CrossSection
         {
            Num = CrossSections.Count > 0 ? CrossSections.Max(s => s.Num) + 1 : 1,
            Tag = definition.Tag
         };
         var result = new ParametricSteelSectionProjectService(db).GenerateAndSave(section, definition);
         if (result.Diagnostics.Count != 0)
         {
            LogService.Error(string.Join("; ", result.Diagnostics));
            return;
         }
         RefreshSectionLiveCollections();
         CurrentCrossSection = section;
         MarkDirty(SaveCategory.CrossSections);
      }

      void RebuildParametricSteelSection(ParametricSteelSectionTreeItem? item)
      {
         if (item is null) return;
         if (MessageBox.Show(Loc.S("ParametricRcConfirmRebuild"),
               Loc.S("ParametricSteelDialogTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
         var state = new ParametricSteelSectionProjectService(db).Restore(item.Section);
         if (state.LoadStatus == ParametricSteelDefinitionLoadStatus.Supported)
         {
            RefreshSectionLiveCollections();
            CurrentCrossSection = item.Section;
            MarkDirty(SaveCategory.CrossSections);
         }
      }

      void EditParametricSteelSection(ParametricSteelSectionTreeItem? item)
      {
         if (item is null) return;
         var service = new ParametricSteelSectionProjectService(db);
         if (!service.TryGetDefinition(item.Section, out var definition)) return;
         var vm = CreateParametricSteelVM();
         vm.LoadDefinition(definition);
         if (!ShowParametricSteelDialog(vm)) return;
         var result = service.GenerateAndSave(item.Section, vm.BuildDefinition());
         if (result.Diagnostics.Count != 0)
         {
            LogService.Error(string.Join("; ", result.Diagnostics));
            return;
         }
         RefreshSectionLiveCollections();
         CurrentCrossSection = item.Section;
         MarkDirty(SaveCategory.CrossSections);
      }

      void NewTwoStageSection()
      {
         currentCrossSection = null;
         CurrentPage = new Views.TwoStageSectionEditorPage(this);
      }

      void EditCrossSection()
      {
         if (currentCrossSection == null) return;
         CurrentPage = currentCrossSection is TwoStageSection tss
            ? (System.Windows.Controls.UserControl)new Views.TwoStageSectionEditorPage(tss, this)
            : new Views.CrossSectionPage(currentCrossSection, this);
      }

      void DeleteCrossSection()
      {
         if (currentCrossSection == null) return;
         System.Windows.MessageBoxImage ic = System.Windows.MessageBoxImage.Warning;
         System.Windows.MessageBoxButton mbb = System.Windows.MessageBoxButton.YesNo;
         var res = System.Windows.MessageBox.Show(
            Loc.S("ConfirmDeleteRegion"), Loc.S("Warning"), mbb, ic);
         if (res != System.Windows.MessageBoxResult.Yes) return;

         db.DeleteCrossSection(currentCrossSection);
         CrossSectionsLive = new(CrossSections);
         CrossSectionsRenumber();
         RefreshSectionLiveCollections();
         currentCrossSection = null;
         CurrentPage = null!;
         OnPropertyChanged(nameof(CurrentCrossSection));
      }

      public void RemoveMaterialArea(ViewModels.MaterialAreaVM vm)
      {
         var sec = CrossSections.FirstOrDefault(s => s.Areas.Contains(vm.Model));
         if (sec == null) return;
         sec.Areas.Remove(vm.Model);
         MarkDirty(SaveCategory.CrossSections);
      }

      void NewBarForceSet()
      {
         currentBarForceSet = null;
         CurrentPage = new Views.BarForceSetPage(this);
      }

      void NewShellForceSet()
      {
         currentShellForceSet = null;
         CurrentPage = new Views.ShellForceSetPage(this);
      }

      void OpenSP20CombinationsDialog(string kind)
      {
         var sets = kind == "shell" ? ShellForceSets : BarForceSets;
         var dlg = new Views.SP20Dialog(sets, this)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         dlg.ShowDialog();
      }

      void DeleteForceSet(CScore.ForceSet? target = null)
      {
         var fs = target ?? currentBarForceSet ?? currentShellForceSet;
         if (fs == null) return;
         var res = System.Windows.MessageBox.Show(
            Loc.S("ConfirmDeleteRegion"), Loc.S("Warning"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
         if (res != System.Windows.MessageBoxResult.Yes) return;
         db.DeleteForceSet(fs);
         if (fs == currentBarForceSet)
         {
            currentBarForceSet = null;
            CurrentPage = null!;
            OnPropertyChanged(nameof(CurrentBarForceSet));
         }
         else if (fs == currentShellForceSet)
         {
            currentShellForceSet = null;
            CurrentPage = null!;
            OnPropertyChanged(nameof(CurrentShellForceSet));
         }
      }

      void DuplicateForceSet(CScore.ForceSet? src)
      {
         if (src == null) return;
         var copy = new CScore.ForceSet
         {
            Tag         = src.Tag + " (копия)",
            Description = src.Description,
            Kind        = src.Kind,
            Items       = src.Items.ConvertAll(i => new CScore.LoadItem
            {
               Label = i.Label, N = i.N, Mx = i.Mx, My = i.My,
               Vx = i.Vx, Vy = i.Vy, T = i.T,
               SourceElementNum = i.SourceElementNum, SourceSectionNum = i.SourceSectionNum,
            }),
            // Напряжения копируются вместе с усилиями: у строк, импортированных из ЛИРЫ/SCAD,
            // они — источник Nx/Ny/Nxy (пересчёт σ·h), без них копия теряет мембранные усилия.
            ShellItems = src.ShellItems.ConvertAll(i => new CScore.ShellLoadItem
            {
               Label = i.Label, Nx = i.Nx, Ny = i.Ny, Nxy = i.Nxy,
               Mx = i.Mx, My = i.My, Mxy = i.Mxy, Qx = i.Qx, Qy = i.Qy,
               SigmaX = i.SigmaX, SigmaY = i.SigmaY, TauXY = i.TauXY,
               SourceElementNum = i.SourceElementNum, SourceSectionNum = i.SourceSectionNum,
            }),
         };
         var col = src.Kind == "shell" ? ShellForceSets : BarForceSets;
         copy.Num = col.Count > 0 ? col.Max(s => s.Num) + 1 : 1;
         for (int i = 0; i < copy.Items.Count;      i++) copy.Items[i].Num      = i + 1;
         for (int i = 0; i < copy.ShellItems.Count; i++) copy.ShellItems[i].Num = i + 1;
         db.SaveForceSet(copy);
         ForceSets.Add(copy);
      }

      public void RefreshMaterialAreaLiveCollections()
      {
         AreasLive       = new(MaterialAreas.Where(a => a.Category == AreaCategory.Region));
         RebarGroupsLive = new(MaterialAreas.Where(a => a.Category == AreaCategory.RebarGroup));
         StirrupGroupsLive = new(MaterialAreas.Where(a => a.Category == AreaCategory.Stirrups));
         OnPropertyChanged(nameof(AreasLive));
         OnPropertyChanged(nameof(RebarGroupsLive));
         OnPropertyChanged(nameof(StirrupGroupsLive));
      }

      public void RefreshSectionLiveCollections()
      {
         FiberSectionsLive.Clear();
         OrdinaryFiberSectionsLive.Clear();
         parametricFiberSectionsLive.Clear();
         parametricSteelSectionsLive.Clear();
         // Явный профиль СП 16 параметрических МК-сечений (снимается у устаревших и отсоединённых).
         var steelService = new ParametricSteelSectionProjectService(db);
         steelService.ApplyBindings(CrossSections);
         var parametricService = new ParametricRcSectionProjectService(db);
         // Три списка: параметрические ЖБ, параметрические МК (поддержанные и неустаревшие), обычные.
         foreach (var s in CrossSections.Where(s => s is not TwoStageSection))
         {
            var state = parametricService.GetState(s);
            if (state.LoadStatus == ParametricRcDefinitionLoadStatus.Supported && !state.IsStale)
            {
               parametricFiberSectionsLive.Add(new ParametricCrossSectionTreeItem(s, state));
               continue;
            }
            var steelState = steelService.GetState(s);
            if (steelState.LoadStatus == ParametricSteelDefinitionLoadStatus.Supported && !steelState.IsStale)
               parametricSteelSectionsLive.Add(new ParametricSteelSectionTreeItem(s, steelState));
            else
               OrdinaryFiberSectionsLive.Add(s);
         }
         foreach (var s in OrdinaryFiberSectionsLive)
            FiberSectionsLive.Add(s);

         TwoStageSectionsLive.Clear();
         foreach (var s in CrossSections.OfType<TwoStageSection>())
            TwoStageSectionsLive.Add(s);
      }

       void RefreshPlateSectionsLive()
       {
          PlateSectionsLive.Clear();
          foreach (var ps in PlateSections)
             PlateSectionsLive.Add(ps);
       }

       void RefreshEquivalentSectionsLive()
       {
          EquivalentSectionsLive.Clear();
          foreach (var equivalent in EquivalentSections)
             EquivalentSectionsLive.Add(equivalent);
       }

       void RefreshEquivalentSectionsStale()
       {
          var materials = Materials
             .Where(m => m.Id != 0)
             .ToDictionary(m => m.Id);
          var service = new EquivalentSectionProjectService(db, materials);
          bool changed = false;
          foreach (var equivalent in EquivalentSections)
             changed |= service.RefreshStale(equivalent, CalcType.C);
          if (changed)
             MarkDirty(SaveCategory.EquivalentSections);
       }

      void RenumberFireSections()
      {
         for (int i = 0; i < FireSections.Count; i++)
            FireSections[i].Num = i + 1;
      }

      void NewArea()
      {
         var area = new MaterialArea
         {
            Tag = $"Область {MaterialAreas.Count + 1}",
            Category = AreaCategory.Region
         };
         CurrentPage = new Views.MaterialAreaPage(area, this);
      }

      void NewRebarGroup()
      {
         var area = new MaterialArea
         {
            Tag = $"Группа {RebarGroupsLive.Count + 1}",
            Category = AreaCategory.RebarGroup
         };
         CurrentPage = new Views.RebarGroupEditorPage(area, this);
      }

      void NewStirrupGroup()
      {
         var area = new MaterialArea
         {
            Tag = string.Format(Loc.S("StirrupTagFormat"), StirrupGroupsLive.Count + 1),
            Category = AreaCategory.Stirrups
         };
         CurrentPage = new Views.StirrupGroupPage(area, this);
      }

      void DeleteMaterialArea()
      {
         if (currentMaterialArea == null) return;
         db.DeleteMaterialArea(currentMaterialArea);
         RefreshMaterialAreaLiveCollections();
         currentMaterialArea = null;
         CurrentPage = null!;
         OnPropertyChanged(nameof(CurrentMaterialArea));
      }

      void NewPlateSection()
      {
         currentPlateSection = null;
         CurrentPage = new Views.PlateSectionPage(this);
      }

       void DeletePlateSection(CScore.PlateSection? target = null)
       {
         var ps = target ?? currentPlateSection;
         if (ps == null) return;
         var res = System.Windows.MessageBox.Show(
            Loc.S("ConfirmDeleteRegion"), Loc.S("Warning"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
         if (res != System.Windows.MessageBoxResult.Yes) return;
         db.DeletePlateSection(ps);
         if (ps == currentPlateSection)
         {
            currentPlateSection = null;
            CurrentPage = null!;
            OnPropertyChanged(nameof(CurrentPlateSection));
          }
       }

       void DeleteEquivalentSection(EquivalentSection? target = null)
       {
          var equivalent = target ?? currentEquivalentSection;
          if (equivalent == null) return;
          var res = System.Windows.MessageBox.Show(
             Loc.S("ConfirmDeleteEquivalentSection"), Loc.S("Warning"),
             System.Windows.MessageBoxButton.YesNo,
             System.Windows.MessageBoxImage.Warning);
          if (res != System.Windows.MessageBoxResult.Yes) return;
          db.DeleteEquivalentSection(equivalent);
          if (equivalent == currentEquivalentSection)
          {
             currentEquivalentSection = null;
             CurrentPage = null!;
             OnPropertyChanged(nameof(CurrentEquivalentSection));
          }
       }

       void RecalculateEquivalentSection(EquivalentSection? target = null)
       {
          var equivalent = target ?? currentEquivalentSection;
          if (equivalent == null) return;

          var materials = Materials
             .Where(m => m.Id != 0)
             .ToDictionary(m => m.Id);
          var service = new EquivalentSectionProjectService(db, materials);
          var result = service.BuildAndSave(
             equivalent.Strip, equivalent.SourceSchemaId, equivalent.SourceRegionId,
             CalcType.C, equivalent.ReductionPolicy, equivalent.WidthIntegrationPoints,
             equivalent.SpanStationFraction, equivalent);
          if (result.Section != null)
          {
             currentEquivalentSection = result.Section;
             CurrentPage = new Views.EquivalentSectionPage(result.Section, this);
             OnPropertyChanged(nameof(CurrentEquivalentSection));
          }
          if (!result.IsCalculable)
             LogService.Warning(Loc.S("EquivalentSectionRecalculateFailed"));
       }

       void DuplicatePlateSection(CScore.PlateSection? src)
      {
         if (src == null) return;
         var copy = new CScore.PlateSection
         {
            Tag                = src.Tag + " (копия)",
            H                  = src.H,
            NLayers            = src.NLayers,
            ConcreteMaterialId = src.ConcreteMaterialId,
            RebarMaterialId    = src.RebarMaterialId,
            TensionConcrete    = src.TensionConcrete,
            SofteningModel     = src.SofteningModel,
            SofteningEpsC2     = src.SofteningEpsC2,
            RebarLayers        = src.RebarLayers.ConvertAll(l => new CScore.PlateRebarLayer
            {
               Name = l.Name, InputMode = l.InputMode,
               Asx = l.Asx, Asy = l.Asy, Zsx = l.Zsx, Zsy = l.Zsy,
               DiameterX = l.DiameterX, DiameterY = l.DiameterY,
               CountPerMeterX = l.CountPerMeterX, CountPerMeterY = l.CountPerMeterY,
               SpacingX = l.SpacingX, SpacingY = l.SpacingY,
               MaterialId = l.MaterialId
            })
         };
         copy.Num = PlateSections.Count > 0 ? PlateSections.Max(s => s.Num) + 1 : 1;
         db.SavePlateSection(copy);
         PlateSections.Add(copy);
      }

      void NewFireSection()
      {
         var dlg = new Views.Dialogs.FireSectionDialog(this)
         {
            Owner = Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;

         var section = dlg.Result;
         section.Num = FireSections.Count > 0 ? FireSections.Max(s => s.Num) + 1 : 1;
         db.SaveFireSection(section);
         RenumberFireSections();
         CurrentFireSection = section;
      }

      void RenameFireSection()
      {
         if (CurrentFireSection == null) return;
         var dlg = new Views.Dialogs.FireSectionDialog(this, CurrentFireSection)
         {
            Owner = Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;

         var updated = dlg.Result;
         CurrentFireSection.Tag = updated.Tag;
         CurrentFireSection.SectionId = updated.SectionId;
         CurrentFireSection.FireDurationMin = updated.FireDurationMin;
         CurrentFireSection.FireCurve = updated.FireCurve;
         CurrentFireSection.MeshStepM = updated.MeshStepM;
         CurrentFireSection.TimeStepS = updated.TimeStepS;
         CurrentFireSection.BcPreset = updated.BcPreset;
         CurrentFireSection.HoleBcPreset = updated.HoleBcPreset;
         db.SaveFireSection(CurrentFireSection);
         OnPropertyChanged(nameof(FireSections));
         OnPropertyChanged(nameof(CurrentFireSection));
         CurrentPage = new Views.FireSectionView(CurrentFireSection, this);
      }

      void DeleteFireSection()
      {
         if (CurrentFireSection == null) return;
         var res = MessageBox.Show(
            Loc.S("FireSection_ConfirmDelete"),
            Loc.S("Warning"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;

         int deletedId = CurrentFireSection.Id;
         db.DeleteFireSection(deletedId);
         RenumberFireSections();
         CurrentFireSection = null;
         CurrentPage = null!;
      }

      void NewCalcTask(string? groupKey = null)
      {
         var dlg = new CalcTaskPropsDialog(this, groupKey: groupKey)
         {
            Owner = Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;
         var ct = dlg.Result;
         ct.Num = CalcTasks.Count > 0 ? CalcTasks.Max(t => t.Num) + 1 : 1;
         db.SaveCalcTask(ct);
         LogService.Info(string.Format(Loc.S("CalcTaskCreated"), ct.Tag));
      }

      async Task RunCalcTaskAsync(CalcTask? ct)
      {
         if (ct == null) return;
         await CalcTaskExecutor.RunAsync(this, ct);
      }

      void EditCalcTask(CalcTask? ct)
      {
         if (ct == null) return;
         var dlg = new CalcTaskPropsDialog(this, ct)
         {
            Owner = Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;
         var src = dlg.Result;
         ct.Tag         = src.Tag;
         ct.Kind        = src.Kind;
         ct.SectionId   = src.SectionId;
         ct.ForceSetId  = src.ForceSetId;
         ct.ForceItemId = src.ForceItemId;
         ct.CalcType    = src.CalcType;
         ct.ParamsJson  = src.ParamsJson;
         db.SaveCalcTask(ct);
         CalcTaskModified?.Invoke();
      }

      void DeleteCalcTask(CalcTask? ct)
      {
         if (ct == null) return;
         var res = MessageBox.Show(Loc.S("ConfirmDeleteCalcTask"), Loc.S("Warning"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         db.DeleteCalcTask(ct);
      }

      void DeleteCalcResults(CalcTask? ct)
      {
         if (ct == null) return;
         var res = MessageBox.Show(string.Format(Loc.S("ConfirmDeleteCalcResults"), ct.Tag), Loc.S("Warning"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         db.DeleteCalcResultsByTaskId(ct.Id);
      }

      private record CircleCsvRow
      {
         public string Tag    { get; init; } = "";
         public double X      { get; init; }
         public double Y      { get; init; }
         public double Radius { get; init; }
      }

      #region FEM

      void NewFemSchema()
      {
         var schema = new CScore.Fem.FemSchema { Tag = "Схема", SourceType = "internal" };
         db.SaveFemSchema(schema);
      }

      void DuplicateFemSchema(CScore.Fem.FemSchema? schema)
      {
         if (schema == null) return;
         db.DuplicateFemSchema(schema.Id, schema.Tag + " (копия)");
      }

      void RenameFemSchema(CScore.Fem.FemSchema? schema)
      {
         if (schema == null) return;
         var dlg = new Views.Dialogs.TextInputDialog(
            Loc.S("FemSchemaRenameTitle"), Loc.S("FemSchemaRenameLabel"), schema.Tag);
         if (dlg.ShowDialog() != true) return;
         schema.Tag = dlg.Value;
         db.SaveFemSchema(schema);
      }

      void ImportLiraSchemaFromCsv()
      {
         var csvFilter = Loc.S("CsvFileFilter");

         var nodesPath = FileDialogService.OpenFile(csvFilter, Loc.S("ImportLiraSchemaNodesTitle"));
         if (nodesPath == null) return;

         var elemsPath = FileDialogService.OpenFile(csvFilter, Loc.S("ImportLiraSchemaElemsTitle"));
         if (elemsPath == null) return;

         var barStiffPath   = FileDialogService.OpenFile(csvFilter, Loc.S("ImportLiraSchemaBarStiffTitle"));
         var plateStiffPath = FileDialogService.OpenFile(csvFilter, Loc.S("ImportLiraSchemaPlateStiffTitle"));

         try
         {
            var raw = CScore.Import.LiraCsvSchemaParser.Parse(
               nodesPath, elemsPath, barStiffPath, plateStiffPath);

            var schemaName = System.IO.Path.GetFileNameWithoutExtension(nodesPath);
            var schema = new CScore.Fem.FemSchema
            {
               Tag        = schemaName,
               SourceType = "lira",
            };
            db.SaveFemSchema(schema);

            var meshNodes = CScore.Import.LiraSchemaConverter.ToFemMeshNodes(raw, schema.Id);
            var meshElements = CScore.Import.LiraSchemaConverter.ToFemMeshBarElements(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMeshShellElements(raw, schema.Id))
                .ToArray();
            var memberGroups = CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByStiffness(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByPlateStiffness(raw, schema.Id))
                .ToArray();

            SaveImportedSchema(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
            SaveLiraSupports(schema, raw);
            RefreshFemSchemaTreeCounts(schema);

            int barCount   = raw.Elements.Count(e => e.NodeIds.Length == 2);
            int shellCount = raw.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
            LogService.Info(string.Format(Loc.S("ImportLiraSchemaSuccess"),
               raw.Nodes.Count, barCount, shellCount, memberGroups.Length));
         }
         catch (Exception ex)
         {
            System.Windows.MessageBox.Show(ex.Message,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// Закрепления узлов ЛИРЫ → закрепления узлов сетки, C1 пластин (таблица 5) → <c>FoundationC1</c> КЭ
      /// (происхождение «import:lira»).
      /// </summary>
      void SaveLiraSupports(CScore.Fem.FemSchema schema, CScore.Import.LiraSchemaData raw)
      {
         var supports = CScore.Import.LiraSchemaConverter.ToFemMeshNodeSupports(raw);
         var props = CScore.Import.LiraSchemaConverter.ToFemElementBoundaryProps(raw);
         if (supports.Length == 0 && props.Count == 0) return;
         db.SaveFemBoundary(schema.Id, CScore.Import.LiraSchemaConverter.BoundaryOrigin, supports, [], [],
            props.Count > 0 ? props : null);
         if (supports.Length > 0) LogService.Info(string.Format(Loc.S("LiraSupportsImported"), supports.Length));
         if (props.Count > 0) LogService.Info(string.Format(Loc.S("LiraFoundationImported"), props.Count));
         if (raw.PlateFoundationBeyondC1 > 0)
            LogService.Warning(string.Format(Loc.S("LiraFoundationBeyondC1"), raw.PlateFoundationBeyondC1));
      }

      /// <summary>Сохраняет прочитанный импорт в только что созданную схему (<see cref="Utilites.DatabaseService.SaveFemImport"/>).
      /// Номера, которых нет в сетке (КЭ пропущенных типов), из групп убираются с предупреждением.</summary>
      void SaveImportedSchema(CScore.Fem.FemSchema schema, CScore.Fem.Import.FemImportResult result)
      {
         int pruned = result.PruneMissingGroupTags();
         if (pruned > 0)
            LogService.Warning(string.Format(Loc.S("FemImportGroupTagsPruned"), pruned));
         var warnings = db.SaveFemImport(schema.Id, result);
         foreach (var w in warnings.Take(20))
            LogService.Warning(w.Message);
         if (warnings.Count > 20)
            LogService.Warning(string.Format(Loc.S("FemImportMoreWarnings"), warnings.Count - 20));
      }

      void ImportLiraSchemaFromFile()
      {
         string? fileName = FileDialogService.OpenFile(
            filter: Loc.S("LiraFileFilter"),
            title:  Loc.S("ImportLiraSchemaFromFileTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         try
         {
            var raw = CScore.Import.LiraFileParser.Parse(fileName);

            var schemaName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var schema = new CScore.Fem.FemSchema
            {
               Tag        = schemaName,
               SourceType = "lira",
            };
            db.SaveFemSchema(schema);

            var meshNodes = CScore.Import.LiraSchemaConverter.ToFemMeshNodes(raw, schema.Id);
            var meshElements = CScore.Import.LiraSchemaConverter.ToFemMeshBarElements(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMeshShellElements(raw, schema.Id))
                .ToArray();
            var memberGroups = CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByStiffness(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByPlateStiffness(raw, schema.Id))
                .ToArray();

            SaveImportedSchema(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
            SaveLiraSupports(schema, raw);
            RefreshFemSchemaTreeCounts(schema);

            int barCount   = raw.Elements.Count(e => e.NodeIds.Length == 2);
            int shellCount = raw.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
            LogService.Info(string.Format(Loc.S("ImportLiraSchemaSuccess"),
               raw.Nodes.Count, barCount, shellCount, memberGroups.Length));
         }
         catch (Exception ex)
         {
            System.Windows.MessageBox.Show(ex.Message,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      void ImportScadTopologyFromTxt()
      {
         string? fileName = FileDialogService.OpenFile(
            filter: Loc.S("ScadTextFileFilter"),
            title:  Loc.S("ImportScadTopologyTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         var import = CScore.Import.ScadTextParser.Parse(fileName);
         if (!import.Success)
         {
            System.Windows.MessageBox.Show(
               import.Error ?? Loc.S("ImportScadFailed"),
               Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }

         foreach (var w in import.Warnings)
            LogService.Warning(w);

         var data = import.Data!;
         var schema = new CScore.Fem.FemSchema
         {
            Tag        = Path.GetFileNameWithoutExtension(fileName),
            SourceType = "scad",
         };
         db.SaveFemSchema(schema);

         var meshNodes    = CScore.Import.ScadSchemaConverter.ToFemMeshNodes(data, schema.Id);
         var meshElements = CScore.Import.ScadSchemaConverter.ToFemMeshElements(data, schema.Id);
         var memberGroups = CScore.Import.ScadSchemaConverter.ToFemMemberGroups(data, schema.Id);

         SaveImportedSchema(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
         RefreshFemSchemaTreeCounts(schema);

         int barCount   = meshElements.Count(e => e.ElemType == "beam");
         int shellCount = meshElements.Count(e => e.ElemType == "shell");
         LogService.Info(string.Format(Loc.S("ImportScadSuccess"),
            meshNodes.Length, barCount, shellCount, memberGroups.Length, data.Groups.Count));
      }

      /// <summary>
      /// Импорт схемы из проекта SCAD (.SPR) через SCADAPIX.dll: диалог → чтение в фоне (вся сессия
      /// SCAD API в одном потоке) → новая схема с сеткой, группами КЭ и жёсткостями.
      /// </summary>
      async void ImportScadSchemaFromApi()
      {
         if (IsBusy) return;
         var settings = db.LoadScadApiSettings();
         var vm = new ViewModels.ScadApiImportVM(settings, FileDialogService);
         var dialog = new Views.ScadApiImportDialog(vm) { Owner = System.Windows.Application.Current?.MainWindow };
         if (dialog.ShowDialog() != true) return;
         vm.ApplyTo(settings);
         db.SaveScadApiSettings(settings);

         string spr = vm.SprPath, dllDir = vm.DllDirectory;
         // ЖБ-группы читаются всегда (привязки и классы для подбора SCAD); флажок — только группы КЭ «ЖБ: …».
         var options = new Services.Scad.ScadReadOptions(vm.ReadOutputAxes, ConcreteGroups: true);
         bool concreteAsMemberGroups = vm.ConcreteGroupsAsMemberGroups;
         bool steelAsMemberGroups = vm.SteelGroupsAsMemberGroups;
         var cts = BeginBusyWithCancellation(Loc.S("ScadApiImporting"), indeterminate: false);
         var progress = new Progress<double>(f => ReportBusyProgress(f));
         try
         {
            var read = await Task.Run(() =>
            {
               Services.Scad.ScadApiTrace.Write("Импорт: ожидание Gate");
               Services.Scad.ScadApiNative.Gate.Wait(cts.Token);
               Services.Scad.ScadApiTrace.Write("Импорт: Gate получен");
               try
               {
                  var native = Services.Scad.ScadApiNative.Load(dllDir);
                  using var session = new Services.Scad.ScadApiSession(native);
                  session.Open(spr);
                  return Services.Scad.ScadApiReader.Read(session, options, progress, cts.Token);
               }
               finally { Services.Scad.ScadApiNative.Gate.Release(); }
            }, cts.Token);

            var data = read.Data;
            var schema = new CScore.Fem.FemSchema
            {
               Tag        = Path.GetFileNameWithoutExtension(spr),
               SourceType = "scad",
               SourcePath = Path.GetFullPath(spr),
            };
            db.SaveFemSchema(schema);

            var meshNodes    = ScadSchemaConverter.ToFemMeshNodes(data, schema.Id);
            var meshElements = ScadSchemaConverter.ToFemMeshElements(data, schema.Id);
            var blockGroups  = ScadSchemaConverter.ToFemMemberGroupsByBlocks(data, schema.Id);
            var concreteGroups = concreteAsMemberGroups
               ? ScadSchemaConverter.ToFemMemberGroupsByConcreteGroups(data, schema.Id)
               : [];
            var steelGroups = steelAsMemberGroups
               ? ScadSchemaConverter.ToFemMemberGroupsBySteelGroups(data, schema.Id)
               : [];
            var memberGroups = ScadSchemaConverter.ToFemMemberGroups(data, schema.Id)
               .Concat(blockGroups).Concat(concreteGroups).Concat(steelGroups).ToArray();
            var stiffnesses  = ScadSchemaConverter.ToSchemaStiffnesses(data);

            SaveImportedSchema(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
            db.SaveFemSchemaStiffnesses(schema.Id, stiffnesses);
            SaveScadConcreteGroups(schema.Id, data.ConcreteGroups);
            if (data.SteelGroups.Count > 0) SaveScadSteelGroups(schema.Id, data.SteelGroups);
            SaveScadSteelProfiles(schema.Id, stiffnesses, dllDir);
            if (data.AssignedRebar != null) SaveScadAssignedRebar(schema.Id, data.AssignedRebar);
            if (data.AnalysisModel != null)
            {
               SaveScadAnalysisModel(schema.Id, data.AnalysisModel);
               TransferScadLoads(schema, data.AnalysisModel);
               TransferScadBoundary(schema, data.AnalysisModel, showSummary: true);
            }
            RefreshFemSchemaTreeCounts(schema);

            foreach (var (type, count) in read.SkippedByType.OrderBy(kv => kv.Key))
               LogService.Warning(string.Format(Loc.S("ScadApiSkippedType"), type, count));
            if (read.DeletedElements > 0)
               LogService.Info(string.Format(Loc.S("ScadApiDeletedElements"), read.DeletedElements));
            if (read.BarStiffnessesWithoutShape.Count > 0)
               LogService.Warning(string.Format(Loc.S("ScadApiBarsWithoutShape"),
                  string.Join(", ", read.BarStiffnessesWithoutShape)));
            if (read.DegenerateAxisElements > 0)
               LogService.Warning(string.Format(Loc.S("ScadApiDegenerateAxes"), read.DegenerateAxisElements));

            int barCount   = meshElements.Count(e => e.ElemType == "beam");
            int shellCount = meshElements.Count(e => e.ElemType == "shell");
            string done = string.Format(Loc.S("ScadApiImportSuccess"), schema.Tag, meshNodes.Length, barCount,
               shellCount, memberGroups.Length, blockGroups.Length, concreteGroups.Length, stiffnesses.Length,
               steelGroups.Length);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (OperationCanceledException)
         {
            EndBusy(Loc.S("ScadApiImportCancelled"));
         }
         catch (Services.Scad.ScadApiException ex)
         {
            EndBusy();
            string msg = ex.Format(Loc.S);
            LogService.Error(msg);
            System.Windows.MessageBox.Show(msg, Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
         }
         catch (Exception ex)
         {
            EndBusy();
            string msg = string.Format(Loc.S("ScadApiUnexpectedError"), ex.Message);
            LogService.Error(msg + Environment.NewLine + ex);
            System.Windows.MessageBox.Show(msg, Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }

      /// <summary>Порог строк РСУ SCAD, выше которого импорт подтверждается пользователем.</summary>
      const long ScadRsuRowsConfirmThreshold = 1_000_000;

      /// <summary>
      /// Усилия из проекта SCAD (.SPR) через SCADAPIX.dll на цель: группу КЭ, конструктивный элемент или всю
      /// схему (из главного меню — текущая группа). Файл — <see cref="CScore.Fem.FemSchema.SourcePath"/>; не
      /// задан или не найден — выбор файла, путь запоминается в схеме. Для РСУ — выбор групп (C/CL/N/NL).
      /// </summary>
      async void ImportScadForcesFromApi(object? target, Services.Scad.ScadForceReadKind kind)
      {
         if (IsBusy) return;
         target ??= currentFemMember;
         var schema = target switch
         {
            CScore.Fem.FemSchema s => s,
            CScore.Fem.IFemCheckable c => FemSchemas.FirstOrDefault(s => s.Id == FemTargetSchemaId(c)),
            _ => null,
         };
         if (schema == null)
         {
            MessageBox.Show(Loc.S("ScadForcesNoTarget"), Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         if (schema.SourceType != "scad")
         {
            MessageBox.Show(string.Format(Loc.S("ScadForcesNotScadSchema"), schema.Tag), Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         // Вид КЭ — по сетке схемы (номер КЭ SCAD = тег КЭ сетки).
         var kindById = new Dictionary<int, CScore.Import.ScadElementKind>();
         foreach (var e in db.GetFemMeshElements(schema.Id))
            if (int.TryParse(e.ElemTag, out int id))
               kindById[id] = e.ElemType == "shell" ? CScore.Import.ScadElementKind.Shell : CScore.Import.ScadElementKind.Beam;
         int[] ids = target is CScore.Fem.IFemCheckable member
            ? FemTargetElementNumbers(member).Distinct().ToArray()
            : [.. kindById.Keys];
         var targets = ids.Where(kindById.ContainsKey).ToDictionary(id => id, id => kindById[id]);
         int notInMesh = ids.Length - targets.Count;
         if (targets.Count == 0)
         {
            MessageBox.Show(Loc.S("ScadForcesNoElements"), Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         string? spr = ResolveScadProjectPath(schema);
         if (spr == null) return;
         var settings = db.LoadScadApiSettings();
         string? dllDir = Services.Scad.ScadInstallLocator.ContainsDll(settings.DllDirectory)
            ? settings.DllDirectory
            : Services.Scad.ScadInstallLocator.FindDllDirectory();
         if (dllDir == null)
         {
            MessageBox.Show(Loc.S("ScadForcesNoDll"), Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         string? work = !string.IsNullOrWhiteSpace(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory)
            ? settings.WorkDirectory
            : Services.Scad.ScadInstallLocator.FindWorkDirectory();

         HashSet<int>? rsuGroups = null;
         if (kind == Services.Scad.ScadForceReadKind.Rsu)
         {
            var dlg = new Views.ScadRsuGroupsDialog(settings.RsuGroups);
            if (dlg.ShowDialog() != true) return;
            settings.RsuGroups = dlg.SelectedGroups;
            db.SaveScadApiSettings(settings);
            rsuGroups = [.. settings.RsuGroups];
         }

         string targetTag = target is CScore.Fem.IFemCheckable t ? t.Tag : schema.Tag;
         var options = new CScore.Import.ScadXlsImportOptions
         {
            TonToKnFactor = LiraImportSettings.TonToKnFactor,
            InvertBarBendingMoments = LiraImportSettings.InvertBarBendingMoments,
            InvertShellBendingMoments = LiraImportSettings.InvertShellBendingMoments,
         };
         var cts = BeginBusyWithCancellation(string.Format(Loc.S("ScadForcesImporting"), targets.Count, targetTag),
            indeterminate: false);
         var progress = new Progress<double>(f => ReportBusyProgress(f));
         try
         {
            var read = await Task.Run(() =>
            {
               Services.Scad.ScadApiTrace.Write("Импорт: ожидание Gate");
               Services.Scad.ScadApiNative.Gate.Wait(cts.Token);
               Services.Scad.ScadApiTrace.Write("Импорт: Gate получен");
               try
               {
                  var native = Services.Scad.ScadApiNative.Load(dllDir);
                  using var session = new Services.Scad.ScadApiSession(native);
                  session.Open(spr);
                  return Services.Scad.ScadApiForceReader.Read(session, work, targets, kind, progress, cts.Token, rsuGroups);
               }
               finally { Services.Scad.ScadApiNative.Gate.Release(); }
            }, cts.Token);

            if (kind == Services.Scad.ScadForceReadKind.Rsu)
            {
               long rows = read.Rsu.Sum(r => (long)r.Rows.Count);
               if (rows > ScadRsuRowsConfirmThreshold && MessageBox.Show(
                     string.Format(Loc.S("ScadRsuManyRowsConfirm"), rows, targets.Count), Loc.S("ImportScadErrorTitle"),
                     MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
               {
                  EndBusy(Loc.S("ScadForcesCancelled"));
                  return;
               }
            }

            var sets = await Task.Run(() => kind switch
            {
               Services.Scad.ScadForceReadKind.LoadCases =>
                  CScore.Import.ScadForceSetBuilder.LoadCases(read.Forces, read.Catalog, schema.Id, targetTag, options),
               Services.Scad.ScadForceReadKind.Combinations =>
                  CScore.Import.ScadForceSetBuilder.Combinations(read.Forces, read.Catalog, schema.Id, targetTag, options),
               _ => CScore.Import.ScadForceSetBuilder.Rsu(read.Rsu, schema.Id, targetTag, options),
            }, cts.Token);

            SaveImportedForceSets(sets, target as CScore.Fem.IFemCheckable);

            if (notInMesh > 0) LogService.Warning(string.Format(Loc.S("ScadForcesNotInMesh"), notInMesh));
            if (read.MissingElements > 0) LogService.Warning(string.Format(Loc.S("ScadForcesMissing"), read.MissingElements));
            if (read.WrongKindElements > 0) LogService.Warning(string.Format(Loc.S("ScadForcesWrongKind"), read.WrongKindElements));
            if (read.NoResultElements > 0) LogService.Info(string.Format(Loc.S("ScadForcesNoResult"), read.NoResultElements));
            string done = string.Format(Loc.S("ScadForcesSuccess"), sets.Count, targetTag,
               sets.Sum(s => s.RowCount));
            LogService.Info(done);
            EndBusy(done);
         }
         catch (OperationCanceledException)
         {
            EndBusy(Loc.S("ScadForcesCancelled"));
         }
         catch (Services.Scad.ScadApiException ex)
         {
            EndBusy();
            string msg = ex.Format(Loc.S);
            LogService.Error(msg);
            MessageBox.Show(msg, Loc.S("ImportScadErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
         }
         catch (Exception ex)
         {
            EndBusy();
            string msg = string.Format(Loc.S("ScadApiUnexpectedError"), ex.Message);
            LogService.Error(msg + Environment.NewLine + ex);
            MessageBox.Show(msg, Loc.S("ImportScadErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }

      /// <summary>Файл .SPR схемы: сохранённый путь, если файл на месте, иначе выбор файла.</summary>
      string? ResolveScadProjectPath(CScore.Fem.FemSchema schema)
      {
         if (!string.IsNullOrWhiteSpace(schema.SourcePath) && File.Exists(schema.SourcePath))
            return schema.SourcePath;
         if (!string.IsNullOrWhiteSpace(schema.SourcePath))
            LogService.Warning(string.Format(Loc.S("ScadForcesProjectMoved"), schema.SourcePath));
         return ChooseScadProjectPath(schema);
      }

      /// <summary>Выбрать файл .SPR для схемы и запомнить его; null — отказ.</summary>
      string? ChooseScadProjectPath(CScore.Fem.FemSchema schema)
      {
         string? path = FileDialogService.OpenFile(Loc.S("ScadApiSprFilter"),
            string.Format(Loc.S("ScadForcesSprBrowseTitle"), schema.Tag));
         if (string.IsNullOrEmpty(path)) return null;
         db.UpdateFemSchemaSourcePath(schema, Path.GetFullPath(path));
         return schema.SourcePath;
      }

      /// <summary>Источник схемы цели (lira, scad, internal …): у схемы — свой, у группы и конструктивного
      /// элемента — их схемы. Управляет видимостью пунктов меню, привязанных к программе-источнику.</summary>
      public string? FemSourceTypeOf(object? target) => target switch
      {
         CScore.Fem.FemSchema s => s.SourceType,
         CScore.Fem.IFemCheckable c => FemSchemas.FirstOrDefault(s => s.Id == FemTargetSchemaId(c))?.SourceType,
         _ => null,
      };

      /// <summary>Команда импорта усилий SCAD через DLL по виду: "lc" — загружения, "rsn" — комбинации,
      /// иначе — РСУ. Параметр команды — цель (группа, конструктивный элемент или схема).</summary>
      public ICommand ImportScadForcesCommand(string? kind) => kind switch
      {
         "lc"  => ImportScadLoadCasesFromApiCommand,
         "rsn" => ImportScadCombinationsFromApiCommand,
         _     => ImportScadRsuFromApiCommand,
      };

      async void ImportScadRsu2()
      {
         string? fileName = FileDialogService.OpenFile(
            filter: Loc.S("ScadRsu2FileFilter"),
            title: Loc.S("ScadRsu2DialogTitle"));
         if (string.IsNullOrEmpty(fileName)) return;

         BeginBusy(Loc.S("ScadRsu2Importing"));
         try
         {
            var import = await Task.Run(() =>
               CScore.Import.ScadRsu2Importer.ImportFile(fileName));

            if (!import.Success)
            {
               EndBusy();
               System.Windows.MessageBox.Show(
                  import.Error ?? "Ошибка импорта RSU2",
                  Loc.S("ImportScadErrorTitle"),
                  MessageBoxButton.OK, MessageBoxImage.Error);
               return;
            }

            int nextNum = ForceSets.Count > 0 ? ForceSets.Max(f => f.Num) + 1 : 1;
            foreach (var fs in import.ForceSets)
            {
               fs.Num = nextNum++;
               db.SaveForceSet(fs);
               if (!ForceSets.Contains(fs))
                  ForceSets.Add(fs);
            }

            string done = string.Format(Loc.S("ScadRsu2Success"), import.ForceSets.Count);
            LogService.Info(done + " — " + Path.GetFileName(fileName));
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            System.Windows.MessageBox.Show(ex.Message, Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }

      async void ImportScadForces(CScore.Import.ScadXlsImportMode mode)
      {
         string? fileName = FileDialogService.OpenFile(
            filter: Loc.S("ScadXlsFileFilter"),
            title: mode switch
            {
               CScore.Import.ScadXlsImportMode.LoadCases => Loc.S("ImportScadForcesTitleLoadCases"),
               CScore.Import.ScadXlsImportMode.Combinations => Loc.S("ImportScadForcesTitleCombinations"),
               _ => Loc.S("ImportScadForcesTitleRsu"),
            });
         if (string.IsNullOrEmpty(fileName)) return;

         string? seed = null;
         if (currentFemMember != null)
         {
            try
            {
               var ids = db.GetFemCheckScope(currentFemMember).ElementNumbers;
               if (ids.Count > 0)
                  seed = string.Join(", ", ids);
            }
            catch { /* ignore bad json */ }
         }

         var dlg = new Views.ScadForceImportDialog(seed) { Owner = System.Windows.Application.Current.MainWindow };
         if (dlg.ShowDialog() != true) return;

         HashSet<int> elementIds = [];
         if (!dlg.ImportAllElements)
         {
            if (!CScore.Import.ScadElementIdParser.TryParse(dlg.ElementText, out elementIds, out var parseError))
            {
               System.Windows.MessageBox.Show(
                  parseError ?? Loc.S("ImportScadFailed"),
                  Loc.S("ImportScadErrorTitle"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
         }

         var options = new CScore.Import.ScadXlsImportOptions
         {
            TonToKnFactor = LiraImportSettings.TonToKnFactor,
            InvertBarBendingMoments = LiraImportSettings.InvertBarBendingMoments,
            InvertShellBendingMoments = LiraImportSettings.InvertShellBendingMoments,
            ElementIds = elementIds,
            ImportAllElements = dlg.ImportAllElements,
         };

         BeginBusy(Loc.S("ImportScadForcesStarted"), indeterminate: false);
         try
         {
            var progress = new Progress<CScore.Import.ScadXlsProgress>(p =>
               ReportBusyProgress(p.Fraction, string.IsNullOrEmpty(p.Message) ? null : p.Message));

            var import = await Task.Run(() =>
               CScore.Import.ScadXlsForceImporter.ImportFile(fileName, mode, options, progress));

            if (!import.Success)
            {
               EndBusy();
               System.Windows.MessageBox.Show(
                  import.Error ?? import.Warning ?? Loc.S("ImportScadForcesNoRows"),
                  Loc.S("ImportScadErrorTitle"),
                  MessageBoxButton.OK,
                  import.Error != null ? MessageBoxImage.Error : MessageBoxImage.Warning);
               return;
            }

            if (!string.IsNullOrEmpty(import.Warning))
               LogService.Warning(import.Warning);

            ReportBusyProgress(0.97, Loc.S("ImportScadForcesSaving"));
            int memberId = currentFemMember?.Id ?? 0;
            int nextNum = ForceSets.Count > 0 ? ForceSets.Max(f => f.Num) + 1 : 1;
            foreach (var fs in import.ForceSets)
            {
               fs.Num = nextNum++;
               if (memberId > 0)
                  fs.SourceMemberId = memberId;
               db.SaveForceSet(fs);
               if (!ForceSets.Contains(fs))
                  ForceSets.Add(fs);
            }

            string done = string.Format(Loc.S("ImportScadForcesSuccess"),
               import.ForceSets.Count, import.RowsMatched);
            LogService.Info(done + " — " + Path.GetFileName(fileName));
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            System.Windows.MessageBox.Show(ex.Message, Loc.S("ImportScadErrorTitle"),
               MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }

      void BuildFemRootNodes()
      {
         femSchemasGroup = new ViewModels.FemSchemasGroupNode(FemSchemas, db, ForceSets);
         femChecksRoot   = new ViewModels.FemChecksRootNode(FemChecks);
         FemRootNodes.Clear();
         FemRootNodes.Add(femSchemasGroup);
         FemRootNodes.Add(femChecksRoot);
      }

      /// <summary>Обновляет в дереве счётчики сохранённой расчётной сетки схемы.</summary>
      public void ReloadFemMeshSnapshotTree(int schemaId)
          => femSchemasGroup?.ReloadMeshSnapshot(schemaId);

      internal void RefreshFemSchemaTreeCounts(CScore.Fem.FemSchema schema)
      {
         var vm = femSchemasGroup?.Schemas.FirstOrDefault(x => x.Schema == schema);
         vm?.ReloadTopology();
         vm?.ReloadMeshSnapshot();
      }



      void DeleteFemSchema(CScore.Fem.FemSchema? schema = null)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         db.DeleteFemSchema(schema);
         if (currentFemSchema == schema)
         {
            currentFemSchema = null;
            CurrentPage = null!;
         }
      }

      async void ImportLiraSchemaFromApi()
      {
         BeginBusy(Loc.S("StatusImportingSchema"));
         try
         {
            int? liraVersion = null;
            string? liraTitle = null;
            double tonToKn = LiraImportSettings.TonToKnFactor;
            var raw = await RunOnStaThread(() => Services.LiraApiSchemaReader.Read(out liraVersion, out liraTitle, tonToKn));
            var schema = new CScore.Fem.FemSchema { Tag = liraTitle ?? "Схема ЛИРА-САПР (API)", SourceType = "lira" };
            db.SaveFemSchema(schema);
            var meshNodes = CScore.Import.LiraSchemaConverter.ToFemMeshNodes(raw, schema.Id);
            var meshElements = CScore.Import.LiraSchemaConverter.ToFemMeshBarElements(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMeshShellElements(raw, schema.Id))
                .ToArray();
            var memberGroups = CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByStiffness(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByPlateStiffness(raw, schema.Id))
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByConstructiveBlocks(raw, schema.Id))
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMemberGroupsByReinforcementTypes(raw, schema.Id))
                .ToArray();
            SaveImportedSchema(schema, CScore.Fem.Import.FemImportResult.MeshOnly(meshNodes, meshElements, memberGroups));
            SaveLiraSupports(schema, raw);
            db.SaveFemSchemaConstructiveBlocks(schema.Id, raw.ConstructiveBlocks);
            db.SaveFemSchemaStiffnesses(schema.Id, raw.Stiffnesses);
            SaveLiraSteelProfiles(schema.Id, raw.Stiffnesses);
            RefreshFemSchemaTreeCounts(schema);
            int barCount   = raw.Elements.Count(e => e.NodeIds.Length == 2);
            int shellCount = raw.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
            int blockCount = raw.ConstructiveBlocks.Count;
            string done = string.Format(Loc.S("ImportLiraSchemaSuccess"),
               raw.Nodes.Count, barCount, shellCount, memberGroups.Length);
            if (blockCount > 0)
                done += $" ({blockCount} кБ)";
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            string msg = ex.Message;
            if (msg.Length > 300)
            {
               LogService.Error("ДИАГНОСТИКА ЛИРА-САПР COM:\n" + msg);
               msg = msg.Split('\n')[0] + "\n\nПодробности — в журнале событий.";
            }
            System.Windows.MessageBox.Show(msg,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// Дозагрузить к схеме файл описаний ТЗА ЛИРЫ (.RBT): разобрать, сверить с номерами ТЗА у КЭ
      /// (таблица «Элементы - ТЗА», сохранена при импорте) и сохранить файл при схеме (заменяет прежний).
      /// </summary>
      void LoadLiraRbt(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         string? path = FileDialogService.OpenFile(Loc.S("LiraRbtFilter"),
            string.Format(Loc.S("LiraRbtOpenTitle"), schema.Tag));
         if (path == null) return;

         string name = System.IO.Path.GetFileName(path);
         try
         {
            byte[] data = System.IO.File.ReadAllBytes(path);
            var parsed = CScore.Import.LiraRbtReader.Read(data);
            foreach (var w in parsed.Warnings)
               LogService.Warning(w);
            if (parsed.PlateTypes.Count == 0 && parsed.BarTypes.Count == 0 && parsed.Skipped.Count == 0)
            {
               LogService.Warning(string.Format(Loc.S("LiraRbtReadError"), name, Loc.S("LiraRbtNoTypes")));
               return;
            }

            var meshElements = db.GetFemMeshElements(schema.Id);
            var assigned = meshElements
               .Where(e => !string.IsNullOrWhiteSpace(e.ReinforcementTypeIds))
               .SelectMany(e => e.ReinforcementTypeIds!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
               .Select(t => int.TryParse(t, out int id) ? id : (int?)null)
               .OfType<int>()
               .ToHashSet();
            if (assigned.Count == 0)
               LogService.Warning(Loc.S("LiraRbtSchemaHasNoTypes"));
            var known = parsed.PlateTypes.Keys.Concat(parsed.BarTypes.Keys).Concat(parsed.Skipped.Select(t => t.Id)).ToHashSet();
            var missing = assigned.Where(id => !known.Contains(id)).Order().ToList();
            if (missing.Count > 0)
               LogService.Warning(string.Format(Loc.S("LiraRbtMissingTypes"), string.Join(", ", missing)));
            // Схемы, импортированные раньше, номеров ТЗА у стержней не хранят.
            if (parsed.BarTypes.Count > 0
                && !meshElements.Any(e => e.ElemType == "beam" && !string.IsNullOrWhiteSpace(e.ReinforcementTypeIds)))
               LogService.Warning(Loc.S("LiraRbtBarsHaveNoTypes"));

            db.SaveFemSchemaReinforcementFile(schema.Id, name, data);
            LogService.Info(string.Format(Loc.S("LiraRbtLoaded"), name, parsed.PlateTypes.Count, parsed.BarTypes.Count, parsed.Skipped.Count));
         }
         catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                        or System.IO.InvalidDataException)
         {
            LogService.Warning(string.Format(Loc.S("LiraRbtReadError"), name, ex.Message));
         }
      }

      /// <summary>
      /// Обновить номера ТЗА у КЭ схемы по таблице «Элементы - ТЗА» открытой в ЛИРЕ схемы. Нужна схемам,
      /// импортированным до того, как номера ТЗА стали сохраняться у стержней, и после правки ТЗА в ЛИРЕ.
      /// </summary>
      async void RefreshLiraReinforcementTypes(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         BeginBusy(Loc.S("LiraTzaRefreshBusy"));
         try
         {
            int? liraVersion = null;
            var raw = await RunOnStaThread(() => Services.LiraApiSchemaReader.ReadElementReinforcementTypes(out liraVersion));
            var byTag = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (elemId, ids) in raw)
               if (ids.Length > 0)
                  byTag[elemId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = string.Join(" ", ids.Distinct().Order());

            // В ЛИРЕ может быть открыта другая схема: номера КЭ таблицы должны быть номерами КЭ этой схемы.
            var mesh = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in db.GetFemMeshElements(schema.Id))
               if (e.Origin == CScore.Fem.FemMember.MeshSourceImported)
                  mesh.TryAdd(e.ElemTag.Trim(), e.ElemType);
            int foreign = byTag.Keys.Count(tag => !mesh.ContainsKey(tag));
            if (byTag.Count == 0 || foreign > 0)
            {
               EndBusy();
               System.Windows.MessageBox.Show(
                  byTag.Count == 0 ? Loc.S("LiraTzaRefreshEmpty") : string.Format(Loc.S("LiraTzaRefreshForeign"), foreign, byTag.Count, schema.Tag),
                  Loc.S("FemSchemaRefreshLiraTza"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
               return;
            }

            db.ReplaceFemElementReinforcementTypes(schema.Id, byTag);
            string done = string.Format(Loc.S("LiraTzaRefreshDone"),
               byTag.Count(kv => mesh[kv.Key] == "shell"), byTag.Count(kv => mesh[kv.Key] == "beam"));
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Error(ex.Message);
            System.Windows.MessageBox.Show(ex.Message.Split('\n')[0], Loc.S("FemSchemaRefreshLiraTza"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// Обновить углы согласования местных осей пластин (оси выдачи усилий) у КЭ схемы по таблице
      /// «местные оси пластин» открытой в ЛИРЕ схемы. Нужна схемам, импортированным до того, как угол
      /// стал сохраняться у КЭ, и после пересогласования осей в ЛИРЕ.
      /// </summary>
      async void RefreshLiraPlateAxes(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         BeginBusy(Loc.S("LiraAxesRefreshBusy"));
         try
         {
            var raw = await RunOnStaThread(Services.LiraApiSchemaReader.ReadPlateAxisAngles);
            var byTag = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (elemId, angle) in raw)
               byTag[elemId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = angle;

            // В ЛИРЕ может быть открыта другая схема: номера КЭ таблицы должны быть номерами пластин этой схемы.
            var shells = db.GetFemMeshElements(schema.Id)
               .Where(e => e.Origin == CScore.Fem.FemMember.MeshSourceImported && e.ElemType == "shell")
               .Select(e => e.ElemTag.Trim()).ToHashSet(StringComparer.Ordinal);
            int foreign = byTag.Keys.Count(tag => !shells.Contains(tag));
            if (byTag.Count == 0 || foreign > 0)
            {
               EndBusy();
               System.Windows.MessageBox.Show(
                  byTag.Count == 0 ? Loc.S("LiraAxesRefreshEmpty") : string.Format(Loc.S("LiraAxesRefreshForeign"), foreign, byTag.Count, schema.Tag),
                  Loc.S("FemSchemaRefreshLiraAxes"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
               return;
            }

            db.ReplaceFemElementLocalAxisAngles(schema.Id, byTag);
            string done = string.Format(Loc.S("LiraAxesRefreshDone"), byTag.Count, byTag.Values.Count(a => Math.Abs(a) > 1e-6));
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Error(ex.Message);
            System.Windows.MessageBox.Show(ex.Message.Split('\n')[0], Loc.S("FemSchemaRefreshLiraAxes"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// Обновить жёсткости схемы (размеры сечений стержней, толщины пластин) и номера жёсткостей у КЭ
      /// по таблицам «Жёсткости» и «Элементы - жёсткости» открытой в ЛИРЕ схемы. Нужна схемам,
      /// импортированным до того, как жёсткости стали сохраняться при схеме, и после их правки в ЛИРЕ.
      /// Группы схемы не меняются.
      /// </summary>
      async void RefreshLiraStiffnesses(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         BeginBusy(Loc.S("LiraStiffRefreshBusy"));
         try
         {
            var (stiffnesses, byElement) = await RunOnStaThread(Services.LiraApiSchemaReader.ReadStiffnesses);
            var byTag = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (elemId, num) in byElement)
               byTag[elemId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = num;

            // В ЛИРЕ может быть открыта другая схема: номера КЭ таблицы должны быть номерами КЭ этой схемы.
            var mesh = db.GetFemMeshElements(schema.Id)
               .Where(e => e.Origin == CScore.Fem.FemMember.MeshSourceImported)
               .Select(e => e.ElemTag.Trim()).ToHashSet(StringComparer.Ordinal);
            int foreign = byTag.Keys.Count(tag => !mesh.Contains(tag));
            if (foreign > 0)
            {
               EndBusy();
               System.Windows.MessageBox.Show(
                  string.Format(Loc.S("LiraStiffRefreshForeign"), foreign, byTag.Count, schema.Tag),
                  Loc.S("FemSchemaRefreshLiraStiffness"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
               return;
            }

            int updated = db.ReplaceFemSchemaStiffnesses(schema.Id, stiffnesses, byTag);
            SaveLiraSteelProfiles(schema.Id, stiffnesses);
            string done = string.Format(Loc.S("LiraStiffRefreshDone"), stiffnesses.Count,
               stiffnesses.Count(s => CScore.Import.LiraStiffnessParams.BarRect(s) != null), updated);
            LogService.Info(done);
            foreach (var s in stiffnesses.Where(s => CScore.Import.LiraStiffnessParams.IsBar(s)
                                                     && CScore.Import.LiraStiffnessParams.BarRect(s) == null))
               LogService.Warning(string.Format(Loc.S("LiraStiffShapeUnsupported"), s.Id, s.Name));
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Error(ex.Message);
            System.Windows.MessageBox.Show(ex.Message.Split('\n')[0], Loc.S("FemSchemaRefreshLiraStiffness"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// Разрешить по сортаментам установленной ЛИРЫ (*.profiles.srt) профили стальных жёсткостей (вид 1018) и сохранить
      /// их при схеме (вложение <see cref="FemSchemaSourceFileKind.LiraSteelProfiles"/>). Нет стальных жёсткостей —
      /// вложение не создаётся; профили без сортамента — предупреждение в журнал.
      /// </summary>
      void SaveLiraSteelProfiles(int schemaId, IEnumerable<CScore.Import.LiraStiffnessRecord> stiffnesses)
      {
         var entries = Services.LiraSteelProfileLoader.Resolve(stiffnesses, Services.LiraSteelProfileLoader.SortamentDirectories());
         if (entries.Count == 0) return;
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.LiraSteelProfiles, "",
            System.Text.Encoding.UTF8.GetBytes(CScore.Import.SteelProfileIndex.ToJson(entries)));
         var failed = entries.Where(e => e.Shape == null).ToList();
         LogService.Info(string.Format(Loc.S("LiraSteelProfilesLoaded"), entries.Count - failed.Count, entries.Count));
         foreach (var e in failed)
            LogService.Warning(string.Format(Loc.S("LiraSteelProfileFailed"), e.Num, e.Source, e.Reason));
      }

      /// <summary>Сохранить ЖБ-группы SCAD при схеме (вложение <see cref="FemSchemaSourceFileKind.ScadConcreteGroups"/>).</summary>
      void SaveScadConcreteGroups(int schemaId, IReadOnlyCollection<CScore.Import.ScadConcreteGroup> groups) =>
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadConcreteGroups, "",
            System.Text.Encoding.UTF8.GetBytes(CScore.Import.ScadConcreteGroupIndex.ToJson(groups)));

      /// <summary>
      /// Разрешить по сортаментам SCAD (PRF из каталога <paramref name="prfDirectory"/>) стальные профили жёсткостей
      /// STZ и сохранить их при схеме (вложение <see cref="FemSchemaSourceFileKind.ScadSteelProfiles"/>).
      /// Нет жёсткостей STZ — вложение не создаётся; профили без сортамента — предупреждение в журнал.
      /// </summary>
      void SaveScadSteelProfiles(int schemaId, IEnumerable<CScore.Import.LiraStiffnessRecord> stiffnesses, string? prfDirectory)
      {
         var entries = Services.Scad.ScadSteelProfileLoader.Resolve(stiffnesses, prfDirectory);
         if (entries.Count == 0) return;
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelProfiles, "",
            System.Text.Encoding.UTF8.GetBytes(CScore.Import.SteelProfileIndex.ToJson(entries)));
         var failed = entries.Where(e => e.Shape == null).ToList();
         LogService.Info(string.Format(Loc.S("ScadSteelProfilesLoaded"), entries.Count - failed.Count, entries.Count));
         foreach (var e in failed)
            LogService.Warning(string.Format(Loc.S("ScadSteelProfileFailed"), e.Num, e.Source, e.Reason));
      }

      /// <summary>Сохранить закрепления, жёсткие тела и нагрузки SCAD (вложение <see cref="FemSchemaSourceFileKind.ScadAnalysisModel"/>).</summary>
      void SaveScadAnalysisModel(int schemaId, CScore.Import.ScadAnalysisModel model) =>
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel, "",
            System.Text.Encoding.UTF8.GetBytes(model.ToJson()));

      /// <summary>Вложение SCAD схемы (закрепления, жёсткие тела, нагрузки); null — нет или повреждено (в журнал).</summary>
      CScore.Import.ScadAnalysisModel? LoadScadAnalysisModel(int schemaId)
      {
         if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
         try { return CScore.Import.ScadAnalysisModel.FromJson(System.Text.Encoding.UTF8.GetString(file.Data)); }
         catch (InvalidDataException ex) { LogService.Error(ex.Message); return null; }
      }

      /// <summary>
      /// Переносит нагрузки вложения SCAD в нагрузки сеточного уровня схемы (повторно — с заменой перенесённого ранее,
      /// ручное не трогается). Журнал: сводка, непереносимое, ΣF загружений для сверки с протоколом SCAD. Результаты
      /// постановок схемы сбрасываются. Открытый редактор схемы закрывает вызывающий.
      /// </summary>
      void TransferScadLoads(CScore.Fem.FemSchema schema, CScore.Import.ScadAnalysisModel model)
      {
         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var elements = db.GetFemMeshElements(schema.Id);
         var types = elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
         int next = 0;
         var result = CScore.Import.ScadLoadTransfer.Transfer(model, types, schema.LoadCases.ToList(),
            db.GetFemElementLoads(schema.Id), db.GetFemMeshNodeLoads(schema.Id), () => --next);
         db.SaveFemLoadCasesAndMeshLoads(schema.Id, result.LoadCases, result.ElementLoads, result.MeshNodeLoads);

         LogService.Info(result.Report[0]);
         foreach (var line in result.Report.Skip(1)) LogService.Warning(line);

         var mesh = new CScore.Fem.Loads.FemLoadMeshContext(meshNodes, elements, schema.MemberGroups.ToList(),
            Services.FemSelfWeightSourceFactory.Create(db, schema.Id));
         foreach (var lc in result.LoadCases.Where(c => c.Origin == CScore.Import.ScadLoadTransfer.Origin))
         {
            var forces = CScore.Fem.Loads.FemLoadCaseNodalForces.Resolve(lc, result.ElementLoads, result.MeshNodeLoads, mesh);
            var (fx, fy, fz) = forces.Total;
            LogService.Info(string.Format(Loc.S("ScadLoadsTotal"), lc.Tag, lc.SourceLoadNum, fx / 1e3, fy / 1e3, fz / 1e3));
            foreach (var d in forces.Diagnostics.Select(d => d.Message).Distinct().Take(10)) LogService.Warning($"«{lc.Tag}»: {d}");
         }

         int invalidated = InvalidateFemSchemaAnalyses(schema);
         if (invalidated > 0) LogService.Info(string.Format(Loc.S("ScadLoadsAnalysesInvalidated"), invalidated));
      }

      /// <summary>Сбрасывает результаты постановок схемы; возвращает число сброшенных.</summary>
      int InvalidateFemSchemaAnalyses(CScore.Fem.FemSchema schema)
      {
         int invalidated = 0;
         foreach (var analysis in schema.Analyses.Where(a => a.ResultId != null))
         {
            analysis.InvalidateResult();
            db.SaveFemAnalysis(analysis);
            invalidated++;
         }
         return invalidated;
      }

      /// <summary>
      /// Переносит ГУ вложения SCAD (закрепления, пружины КЭ 51, жёсткие тела, шарниры стержней, C1 пластин) в сеточный уровень
      /// схемы с заменой перенесённых раньше; ручные не трогаются. Журнал: сводка, непереносимое, проверки резолвера.
      /// Результаты постановок сбрасываются. Открытый редактор схемы закрывает вызывающий.
      /// </summary>
      /// <param name="showSummary">Открыть окно сводки переноса (импорт и «Дочитать граничные условия»).</param>
      void TransferScadBoundary(CScore.Fem.FemSchema schema, CScore.Import.ScadAnalysisModel model, bool showSummary = false)
      {
         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var elements = db.GetFemMeshElements(schema.Id);
         var types = elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
         var result = CScore.Import.ScadBoundaryTransfer.Transfer(model,
            meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal), types);
         var manual = db.ManualMeshNodeTags(schema.Id);
         int keptManual = result.Supports.Select(x => x.NodeTag)
            .Concat(result.Springs.Where(x => x.TargetKind == CScore.Fem.FemSpringTargetKinds.MeshNode).Select(x => x.NodeTag))
            .Where(manual.Contains).Distinct().Count();
         db.SaveFemBoundary(schema.Id, CScore.Import.ScadBoundaryTransfer.Origin, result.Supports, result.Springs,
            result.RigidBodies, result.ElementProps);

         LogService.Info(result.Report[0]);
         foreach (var line in result.Report.Skip(1)) LogService.Warning(line);
         if (keptManual > 0) LogService.Info(string.Format(Loc.S("FemBoundaryManualKept"), keptManual));

         var resolved = CScore.Fem.FemBoundaryResolver.Resolve(db.GetFemNodes(schema.Id), meshNodes, elements,
            db.GetFemMeshNodeSupports(schema.Id), db.GetFemSprings(schema.Id), db.GetFemRigidBodies(schema.Id));
         foreach (var d in resolved.Diagnostics.Take(10))
            if (d.IsError) LogService.Error(d.Message); else LogService.Warning(d.Message);
         if (resolved.Diagnostics.Count > 10)
            LogService.Warning(string.Format(Loc.S("ScadBoundaryMoreDiagnostics"), resolved.Diagnostics.Count - 10));

         int invalidated = InvalidateFemSchemaAnalyses(schema);
         if (invalidated > 0) LogService.Info(string.Format(Loc.S("ScadBoundaryAnalysesInvalidated"), invalidated));

         if (showSummary && System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
         {
            var messages = result.Report.Skip(1).Concat(resolved.Diagnostics.Select(d => d.Message)).ToList();
            if (keptManual > 0) messages.Insert(0, string.Format(Loc.S("FemBoundaryManualKept"), keptManual));
            new Views.FemBoundarySummaryWindow(string.Format(Loc.S("FemBoundarySummaryHeader"), schema.Tag), result.Summary, messages)
            {
               Owner = owner,
            }.Show();
         }
      }

      /// <summary>
      /// «Дочитать граничные условия»: из вложения SCAD, если оно уже с пружинами, шарнирами и основанием, иначе — повторным
      /// чтением .SPR через SCADAPIX.dll (вложение обновляется, нагрузки заново не переносятся).
      /// </summary>
      async Task RefreshScadBoundary(CScore.Fem.FemSchema schema)
      {
         if (IsBusy) return;
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return;
         if (LoadScadAnalysisModel(schema.Id) is { HasBoundaryV2: true, HasBeds: true } stored)
         {
            TransferScadBoundary(schema, stored, showSummary: true);
            return;
         }

         string? spr = ResolveScadProjectPath(schema);
         var settings = db.LoadScadApiSettings();
         string? dllDir = Services.Scad.ScadInstallLocator.ContainsDll(settings.DllDirectory)
            ? settings.DllDirectory
            : Services.Scad.ScadInstallLocator.FindDllDirectory();
         if (spr == null || dllDir == null)
         {
            LogService.Warning(string.Format(Loc.S("ScadBoundaryNoSpr"), schema.Tag));
            return;
         }

         var cts = BeginBusyWithCancellation(Loc.S("ScadBoundaryReading"));
         try
         {
            var analysis = await Task.Run(() =>
            {
               Services.Scad.ScadApiNative.Gate.Wait(cts.Token);
               try
               {
                  var native = Services.Scad.ScadApiNative.Load(dllDir);
                  using var session = new Services.Scad.ScadApiSession(native);
                  session.Open(spr);
                  return Services.Scad.ScadApiReader.Read(session, new Services.Scad.ScadReadOptions(OutputAxes: false,
                     ConcreteGroups: false, AssignedRebar: false, SteelGroups: false), null, cts.Token).Data.AnalysisModel;
               }
               finally { Services.Scad.ScadApiNative.Gate.Release(); }
            }, cts.Token);
            EndBusy();
            if (analysis == null) return;
            SaveScadAnalysisModel(schema.Id, analysis);
            TransferScadBoundary(schema, analysis, showSummary: true);
         }
         catch (OperationCanceledException) { EndBusy(); }
         catch (Services.Scad.ScadApiException ex)
         {
            EndBusy();
            LogService.Warning(ex.Format(Loc.S));
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Warning(ex.Message);
         }
      }

      /// <summary>Сохранить стальные группы SCAD при схеме (вложение <see cref="FemSchemaSourceFileKind.ScadSteelGroups"/>).</summary>
      void SaveScadSteelGroups(int schemaId, IReadOnlyCollection<CScore.Import.ScadSteelGroup> groups) =>
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelGroups, "",
            System.Text.Encoding.UTF8.GetBytes(CScore.Import.ScadSteelGroupIndex.ToJson(groups)));

      /// <summary>Сохранить заданное армирование SCAD при схеме (вложение <see cref="FemSchemaSourceFileKind.ScadAssignedRebar"/>).</summary>
      void SaveScadAssignedRebar(int schemaId, CScore.Import.ScadAssignedRebarFile file) =>
         db.SaveFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAssignedRebar, "",
            System.Text.Encoding.UTF8.GetBytes(file.ToJson()));

      /// <summary>
      /// Загрузить к схеме SCAD выгрузку плагина «Экспорт для OpenCS» (*.opencs-scad.json — подобранная арматура):
      /// разобрать, сверить номера КЭ с сеткой и сохранить при схеме (заменяет ранее загруженную). Если у схемы
      /// нет ЖБ-групп (привязки и классы арматуры) — дочитать их из .SPR через SCADAPIX.dll.
      /// </summary>
      async void LoadScadSelectedRebar(CScore.Fem.FemSchema? schema)
      {
         if (IsBusy) return;
         schema ??= currentFemSchema;
         string title = Loc.S("ScadRebarLoad");
         if (schema == null || schema.SourceType != "scad")
         {
            MessageBox.Show(schema == null ? Loc.S("ScadForcesNoTarget") : string.Format(Loc.S("ScadForcesNotScadSchema"), schema.Tag),
               title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         // Плагин пишет выгрузку рядом с .SPR: «<проект>.opencs-scad.json».
         string? initial = null;
         if (!string.IsNullOrWhiteSpace(schema.SourcePath))
         {
            string candidate = Path.Combine(Path.GetDirectoryName(schema.SourcePath) ?? "",
               Path.GetFileNameWithoutExtension(schema.SourcePath) + CScore.Import.ScadRebarExportReader.FileSuffix);
            initial = File.Exists(candidate) ? candidate : Path.GetDirectoryName(schema.SourcePath);
         }
         string? path = FileDialogService.OpenFile(Loc.S("ScadRebarFilter"),
            string.Format(Loc.S("ScadRebarOpenTitle"), schema.Tag), initial);
         if (path == null) return;

         string name = Path.GetFileName(path);
         CScore.Import.ScadSelectedRebarFile parsed;
         byte[] data;
         try
         {
            data = File.ReadAllBytes(path);
            parsed = CScore.Import.ScadRebarExportReader.Read(data);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
         {
            Rejected(string.Format(Loc.S("ScadRebarReadError"), name, ex.Message));
            return;
         }

         foreach (var w in parsed.Warnings)
            LogService.Warning(w);
         if (parsed.Plates.Count + parsed.Bars.Count == 0)
         {
            Rejected(string.Format(Loc.S("ScadRebarEmpty"), name));
            return;
         }

         var elements = db.GetFemMeshElements(schema.Id)
            .Select(e => (Ok: int.TryParse(e.ElemTag, out int id), Id: id, IsPlate: e.ElemType == "shell"))
            .Where(e => e.Ok)
            .Select(e => (e.Id, e.IsPlate));
         var match = parsed.MatchSchema(elements);
         if (match.Missing.Count > 0 || match.KindMismatch.Count > 0)
            LogService.Warning(string.Format(Loc.S("LiraAspSchemaMismatch"),
               match.Missing.Count, match.KindMismatch.Count,
               string.Join(", ", match.Missing.Concat(match.KindMismatch).Order().Take(20))));
         if (match.PlatesMatched + match.BarsMatched == 0)
         {
            Rejected(string.Format(Loc.S("ScadRebarNoMatch"), name, schema.Tag, parsed.Plates.Count, parsed.Bars.Count));
            return;
         }

         // Выгрузка не из проекта схемы или сделана до последнего сохранения проекта — предупреждение, не отказ.
         if (!string.IsNullOrWhiteSpace(schema.SourcePath) && parsed.Project.Length > 0
             && !string.Equals(Path.GetFileName(parsed.Project), Path.GetFileName(schema.SourcePath), StringComparison.OrdinalIgnoreCase))
            LogService.Warning(string.Format(Loc.S("ScadRebarOtherProject"), name, parsed.Project, schema.SourcePath));
         string? spr = !string.IsNullOrWhiteSpace(schema.SourcePath) && File.Exists(schema.SourcePath) ? schema.SourcePath
                     : File.Exists(parsed.Project) ? parsed.Project : null;
         if (spr != null)
         {
            DateTime exported = parsed.Exported?.UtcDateTime ?? File.GetLastWriteTimeUtc(path);
            if (exported < File.GetLastWriteTimeUtc(spr))
               LogService.Warning(string.Format(Loc.S("ScadRebarStale"), name, Path.GetFileName(spr)));
         }

         db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadSelectedRebar, name, data);
         string done = string.Format(Loc.S("ScadRebarLoaded"), name, match.PlatesMatched, match.BarsMatched);
         LogService.Info(done);

         if (db.GetFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.ScadConcreteGroups) == null)
            await RefreshScadRebarData(schema);

         void Rejected(string message)
         {
            LogService.Warning(message);
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
         }
      }

      /// <summary>
      /// Перечитать из .SPR через SCADAPIX.dll ЖБ-группы и заданное армирование схемы SCAD — для схем,
      /// импортированных без них, и после правки армирования в SCAD. Отказ или ошибка — в журнал: подбор
      /// работает и без групп, привязки берутся из сечения цели.
      /// </summary>
      async Task RefreshScadRebarData(CScore.Fem.FemSchema schema)
      {
         string? spr = ResolveScadProjectPath(schema);
         var settings = db.LoadScadApiSettings();
         string? dllDir = Services.Scad.ScadInstallLocator.ContainsDll(settings.DllDirectory)
            ? settings.DllDirectory
            : Services.Scad.ScadInstallLocator.FindDllDirectory();
         SaveScadSteelProfiles(schema.Id, db.GetFemSchemaStiffnesses(schema.Id).Values, dllDir);
         if (spr == null || dllDir == null)
         {
            LogService.Warning(Loc.S("ScadRebarNoGroups"));
            return;
         }

         var cts = BeginBusyWithCancellation(Loc.S("ScadRebarReadingGroups"));
         try
         {
            var (groups, assigned, steelGroups, analysis) = await Task.Run(() =>
            {
               Services.Scad.ScadApiTrace.Write("Импорт: ожидание Gate");
               Services.Scad.ScadApiNative.Gate.Wait(cts.Token);
               Services.Scad.ScadApiTrace.Write("Импорт: Gate получен");
               try
               {
                  var native = Services.Scad.ScadApiNative.Load(dllDir);
                  using var session = new Services.Scad.ScadApiSession(native);
                  session.Open(spr);
                  return (Services.Scad.ScadApiReader.ReadConcreteGroups(session),
                     Services.Scad.ScadApiReader.ReadAssignedRebar(session),
                     Services.Scad.ScadApiReader.ReadSteelGroups(session),
                     Services.Scad.ScadApiReader.Read(session, new Services.Scad.ScadReadOptions(OutputAxes: false,
                        ConcreteGroups: false, AssignedRebar: false, SteelGroups: false), null, cts.Token).Data.AnalysisModel);
               }
               finally { Services.Scad.ScadApiNative.Gate.Release(); }
            }, cts.Token);
            SaveScadConcreteGroups(schema.Id, groups);
            SaveScadAssignedRebar(schema.Id, assigned);
            SaveScadSteelGroups(schema.Id, steelGroups);
            if (analysis != null)
            {
               SaveScadAnalysisModel(schema.Id, analysis);
               bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
               if (!editorOpen || TryLeaveFemSchemaEditor())
               {
                  TransferScadLoads(schema, analysis);
                  TransferScadBoundary(schema, analysis);
               }
               else LogService.Warning(string.Format(Loc.S("ScadLoadsEditorOpen"), schema.Tag));
            }
            string done = string.Format(Loc.S("ScadRebarGroupsLoaded"), groups.Count, assigned.Plates.Count, assigned.Rods.Count,
               steelGroups.Count);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (OperationCanceledException)
         {
            EndBusy();
            LogService.Warning(Loc.S("ScadRebarNoGroups"));
         }
         catch (Services.Scad.ScadApiException ex)
         {
            EndBusy();
            LogService.Warning(ex.Format(Loc.S) + " " + Loc.S("ScadRebarNoGroups"));
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Warning(ex.Message + " " + Loc.S("ScadRebarNoGroups"));
         }
      }

      /// <summary>
      /// Скопировать плагин «Экспорт для OpenCS» (поставляется в bin\ScadPlugin\OpenCSExport) в каталог плагинов
      /// постпроцессора SCAD (%ALLUSERSPROFILE%\SCAD Soft\Plugins\PostProcessor). Прав не повышает: при отказе
      /// доступа — путь назначения и предложение открыть папку плагина для ручного копирования.
      /// </summary>
      void InstallScadPlugin()
      {
         string title = Loc.S("ScadPluginInstall");
         string source = Path.Combine(AppContext.BaseDirectory, "ScadPlugin", "OpenCSExport");
         if (!File.Exists(Path.Combine(source, "Plugin.js")))
         {
            MessageBox.Show(string.Format(Loc.S("ScadPluginNoSource"), source), title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }
         string scadSoft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SCAD Soft");
         if (!Directory.Exists(scadSoft))
         {
            MessageBox.Show(string.Format(Loc.S("ScadPluginNoScad"), scadSoft), title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         string target = Path.Combine(scadSoft, "Plugins", "PostProcessor", "OpenCSExport");
         if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()
             && MessageBox.Show(string.Format(Loc.S("ScadPluginReplace"), target), title,
                   MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

         try
         {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
               string dest = Path.Combine(target, Path.GetRelativePath(source, file));
               Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
               File.Copy(file, dest, overwrite: true);
            }
         }
         catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
         {
            LogService.Warning(ex.Message);
            if (MessageBox.Show(string.Format(Loc.S("ScadPluginAccessDenied"), target, source, ex.Message), title,
                   MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
               System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + source + "\"")
                  { UseShellExecute = true });
            return;
         }

         string done = string.Format(Loc.S("ScadPluginInstalled"), target);
         LogService.Info(done);
         MessageBox.Show(done, title, MessageBoxButton.OK, MessageBoxImage.Information);
      }

      /// <summary>
      /// Дозагрузить к схеме файл подобранной ЛИРОЙ арматуры (*.asp: Файл → Экспорт в режиме
      /// «Конструирование»): разобрать, сверить номера КЭ с сеткой схемы и сохранить файл при схеме
      /// (заменяет ранее загруженный).
      /// </summary>
      void LoadLiraAsp(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         string? path = FileDialogService.OpenFile(Loc.S("LiraAspFilter"),
            string.Format(Loc.S("LiraAspOpenTitle"), schema.Tag));
         if (path == null) return;

         string name = System.IO.Path.GetFileName(path);
         try
         {
            byte[] data = System.IO.File.ReadAllBytes(path);
            var parsed = CScore.Import.LiraAspReader.Read(data);
            foreach (var w in parsed.Warnings)
               LogService.Warning(w);
            if (parsed.Plates.Count + parsed.Bars.Count == 0)
            {
               ShowAspRejected(string.Format(Loc.S("LiraAspEmpty"), name, parsed.PunchingCount));
               return;
            }

            var elements = db.GetFemMeshElements(schema.Id)
               .Select(e => (Ok: int.TryParse(e.ElemTag, out int id), Id: id, IsPlate: e.ElemType == "shell"))
               .Where(e => e.Ok)
               .Select(e => (e.Id, e.IsPlate));
            var match = CScore.Import.LiraAspSchemaMatch.Check(parsed, elements);
            if (match.Missing.Count > 0 || match.KindMismatch.Count > 0)
               LogService.Warning(string.Format(Loc.S("LiraAspSchemaMismatch"),
                  match.Missing.Count, match.KindMismatch.Count,
                  string.Join(", ", match.Missing.Concat(match.KindMismatch).Order().Take(20))));
            if (match.PlatesMatched + match.BarsMatched == 0)
            {
               ShowAspRejected(string.Format(Loc.S("LiraAspNoMatch"), name, schema.Tag,
                  parsed.Plates.Count, parsed.Bars.Count));
               return;
            }

            db.SaveFemSchemaSelectedReinforcementFile(schema.Id, name, data);
            string done = string.Format(Loc.S("LiraAspLoaded"), name, parsed.Variant,
               match.PlatesMatched, match.BarsMatched);
            LogService.Info(done);
         }
         catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                        or System.IO.InvalidDataException)
         {
            ShowAspRejected(string.Format(Loc.S("LiraAspReadError"), name, ex.Message));
         }

         // Отказ в загрузке — в журнал и окном: иначе команда выглядит так, будто ничего не произошло.
         void ShowAspRejected(string message)
         {
            LogService.Warning(message);
            System.Windows.MessageBox.Show(message, Loc.S("FemSchemaLoadLiraAsp"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
         }
      }

      /// <summary>
      /// Создаёт сечения пластинчатых целей по данным ЛИРЫ: материалы по классам из подбора (*.asp), толщина —
      /// из подбора, арматура — фоновые ТЗА (*.RBT). Сечение получают цели без пластинчатого сечения.
      /// </summary>
      /// <param name="targets">Цели; null — все группы и пластинчатые конструктивные элементы схемы
      /// (вызов из меню схемы, итог показывается окном).</param>
      /// <returns>Хотя бы одной цели назначено сечение.</returns>
      internal bool CreateLiraPlateSections(
         CScore.Fem.FemSchema? schema, IReadOnlyList<CScore.Fem.IFemCheckable>? targets = null)
      {
         schema ??= currentFemSchema;
         if (schema == null) return false;
         bool fromMenu = targets == null;
         bool scad = schema.SourceType == "scad";
         string title = Loc.S(scad ? "ScadSectionsTitle" : "LiraSectionsTitle");

         // Открытый редактор этой схемы держит конструктивные элементы в памяти и при сохранении перезапишет их.
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return false;

         var data = Services.FemCheckSchemaData.Load(db, schema.Id);
         foreach (string error in data.Errors)
            LogService.Warning(error);
         if (targets == null)
         {
            // Автогруппы по жёсткостям и наборам ТЗА повторяют КЭ конструктивных блоков и дали бы по сечению
            // на каждый набор ТЗА — им сечение создаётся по запросу из диалога проверки.
            var blockTags = db.GetLiraBlocks(schema.Id).Select(b => b.Tag).ToHashSet(StringComparer.Ordinal);
            var groups = blockTags.Count > 0
               ? schema.MemberGroups.Where(g => blockTags.Contains(g.Tag))
               : schema.MemberGroups;
            targets = [.. groups, .. data.Members.Where(m => m.ElemType == "shell")];
         }

         var report = Services.LiraPlateSectionCreator.Create(db, data, targets, suggested =>
         {
            if (scad)
            {
               // У SCAD привязки — из ЖБ-группы; диаметр нужен для ширины раскрытия трещин.
               var input = new Views.Dialogs.TextInputDialog(Loc.S("ScadSectionsDiameterTitle"), Loc.S("ScadSectionsDiameter"),
                  (suggested.DiameterM * 1000).ToString("0.#", CultureInfo.CurrentCulture));
               if (input.ShowDialog() != true) return null;
               return double.TryParse(input.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double mm)
                  ? new CScore.Import.LiraPlateNominalRebar(0, mm / 1000)
                  : new CScore.Import.LiraPlateNominalRebar(0, 0);
            }
            var dlg = new Views.Dialogs.DoubleInputDialog(Loc.S("LiraSectionsNominalTitle"),
               Loc.S("LiraSectionsNominalCover"), Loc.S("LiraSectionsNominalDiameter"),
               suggested.CoverM * 1000, suggested.DiameterM * 1000);
            return dlg.ShowDialog() == true
               ? new CScore.Import.LiraPlateNominalRebar(dlg.Value1 / 1000, dlg.Value2 / 1000)
               : null;
         });
         if (report.Cancelled) return false;
         if (report.NoAsp)
         {
            MessageBox.Show(Loc.S(scad ? "ScadSectionsNoGroups" : "LiraSectionsNoAsp"), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
         }

         foreach (string tag in report.Materials)
            LogService.Info(string.Format(Loc.S("LiraSectionsMaterialCreated"), tag));
         foreach (string tag in report.Sections)
            LogService.Info(string.Format(Loc.S("LiraSectionsSectionCreated"), tag));
         static string Names(IEnumerable<string> tags)
         {
            var list = tags.Distinct().ToList();
            return string.Join("; ", list.Take(20)) + (list.Count > 20 ? "; …" : "");
         }
         foreach (var g in report.Assigned.GroupBy(a => a.Section))
            LogService.Info(string.Format(Loc.S("LiraSectionsAssigned"), g.Key, g.Count(), Names(g.Select(a => a.Target))));
         foreach (var g in report.Nominal.GroupBy(n => n.Faces))
            LogService.Warning(string.Format(Loc.S("LiraSectionsNominalNote"), Names(g.Select(n => n.Target)), g.Key,
               report.NominalRebar!.DiameterM * 1000, report.NominalRebar.CoverM * 1000));
         foreach (var (target, combos) in report.OtherCombos)
            LogService.Warning(string.Format(Loc.S("LiraSectionsOtherCombos"), target, combos));
         foreach (var (target, reason) in report.Skipped)
            LogService.Warning(string.Format(Loc.S("LiraSectionsSkipped"), target, reason));

         bool nothingToDo = report.Assigned.Count == 0 && report.Skipped.Count == 0;
         string done = nothingToDo
            ? Loc.S("LiraSectionsNothing")
            : string.Format(Loc.S("LiraSectionsSummary"), report.Materials.Count, report.Sections.Count,
               report.Assigned.Count, report.Skipped.Count);
         LogService.Info(done);
         StatusMessage = done;
         if (fromMenu || report.Assigned.Count == 0)
         {
            string details = string.Join("\n", report.Skipped.Take(10).Select(s => $"«{s.Target}»: {s.Reason}"));
            MessageBox.Show(details.Length > 0 ? done + "\n\n" + details : done, title, MessageBoxButton.OK,
               report.Skipped.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
         }
         if (editorOpen && report.Assigned.Count > 0) ReloadFemSchemaPage();
         return report.Assigned.Count > 0;
      }

      /// <summary>
      /// Создаёт сечения стержней импортированной схемы (ЛИРА, SCAD): параметрические ЖБ-сечения по жёсткостям «Брус» /
      /// S0 и классам материалов (подбор ЛИРЫ, ЖБ-группы SCAD), назначает их КЭ сетки без сечения.
      /// </summary>
      void CreateImportedBarSections(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         bool scad = schema.SourceType == "scad";
         string title = Loc.S("BarSectionsTitle");

         // Открытый редактор этой схемы держит КЭ в памяти и при сохранении перезапишет назначения.
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return;

         var data = Services.FemCheckSchemaData.Load(db, schema.Id);
         foreach (string error in data.Errors)
            LogService.Warning(error);

         var report = Services.ImportedBarSectionCreator.Create(db, data,
            chooseSteel: ChooseSteelForImportedBars, steelCatalog: new ProfileDB(), chooseRebar: ChooseRebarForImportedBars);
         if (report.Cancelled) return;
         if (report.NoMaterialData)
         {
            MessageBox.Show(Loc.S(scad ? "BarSectionsNoScadData" : "BarSectionsNoLiraData"), title,
               MessageBoxButton.OK, MessageBoxImage.Information);
            return;
         }

         foreach (string tag in report.Materials)
            LogService.Info(string.Format(Loc.S("LiraSectionsMaterialCreated"), tag));
         foreach (string tag in report.Sections)
            LogService.Info(string.Format(Loc.S("BarSectionsSectionCreated"), tag));
         foreach (var (section, count) in report.Assigned)
            LogService.Info(string.Format(Loc.S("BarSectionsAssigned"), section, count));
         var skippedLines = report.Skipped
            .Select(s => string.Format(Loc.S("BarSectionsSkipped"), s.Elements.Count,
               CScore.Fem.FemCheckReadiness.FormatRanges(s.Elements), s.Reason))
            .ToList();
         skippedLines.AddRange(report.WithoutRebar
            .Select(s => string.Format(Loc.S("BarSectionsWithoutRebar"), s.Elements.Count,
               CScore.Fem.FemCheckReadiness.FormatRanges(s.Elements), s.Reason)));
         foreach (string line in skippedLines.Concat(report.Warnings))
            LogService.Warning(line);

         string done = report.AssignedElements == 0 && report.Skipped.Count == 0
            ? string.Format(Loc.S("BarSectionsNothing"), report.AlreadyAssigned)
            : string.Format(Loc.S("BarSectionsSummary"), report.Materials.Count, report.Sections.Count,
               report.Reused.Count, report.AssignedElements, report.AlreadyAssigned,
               report.Skipped.Sum(s => s.Elements.Count));
         if (report.SteelSections.Count > 0)
            done += " " + string.Format(Loc.S("BarSectionsSteelSummary"), report.SteelSections.Count);
         if (report.Replaced > 0)
            done += " " + string.Format(Loc.S("BarSectionsReplaced"), report.Replaced);
         if (report.Unified.Elements > 0)
            done += " " + string.Format(Loc.S("BarSectionsUnified"), report.Unified.Elements, report.Unified.Layouts,
               report.SelectedTolerance * 100);
         LogService.Info(done);
         StatusMessage = done;

         if (report.Sections.Count > 0)
         {
            RefreshSectionLiveCollections();
            MarkDirty(SaveCategory.CrossSections);
         }
         string details = string.Join("\n", skippedLines.Concat(report.Warnings).Take(10));
         MessageBox.Show(details.Length > 0 ? done + "\n\n" + details : done, title, MessageBoxButton.OK,
            report.Skipped.Count + report.Warnings.Count + report.WithoutRebar.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
         if (editorOpen && report.AssignedElements > 0) ReloadFemSchemaPage();
      }

      /// <summary>
      /// Армирование создаваемых ЖБ-сечений стержней импорта: без арматуры, заданное или подобранное (только режимы,
      /// для которых у схемы есть данные); по умолчанию — заданное, иначе подобранное. null — отказ.
      /// </summary>
      Services.ImportedBarRebarChoice? ChooseRebarForImportedBars(IReadOnlyList<CScore.Import.ImportedBarRebarMode> modes)
      {
         var items = modes.Select(m => Loc.S(m switch
         {
            CScore.Import.ImportedBarRebarMode.Assigned => "BarSectionsRebarAssigned",
            CScore.Import.ImportedBarRebarMode.Selected => "BarSectionsRebarSelected",
            _ => "BarSectionsRebarNone",
         })).ToList();
         int assigned = modes.ToList().IndexOf(CScore.Import.ImportedBarRebarMode.Assigned);
         int selected = modes.ToList().IndexOf(CScore.Import.ImportedBarRebarMode.Selected);
         var dialog = new Views.SteelMaterialChoiceDialog(Loc.S("BarSectionsRebarPrompt"), items,
            assigned >= 0 ? assigned : modes.Count - 1, Loc.S("BarSectionsRebarTitle"),
            selected >= 0 ? (Loc.S("BarSectionsRebarTolerance"), selectedRebarTolerancePercent, selected, 0, 100) : null);
         if (dialog.ShowDialog() != true || dialog.SelectedIndex < 0) return null;
         if (dialog.Number is double percent) selectedRebarTolerancePercent = percent;
         return new Services.ImportedBarRebarChoice(modes[dialog.SelectedIndex], selectedRebarTolerancePercent / 100);
      }

      /// <summary>Допуск унификации подбора, % — запоминается до конца сеанса.</summary>
      double selectedRebarTolerancePercent = CScore.Import.ImportedBarRebar.DefaultSelectedTolerance * 100;

      /// <summary>
      /// Сталь для стальных КЭ импорта вне стальных групп SCAD (или с маркой, которой нет в справочнике): стальной материал проекта
      /// либо новая С245 по СП 16.13330.2017; null — отказ.
      /// </summary>
      Material? ChooseSteelForImportedBars()
      {
         var steels = db.Materials.Where(m => m.Type == MatType.Steel).ToList();
         var items = steels.Select(m => m.Tag).Append(Loc.S("SteelChoiceCreateC245")).ToList();
         var dialog = new Views.SteelMaterialChoiceDialog(Loc.S("SteelChoiceDlgPrompt"), items, 0);
         if (dialog.ShowDialog() != true) return null;
         if (dialog.SelectedIndex < steels.Count) return steels[dialog.SelectedIndex];
         var created = Services.MaterialCatalog.CreateStructuralSteel("С245");
         if (created == null) LogService.Warning(Loc.S("SteelChoiceNotInCatalog"));
         return created;
      }
      /// <summary>
      /// Преобразует выбранные в диалоге кБ ЛИРЫ в конструктивные элементы схемы (стержни — по прямым цепочкам,
      /// пластины — плоскими элементами с контуром). Сетка ЛИРЫ не меняется, элементы получают замок сетки.
      /// КЭ, уже принадлежащие элементу, не входящему в выбранные кБ, пропускаются с предупреждением.
      /// </summary>
      void ConvertLiraBlocksToMembers(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         var blocks = db.GetLiraBlocks(schema.Id);
         if (blocks.Count == 0)
         {
            MessageBox.Show(Loc.S("LiraBlocksNone"), Loc.S("LiraBlocksDialogTitle"),
               MessageBoxButton.OK, MessageBoxImage.Information);
            return;
         }
         // Открытый редактор этой схемы держит конструктивный слой в памяти и при сохранении перезапишет его.
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return;

         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var meshElements = db.GetFemMeshElements(schema.Id);
         var members = db.GetFemMembers(schema.Id);
         var vm = new ViewModels.LiraBlocksToMembersVM(blocks, meshElements, members);
         if (new Views.LiraBlocksToMembersDialog(vm).ShowDialog() != true) return;
         var selected = vm.SelectedBlocks;

         var replacedTags = members
            .Where(m => selected.Any(b => CScore.Import.MeshMemberBuilder.IsPartTag(m.ElemTag, b.Tag)))
            .Select(m => m.ElemTag).ToHashSet(StringComparer.Ordinal);
         var ownerByElement = meshElements
            .Where(e => e.SourceMemberTag != null && !replacedTags.Contains(e.SourceMemberTag))
            .ToDictionary(e => e.ElemTag, e => e.SourceMemberTag!, StringComparer.Ordinal);
         var claimed = new HashSet<string>(StringComparer.Ordinal);
         var builds = new List<CScore.Import.MeshMemberBuild>();
         var converted = new List<CScore.Import.LiraBlockInfo>();
         foreach (var block in selected)
         {
            var free = new List<string>();
            var busy = new List<string>();
            foreach (var tag in block.ElementTags)
               (ownerByElement.ContainsKey(tag) || !claimed.Add(tag) ? busy : free).Add(tag);
            if (busy.Count > 0)
               LogService.Warning(string.Format(Loc.S("LiraBlocksElementsBusy"), block.Tag, busy.Count,
                  string.Join(", ", busy.Take(20))));
            CScore.Import.MeshMemberBuild build;
            try
            {
               build = CScore.Import.LiraBlockMemberBuilder.Build(block with { ElementTags = free }, meshNodes, meshElements);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
               // Непредвиденная геометрия одного кБ не должна срывать остальные; его прежние элементы не трогаем.
               LogService.Error(string.Format(Loc.S("LiraBlocksBuildFailed"), block.Tag, ex.Message));
               continue;
            }
            foreach (var d in build.Diagnostics)
               LogService.Warning(d.Message);
            builds.Add(build);
            converted.Add(block);
         }
         if (converted.Count == 0) return;
         selected = converted;

         try
         {
            db.ApplyLiraBlockMembers(schema.Id, selected, builds);
         }
         catch (InvalidOperationException ex)
         {
            MessageBox.Show(ex.Message, Loc.S("LiraBlocksDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         var parts = builds.SelectMany(b => b.Parts).ToList();
         string done = string.Format(Loc.S("LiraBlocksConverted"), selected.Count,
            parts.Count(p => p.Member.ElemType == "beam"), parts.Count(p => p.Member.ElemType == "shell"),
            builds.Sum(b => b.Diagnostics.Count));
         LogService.Info(done);
         StatusMessage = done;
         if (editorOpen) ReloadFemSchemaPage();
         else RefreshFemSchemaTreeCounts(schema);
      }

      /// <summary>«Создать КонЭ из группы…» в дереве: КЭ — состав группы КЭ, имя и тип — от группы.</summary>
      void CreateFemMembersFromMeshGroup(CScore.Fem.FemMemberGroup? group)
      {
         if (group is not { IsMeshGroup: true }) return;
         var schema = FemSchemas.FirstOrDefault(s => s.Id == group.SchemaId);
         if (schema == null) return;
         CreateFemMembersFromMeshElements(schema, group.Tags, group.Tag, group.MemberType, makeGroup: true);
      }

      /// <summary>
      /// Конструктивные элементы из КЭ сетки (таблицы сетки, выбор КЭ в 3D, группа КЭ): прямые цепочки стержней и
      /// плоские части пластин. Выбор, не складывающийся в один элемент, разбивается на части «Имя · 1», «Имя · 2»…
      /// с сообщением. КЭ, уже принадлежащие элементу, пропускаются с предупреждением. Элементы получают замок
      /// сетки; по флажку диалога из них собирается группа КонЭ. True — элементы созданы.
      /// </summary>
      /// <param name="groupType">Тип группы КонЭ по умолчанию; для пластин плита/стена задаёт и вид плоских частей.</param>
      public bool CreateFemMembersFromMeshElements(CScore.Fem.FemSchema schema, IReadOnlyList<string> elementTags,
         string? defaultTag = null, string? groupType = null, bool makeGroup = false)
      {
         if (elementTags.Count == 0) return false;
         var meshElements = db.GetFemMeshElements(schema.Id);
         var elementByTag = new Dictionary<string, CScore.Fem.FemElement>(StringComparer.Ordinal);
         foreach (var e in meshElements) elementByTag.TryAdd(e.ElemTag, e);
         var tags = elementTags.Distinct(StringComparer.Ordinal).ToList();
         var busy = tags.Where(t => elementByTag.TryGetValue(t, out var e) && e.SourceMemberTag != null).ToList();
         var free = tags.Except(busy, StringComparer.Ordinal).ToList();
         if (free.Count == 0)
         {
            MessageBox.Show(string.Format(Loc.S("FemMeshMembersAllBusy"), tags.Count), Loc.S("FemMeshMembersDlgTitle"),
               MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
         }

         var members = db.GetFemMembers(schema.Id);
         bool TagTaken(string tag) => members.Any(m => CScore.Import.MeshMemberBuilder.IsPartTag(m.ElemTag, tag));
         string tag = defaultTag ?? "";
         if (tag.Length == 0 || TagTaken(tag))
         {
            string stem = defaultTag is { Length: > 0 } ? defaultTag : Loc.S("FemMeshMembersDefaultTag");
            int n = 1;
            while (TagTaken(tag = $"{stem} {n}")) n++;
         }
         bool hasShells = free.Any(t => elementByTag.TryGetValue(t, out var e) && e.ElemType == "shell");
         string? planarKind = groupType is CScore.Fem.FemMemberTypes.Plate or CScore.Fem.FemMemberTypes.Wall ? groupType : null;
         var dialog = new Views.MeshMembersDialog(free.Count, hasShells, tag, planarKind, makeGroup, groupType);
         if (dialog.ShowDialog() != true) return false;
         tag = dialog.MemberTag;

         // Открытый редактор этой схемы держит конструктивный слой в памяти и при сохранении перезапишет его.
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return false;
         members = db.GetFemMembers(schema.Id);
         if (TagTaken(tag))
         {
            MessageBox.Show(string.Format(Loc.S("FemMeshMembersNameTaken"), tag), Loc.S("FemMeshMembersDlgTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            if (editorOpen) ReloadFemSchemaPage();
            return false;
         }
         if (busy.Count > 0)
            LogService.Warning(string.Format(Loc.S("LiraBlocksElementsBusy"), tag, busy.Count, string.Join(", ", busy.Take(20))));

         CScore.Import.MeshMemberBuild build;
         try
         {
            build = CScore.Import.MeshMemberBuilder.Build(new CScore.Import.MeshMemberRequest(tag, dialog.PlanarKind, free),
               db.GetFemMeshNodes(schema.Id), meshElements);
            db.ApplyMeshMembers(schema.Id, [], [build]);
         }
         catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
         {
            MessageBox.Show(ex.Message, Loc.S("FemMeshMembersDlgTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            if (editorOpen) ReloadFemSchemaPage();
            return false;
         }
         foreach (var d in build.Diagnostics)
            LogService.Warning(d.Message);

         var parts = build.Parts;
         if (parts.Count > 0 && dialog.MakeGroup)
            FemGroups.CreateMembersGroup(schema, parts.Select(p => p.Member.ElemTag), tag, dialog.GroupType);
         string done = string.Format(Loc.S("FemMeshMembersCreated"), tag, free.Count, parts.Count,
            parts.Count(p => p.Member.ElemType == "beam"), parts.Count(p => p.Member.ElemType == "shell"), build.Diagnostics.Count);
         LogService.Info(done);
         StatusMessage = done;
         if (editorOpen) ReloadFemSchemaPage();
         else RefreshFemSchemaTreeCounts(schema);

         if (parts.Count == 0)
            MessageBox.Show(Loc.S("FemMeshMembersNoneBuilt"), Loc.S("FemMeshMembersDlgTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
         else if (parts.Count > 1)
            MessageBox.Show(string.Format(Loc.S("FemMeshMembersSplit"), parts.Count, tag), Loc.S("FemMeshMembersDlgTitle"),
               MessageBoxButton.OK, MessageBoxImage.Information);
         return parts.Count > 0;
      }

      async void ImportLiraForcesFromApi(CScore.Fem.IFemCheckable? target = null)
      {
         // Цель — узел, по которому вызвано контекстное меню; из главного меню — текущий выбор.
         CScore.Fem.IFemCheckable? member = target ?? currentFemMember;
         if (member == null)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoMember"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = FemTargetElementNumbers(member);

         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoElements"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == FemTargetSchemaId(member));
         if (schema == null)
         {
            LogService.Warning(Loc.S("ImportLiraForcesNoSchema"));
            return;
         }
         BeginBusy(string.Format(Loc.S("ImportLiraForcesStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadLoadCaseForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member);
            string done = string.Format(Loc.S("ImportLiraSuccess"), forceSets.Count, member.Tag);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            System.Windows.MessageBox.Show(ex.Message,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      async void ImportLiraRsnFromApi(CScore.Fem.IFemCheckable? target = null)
      {
         // Цель — узел, по которому вызвано контекстное меню; из главного меню — текущий выбор.
         CScore.Fem.IFemCheckable? member = target ?? currentFemMember;
         if (member == null)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoMember"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = FemTargetElementNumbers(member);

         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoElements"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == FemTargetSchemaId(member));
         if (schema == null)
         {
            LogService.Warning(Loc.S("ImportLiraForcesNoSchema"));
            return;
         }
         BeginBusy(string.Format(Loc.S("ImportLiraRsnStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadLoadCombinationForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member);
            string done = string.Format(Loc.S("ImportLiraSuccess"), forceSets.Count, member.Tag);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            System.Windows.MessageBox.Show(ex.Message,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      async void ImportLiraRsuFromApi(CScore.Fem.IFemCheckable? target = null)
      {
         // Цель — узел, по которому вызвано контекстное меню; из главного меню — текущий выбор.
         CScore.Fem.IFemCheckable? member = target ?? currentFemMember;
         if (member == null)
         {
            System.Windows.MessageBox.Show(Loc.S("ImportLiraForcesNoMember"), Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = FemTargetElementNumbers(member);
         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(Loc.S("ImportLiraForcesNoElements"), Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == FemTargetSchemaId(member));
         if (schema == null) { LogService.Warning(Loc.S("ImportLiraForcesNoSchema")); return; }
         BeginBusy(string.Format(Loc.S("ImportLiraRsuStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadDesignCombinationForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member);
            string done = string.Format(Loc.S("ImportLiraSuccess"), forceSets.Count, member.Tag);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            System.Windows.MessageBox.Show(ex.Message, Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
         }
      }

      /// <summary>Команда импорта усилий ЛИРЫ через API по виду: "lc" — загружения, "rsn" — РСН,
      /// иначе — РСУ. Параметр команды — цель (группа или конструктивный элемент).</summary>
      public ICommand ImportLiraForcesCommand(string? kind) => kind switch
      {
         "lc"  => ImportLiraForcesFromApiCommand,
         "rsn" => ImportLiraRsnFromApiCommand,
         _     => ImportLiraRsuFromApiCommand,
      };

      /// <summary>Номера КЭ внешней схемы, по которым запрашиваются усилия цели: КЭ сетки группы (у группы КЭ —
      /// её номера, у группы КонЭ — КЭ её элементов) или КЭ, привязанные к конструктивному элементу.</summary>
      int[] FemTargetElementNumbers(CScore.Fem.IFemCheckable target) => target switch
      {
         CScore.Fem.FemMemberGroup group => [.. db.GetFemCheckScope(group).ElementNumbers],
         CScore.Fem.FemMember element    => [.. db.GetFemCheckScope(element).ElementNumbers],
         _ => [],
      };

      static int FemTargetSchemaId(CScore.Fem.IFemCheckable target) => target switch
      {
         CScore.Fem.FemMemberGroup group => group.SchemaId,
         CScore.Fem.FemMember element    => element.SchemaId,
         _ => 0,
      };

      void SaveImportedForceSets(IReadOnlyList<CScore.ForceSet> forceSets, CScore.Fem.IFemCheckable? target)
      {
         foreach (var fs in forceSets)
         {
            // Группа и конструктивный элемент — разные таблицы: id могут совпадать, поэтому колонки разные.
            if (target is CScore.Fem.FemMember element)
            {
               fs.SourceElementId  = element.Id;
               fs.SourceElementTag = element.ElemTag;
            }
            else if (target is CScore.Fem.FemMemberGroup group)
               fs.SourceMemberId = group.Id;
            db.SaveForceSet(fs);
            if (!ForceSets.Contains(fs))
               ForceSets.Add(fs);
         }
      }

      CancellationTokenSource? _busyCts;

      public void BeginBusy(string message, bool indeterminate = true)
      {
         StatusMessage = message;
         IsBusyProgressIndeterminate = indeterminate;
         BusyProgress = 0;
         IsBusy = true;
         System.Windows.Input.CommandManager.InvalidateRequerySuggested();
      }

      public CancellationTokenSource BeginBusyWithCancellation(string message, bool indeterminate = true)
      {
         _busyCts?.Dispose();
         _busyCts = new CancellationTokenSource();
         BeginBusy(message, indeterminate);
         OnPropertyChanged(nameof(CanCancelBusy));
         return _busyCts;
      }

      public void CancelBusy() => _busyCts?.Cancel();

      public void ReportBusyProgress(double fraction, string? message = null)
      {
         if (IsBusyProgressIndeterminate)
            IsBusyProgressIndeterminate = false;
         BusyProgress = Math.Clamp(fraction, 0, 1);
         if (message != null)
            StatusMessage = message;
      }

      public void EndBusy(string? message = null)
      {
         _busyCts?.Dispose();
         _busyCts = null;
         IsBusy = false;
         IsBusyProgressIndeterminate = true;
         BusyProgress = 0;
         StatusMessage = message ?? "";
         System.Windows.Input.CommandManager.InvalidateRequerySuggested();
      }

      // COM-объекты ЛИРЫ требуют STA; ThreadPool-потоки — MTA, поэтому запускаем в отдельном STA-потоке.
      static Task<T> RunOnStaThread<T>(Func<T> func)
      {
         var tcs = new System.Threading.Tasks.TaskCompletionSource<T>();
         var thread = new System.Threading.Thread(() =>
         {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
         });
         thread.SetApartmentState(System.Threading.ApartmentState.STA);
         thread.IsBackground = true;
         thread.Start();
         return tcs.Task;
      }

      void NewFemMember(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         FemGroups.CreateEmptyGroup(schema, "Группа");
      }

      void NewFemMemberDialog(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         var dlg = new Views.FemMemberDialog();
         if (dlg.ShowDialog() != true) return;
         var ids = Views.LiraElemRangeDialog.ParseRange(dlg.Range);
         CreateFemMemberFromRange(schema, ids, dlg.MemberTag, dlg.MemberType);
      }

      /// <summary>Открывает/переключает 3D-вид указанной схемы и включает режим создания плоского
      /// конструктивного элемента кликами по узлам — точка входа из контекстного меню узла
      /// «Пластины» дерева (см. FemShellsSubNode). Тумблеры тулбара 3D-вида читают/пишут то же
      /// самое свойство FemSchemaEditorVM.CreateXxxMode, поэтому остаются синхронизированы.</summary>
      void StartPlanarRegionCreateMode(CScore.Fem.FemSchema? schema, string mode)
      {
         if (schema == null) return;
         CurrentFemSchema = schema;
         if (activeFemSchemaEditor == null) return;

         switch (mode)
         {
            case "plate": activeFemSchemaEditor.CreatePlateMode = true; break;
            case "wall": activeFemSchemaEditor.CreateWallMode = true; break;
            case "spatial": activeFemSchemaEditor.CreateSpatialPlateMode = true; break;
         }
      }

      void NewFemMembersGroup(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         FemGroups.CreateEmptyGroup(schema, "Группа", CScore.Fem.FemMemberGroup.KindMembers);
      }

      void RenameFemMemberGroup(CScore.Fem.FemMemberGroup? group)
      {
         group ??= currentFemMember;
         if (group == null) return;
         var dlg = new Views.Dialogs.TextInputDialog(
            Loc.S("FemGroupRenameTitle"), Loc.S("FemGroupRenameLabel"), group.Tag);
         if (dlg.ShowDialog() != true) return;
         FemGroups.Rename(group, dlg.Value);
      }

      /// <param name="group">Группа из меню дерева; без параметра — открытая группа.</param>
      void DeleteFemMember(CScore.Fem.FemMemberGroup? group)
      {
         group ??= currentFemMember;
         if (group == null) return;
         string message = group.Checks.Count > 0
            ? string.Format(Loc.S("FemGroupDeleteConfirmChecks"), group.Tag, group.Checks.Count)
            : string.Format(Loc.S("FemGroupDeleteConfirm"), group.Tag);
         if (MessageBox.Show(message, Loc.S("FemGroupDeleteTitle"), MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
         db.DeleteFemMemberGroup(group);
         if (group != currentFemMember) return;
         currentFemMember = null;
         CurrentPage = null!;
      }

      /// <summary>Создание групп и правка их состава (единственный путь из интерфейса).</summary>
      public Services.FemGroupService FemGroups => femGroups ??= new Services.FemGroupService(db, LogService);
      Services.FemGroupService? femGroups;

      /// <summary>Тип группы КонЭ по составу: все плиты / все стены / пластины / стержни.</summary>
      public static string FemMembersGroupType(IReadOnlyCollection<CScore.Fem.FemMember> members) =>
         members.Count > 0 && members.All(e => e.ElemType == "shell")
            ? members.Select(e => e.Kind).Distinct().ToList() switch
            {
               ["plate"] => CScore.Fem.FemMemberTypes.Plate,
               ["wall"]  => CScore.Fem.FemMemberTypes.Wall,
               _         => CScore.Fem.FemMemberTypes.Shell,
            }
            : CScore.Fem.FemMemberTypes.Beam;

      /// <summary>Создаёт группу КЭ из номеров КЭ сетки (строка диапазонов уже разобрана).</summary>
      public void CreateFemMemberFromRange(
         CScore.Fem.FemSchema schema,
         IList<int>           elemIds,
         string               tag,
         string?              memberType)
      {
         var tags = elemIds.Select(id => id.ToString(CultureInfo.InvariantCulture));
         if (FemGroups.CreateMeshGroup(schema, tags, tag, memberType) == null)
            MessageBox.Show(Loc.S("FemGroupMeshNoneAccepted"), Loc.S("FemGroupCreateTitle"),
               MessageBoxButton.OK, MessageBoxImage.Information);
      }

      /// <summary>Авто-группирует стержни схемы по SectionTag в группы КонЭ. Пропускает уже существующие имена.</summary>
      public void AutoGroupFemMembersBySection(CScore.Fem.FemSchema schema)
      {
         var members = db.GetFemMembers(schema.Id)
            .Where(e => e.ElemType == "beam")
            .ToList();
         if (members.Count == 0) return;

         var existingTags = schema.MemberGroups.Select(g => g.Tag).ToHashSet();
         int added = 0;
         foreach (var grp in members.GroupBy(e => e.SectionTag ?? "").OrderBy(g => g.Key))
         {
            if (existingTags.Contains(grp.Key)) continue;
            if (FemGroups.CreateMembersGroup(schema, grp.Select(e => e.ElemTag), grp.Key,
                   CScore.Fem.FemMemberTypes.Beam, CScore.Fem.FemMemberGroup.OriginAuto) == null) continue;
            existingTags.Add(grp.Key);
            added++;
         }
         if (added > 0)
            LogService.Info(string.Format(Loc.S("FemGroupAutoResult"), added));
      }

      /// <param name="member">Группа или КонЭ, из меню которых вызвана команда: становится целью проверки.</param>
      public void AddFemCheck(CScore.Fem.IFemCheckable? member)
      {
         var dlg = new Views.FemCheckDialog(this, target: member);
         if (dlg.ShowDialog() != true || dlg.ResultCheck == null) return;
         var check = dlg.ResultCheck;
         db.SaveFemCheck(check);
      }

      /// <param name="group">Группа или КонЭ, из меню которых вызвана команда: становится целью проверки.</param>
      public void AddSlsFemCheck(CScore.Fem.IFemCheckable? group = null)
      {
         var dlg = new Views.FemSlsCheckDialog(this, target: group);
         if (group != null && !dlg.HasTarget(group))
         {
            dlg.Close();
            System.Windows.MessageBox.Show(string.Format(Loc.S("FemCheckSlsNoShells"), group.Tag),
               Loc.S("FemSlsDlgTitle"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
         }
         if (dlg.ShowDialog() != true || dlg.ResultCheck == null) return;
         db.SaveFemCheck(dlg.ResultCheck);
      }

      /// <summary>Открывает окно эпюр вдоль стержней цели: импортированные усилия и подобранная арматура.</summary>
      /// <param name="target">Группа или конструктивный элемент.</param>
      public void ShowBarDiagrams(CScore.Fem.IFemCheckable? target)
      {
         if (target == null) return;
         var vm = ViewModels.FemBarDiagramVM.Load(db, target, LogService.Warning);
         if (vm == null || vm.NoData)
         {
            System.Windows.MessageBox.Show(
               string.Format(Loc.S(vm == null ? "FemBarDiagramNoBars" : "FemBarDiagramNoData"), target.Tag),
               Loc.S("FemBarDiagramMenu"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
         }
         new Views.FemBarDiagramDialog(vm) { Owner = System.Windows.Application.Current.MainWindow }.Show();
      }

      void EditFemCheck(CScore.Fem.FemCheck? check)
      {
         check ??= currentFemCheck;
         if (check == null) return;

         bool isSls = check.NormCode == "rc_plate_check"
                      && CScore.Fem.PlateCheckParams.Parse(check.ParamsJson).CheckGroup == "sls";

         if (isSls)
         {
             var dlg = new Views.FemSlsCheckDialog(this, check);
             if (dlg.ShowDialog() != true) return;
         }
         else
         {
             var dlg = new Views.FemCheckDialog(this, check);
             if (dlg.ShowDialog() != true) return;
         }
         db.SaveFemCheck(check);
      }

      /// <summary>Возвращает конструктивные элементы схемы синхронно (для диалогов выбора цели проверки).</summary>
      public List<CScore.Fem.FemMember> GetFemMembers(CScore.Fem.FemSchema schema) => db.GetFemMembers(schema.Id);

      /// <summary>
      /// Выполняет нормативную проверку цели. Если в строках усилий есть номера КЭ — проверка идёт по КЭ
      /// (каждая строка с сечением и армированием своего КЭ) в фоне, с прогрессом и отменой; иначе —
      /// по-старому, с одним сечением цели.
      /// </summary>
      async Task RunFemCheck(CScore.Fem.FemCheck? check)
      {
         check ??= currentFemCheck;
         if (check == null || IsBusy) return;

         var data = Services.FemCheckSchemaData.Load(db, check.SchemaId);
         CScore.Fem.IFemCheckable? target = check.TargetsElement
            ? data.Members.FirstOrDefault(e => e.Id == check.ElementId)
            : FemSchemas.SelectMany(s => s.MemberGroups).FirstOrDefault(m => m.Id == check.MemberId);
         if (target == null) { LogService.Warning($"FemCheck #{check.Id}: конструктивный элемент не найден"); return; }
         foreach (string error in data.Errors) LogService.Warning(error);

         bool isPlate = Services.FemCheckContext.IsPlate(check);
         var scope = data.Scope(target);

         // Наборы цели: привязанные к ней и наборы схемы со строками по её КЭ; явный выбор — по id.
         var candidates = Services.FemCheckContext.TargetForceSets(ForceSets, target, check.SchemaId, scope, isPlate)
            .Select(t => t.Set).ToList();
         List<CScore.ForceSet> selected = candidates;
         if (!check.IsAllSets)
         {
            var ids = check.GetForceSetIds().ToHashSet();
            selected = ForceSets.Where(f => ids.Contains(f.Id)).ToList();
         }
         var lookup = candidates.Union(selected).ToList();
         var inputs = Services.FemCheckContext.BuildInputs(this, check, target, data, scope, lookup);
         if (isPlate)
         {
            var schemaGroups = FemSchemas.FirstOrDefault(s => s.Id == check.SchemaId)?.MemberGroups;
            Services.FemCheckContext.TargetPlateSectionId(target, scope, schemaGroups ?? [], out string? inheritedFrom);
            if (inheritedFrom != null && inputs.PlateTemplate != null)
               LogService.Info(string.Format(Loc.S("FemCheckPlateSectionInherited"), check.DisplayTag,
                  inputs.PlateTemplate.Tag, inheritedFrom));
         }

         CalcResult result;
         bool saved = false;   // результат по КЭ уже в БД (строки записаны по ходу расчёта)
         if (!CScore.Fem.FemCheckRunner.HasElementNumbers(check, selected)
             || CScore.Fem.FemCheckRunner.ScopeElements(check, scope).Count == 0)
         {
            result = CScore.Fem.FemCheckRunner.RunMulti(
               check, target, inputs.TargetBarSection, inputs.PlateTemplate, check.IsAllSets ? candidates : lookup,
               (task, sect, item) => TaskRunner.Run(task, sect, item),
               inputs.ConcreteMat, inputs.RebarMat);
         }
         else
         {
            var readiness = CScore.Fem.FemCheckRunner.EvaluateReadiness(check, scope, selected, inputs);
            if (readiness.CanRun && !readiness.IsComplete)
            {
               string message = Loc.S("FemCheckIncompleteTitle") + "\n\n"
                  + Services.FemCheckContext.ReadinessDetails(readiness, isPlate) + "\n\n"
                  + Loc.S("FemCheckIncompleteQuestion");
               if (System.Windows.MessageBox.Show(message, check.DisplayTag, System.Windows.MessageBoxButton.YesNo,
                      System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                  return;
            }

            var cts = BeginBusyWithCancellation(string.Format(Loc.S("FemCheckRunning"), check.DisplayTag), indeterminate: false);
            var progress = new Progress<double>(f => { if (IsBusy) ReportBusyProgress(f); });
            var token = cts.Token;
            int checkId = check.Id;
            try
            {
               // Строки РСУ читаются и строки результата пишутся порциями: в памяти — только порция.
               result = await Task.Run(() =>
               {
                  using var stream = db.BeginFemCheckResult();
                  var r = CScore.Fem.FemCheckRunner.RunPerElement(
                     check, target, scope, selected, inputs,
                     (task, sect, item) => TaskRunner.Run(task, sect, item), progress, token, stream);
                  stream.Complete(r, checkId);
                  return r;
               });
               saved = true;
               EndBusy();
            }
            catch (OperationCanceledException)
            {
               // Отменённая проверка результат не сохраняет.
               EndBusy(Loc.S("CalcTaskCancelled"));
               return;
            }
            catch (Exception ex)
            {
               EndBusy();
               LogService.Error($"FemCheck «{check.DisplayTag}»: {ex.Message}");
               return;
            }
         }

         if (!saved)
            db.SaveCalcResultRaw(result, check.Id);
         check.ResultId = result.Id;
         db.SaveFemCheck(check);
         // Строки наборов проверке больше не нужны (на РСУ SCAD — сотни мегабайт): при следующем
         // обращении они прочитаются из БД.
         foreach (var fs in lookup) fs.UnloadRows();

         CurrentFemCheck = check;
         CurrentPage = new Views.FemCheckResultView(result, db);
         string? checkError = null;
         try
         {
            using var resDoc = JsonDocument.Parse(result.DataJson);
            if (resDoc.RootElement.TryGetProperty("error", out var errEl)) checkError = errEl.GetString();
         }
         catch (JsonException) { }
         if (checkError != null)
            LogService.Warning($"FemCheck «{check.DisplayTag}»: {checkError}");
         else
            LogService.Info($"FemCheck «{check.DisplayTag}»: {result.Status}");
      }

      void CreateFemAnalysis(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;
         var dlg = new Views.FemAnalysisDialog(schema, db.GetFemNodes(schema.Id))
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true) return;
         var analysis = dlg.Result;
         analysis.SchemaId = schema.Id;
         db.SaveFemAnalysis(analysis);   // добавит в schema.Analyses
      }

      void EditFemAnalysis(CScore.Fem.FemAnalysis? analysis)
      {
         if (analysis == null) return;
         var schema = FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId);
         if (schema == null) return;

         var dlg = new Views.FemAnalysisDialog(schema, db.GetFemNodes(schema.Id), analysis)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true) return;
         bool changed = analysis.Tag != dlg.Result.Tag ||
            analysis.Kind != dlg.Result.Kind ||
            analysis.LoadExpressionJson != dlg.Result.LoadExpressionJson ||
            analysis.ParamsJson != dlg.Result.ParamsJson;
         analysis.Tag = dlg.Result.Tag;
         analysis.Kind = dlg.Result.Kind;
         analysis.LoadExpressionJson = dlg.Result.LoadExpressionJson;
         analysis.ParamsJson = dlg.Result.ParamsJson;
         if (changed)
            analysis.InvalidateResult();
         db.SaveFemAnalysis(analysis);
         CommandManager.InvalidateRequerySuggested();
      }

      void ViewFemAnalysisResult(CScore.Fem.FemAnalysis? analysis)
      {
         if (analysis?.ResultId is not int resultId || resultId <= 0) return;
         var schema = FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId);
         if (schema == null) return;
         var result = db.GetCalcResultById(resultId);
         if (result == null) return;

         var vm = new ViewModels.FemAnalysisResultVM(result, db, schema);
         AttachFemResultVmEvents(vm, schema);
         CurrentPage = new Views.FemAnalysisResultView(vm);
      }

      async Task RunFemAnalysis(CScore.Fem.FemAnalysis? analysis)
      {
         if (analysis == null || IsBusy) return;
         var schema = FemSchemas.FirstOrDefault(s => s.Id == analysis.SchemaId);
         if (schema == null) return;

         var cts = BeginBusyWithCancellation(
            string.Format(Loc.S("FemAnalysisRunning"), analysis.Tag), indeterminate: true);
         analysis.Status = "running";
         db.SaveFemAnalysis(analysis);
         try
         {
            var result = await Tasks.FemAnalysisExecutor.RunAsync(this, schema, analysis, cts.Token);
            db.SaveCalcResult(result);
            analysis.ResultId = result.Id;
            analysis.Status   = result.Status;
            db.SaveFemAnalysis(analysis);

            var vm = new ViewModels.FemAnalysisResultVM(result, db, schema);
            AttachFemResultVmEvents(vm, schema);
            
            var statusKey = result.Status switch
            {
                "ok" => "CalcResultOk",
                "not_converged" => "CalcResultNotConverged",
                "partial" => "CalcResultPartial",
                "not_passed" => "CalcResultNotPassed",
                _ => "CalcResultError"
            };
            string done = string.Format(Loc.S(statusKey), analysis.Tag);
            if (result.Status == "ok")
                LogService.Info(done);
            else if (result.Status == "not_converged")
                LogService.Warning(done);
            else
                LogService.Error(done);

            if (vm.Diagnostics.Count > 0)
                foreach (var diag in vm.Diagnostics)
                    LogService.Warning($"[{analysis.Tag}] {diag}");
            
            if (vm.HasArtifacts)
                LogService.Info($"[{analysis.Tag}] {Loc.S("FemResultArtifacts")} {vm.ArtifactDirectory}");

            CurrentPage = new Views.FemAnalysisResultView(vm);
            EndBusy(string.Format(Loc.S("FemAnalysisDone"), analysis.Tag));
         }
         catch (OperationCanceledException)
         {
            analysis.Status = "cancelled";
            db.SaveFemAnalysis(analysis);
            EndBusy(Loc.S("CalcTaskCancelled"));
         }
         catch (Exception ex)
         {
            analysis.Status = "error";
            db.SaveFemAnalysis(analysis);
            EndBusy();
            LogService.Error(ex.Message);
         }
      }

      void DeleteFemAnalysis(CScore.Fem.FemAnalysis? analysis)
      {
         if (analysis == null) return;
         db.DeleteFemAnalysis(analysis);
      }

      void ShowFemNodeResult(ViewModels.FemAnalysisResultVM vm, string tag)
      {
         if (!vm.TryGetNodeResult(tag, out var point, out var displacement, out var reaction))
            return;

         var lines = new List<string>
         {
            string.Format(Loc.S("FemResultNodeCoordinates"), point.X, point.Y, point.Z)
         };
         if (displacement is { } d)
            lines.Add(string.Format(Loc.S("FemResultNodeDisplacements"), d.Ux, d.Uy, d.Uz, d.Rx, d.Ry, d.Rz));
         if (reaction is { } r)
            lines.Add(string.Format(Loc.S("FemResultNodeReactions"),
               r.Rx / 1000, r.Ry / 1000, r.Rz / 1000, r.Mx / 1000, r.My / 1000, r.Mz / 1000));
         MessageBox.Show(string.Join(Environment.NewLine, lines), Loc.S("FemResultNodeTitle"));
      }

      /// <summary>Открывает 2D-эпюры усилий по одному конструктивному стержню
      /// на основе последнего расчёта схемы с сохранённым результатом.</summary>
      public void ShowMemberForceDiagram(CScore.Fem.FemSchema schema, string memberTag)
      {
         var analysis = FemAnalysisResultResolver.FindLatestWithResult(schema.Analyses);
         if (analysis?.ResultId is not int rid)
         {
            LogService.Warning(Loc.S("FemMemberForceNoResult"));
            return;
         }
         var cr = db.GetCalcResultById(rid);
         if (cr == null)
         {
            LogService.Warning(Loc.S("FemMemberForceNoResult"));
            return;
         }
         var vm = new ViewModels.FemAnalysisResultVM(cr, db, schema);
         AttachFemResultVmEvents(vm, schema);
         CurrentPage = new Views.FemMemberForceView(db, schema, memberTag, vm);
      }

      void ShowMemberForceDiagram(CScore.Fem.FemSchema schema, string memberTag, ViewModels.FemAnalysisResultVM vm)
      {
         var dialog = new Views.FemMemberForceDialog(db, schema, memberTag, vm)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         dialog.ShowDialog();
      }

      /// <summary>Открывает read-only preview поперечного сечения конструктивного стержня
      /// из контекстного меню результата OpenSees.</summary>
      void ShowFemMemberSectionDialog(CScore.Fem.FemSchema schema, string memberTag)
      {
         var member = db.GetFemMembers(schema.Id).FirstOrDefault(m => m.ElemTag == memberTag);
         if (member?.CrossSectionId is not int sectionId)
         {
            ShowSectionStateUnavailable("FemMemberSectionNotAssigned");
            return;
         }

         var section = db.CrossSections.FirstOrDefault(s => s.Id == sectionId);
         if (section == null)
         {
            ShowSectionStateUnavailable("FemMemberSectionNotFound");
            return;
         }

         var dialog = new Views.FemMemberSectionDialog(section, PlotSettings)
         {
            Owner = System.Windows.Application.Current.MainWindow
         };
         dialog.ShowDialog();
      }

      /// <summary>Строит preview и сохраняет набор усилий выбранного стержня текущего шага.</summary>
      void CreateMemberForceSet(
         CScore.Fem.FemSchema schema,
         string memberTag,
         ViewModels.FemAnalysisResultVM vm)
      {
         var member = db.GetFemMembers(schema.Id)
            .FirstOrDefault(item => item.ElemTag == memberTag);
         if (member is null)
         {
            ShowFemForceSetError(ViewModels.FemMemberForceSetBuildError.MemberNotFound);
            return;
         }

         var build = ViewModels.FemMemberForceSetBuilder.Build(
            vm.BuildMemberForceSetInput(member));
         if (!build.IsSuccess)
         {
            ShowFemForceSetError(build.Error);
            return;
         }

         var dialog = new Views.FemMemberForceSetPreviewDialog(build.Preview!)
         {
            Owner = System.Windows.Application.Current?.MainWindow
         };
         if (dialog.ShowDialog() != true || dialog.Result is not { } selection)
            return;

         var forceSet = ViewModels.FemMemberForceSetFactory.Create(
            schema, member, build.Preview!, selection, ForceSets);
         db.SaveForceSet(forceSet);
         ForceSets.Add(forceSet);
      }

      /// <summary>Показывает локализованную ошибку построения preview набора.</summary>
      void ShowFemForceSetError(ViewModels.FemMemberForceSetBuildError error)
      {
         if (error == ViewModels.FemMemberForceSetBuildError.None) return;
         string key = error switch
         {
            ViewModels.FemMemberForceSetBuildError.MemberNotFound => "FemForceSetMemberNotFound",
            ViewModels.FemMemberForceSetBuildError.MissingSourceNode => "FemForceSetMissingSourceNode",
            ViewModels.FemMemberForceSetBuildError.CannotOrientMember => "FemForceSetCannotOrient",
            ViewModels.FemMemberForceSetBuildError.NoMeshElements => "FemForceSetNoMeshElements",
            ViewModels.FemMemberForceSetBuildError.MissingMeshNode => "FemForceSetMissingMeshNode",
            ViewModels.FemMemberForceSetBuildError.MissingElementForce => "FemForceSetMissingForce",
            ViewModels.FemMemberForceSetBuildError.NonFiniteForce => "FemForceSetNonFiniteForce",
            ViewModels.FemMemberForceSetBuildError.ReusedElement => "FemForceSetReusedElement",
            ViewModels.FemMemberForceSetBuildError.DuplicateElementPair => "FemForceSetDuplicateElementPair",
            ViewModels.FemMemberForceSetBuildError.EqualElementNodes => "FemForceSetEqualElementNodes",
            ViewModels.FemMemberForceSetBuildError.ZeroLengthElement => "FemForceSetZeroElementLength",
            ViewModels.FemMemberForceSetBuildError.NotConvergedStep => "FemForceSetNotConverged",
            _ => "FemForceSetInvalidTopology"
         };
         System.Windows.MessageBox.Show(
            Loc.S(key), Loc.S("Warning"),
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
      }

      /// <summary>Открывает (или обновляет) немодальное окно состояния сечения в точке
      /// интегрирования нелинейного FEM-результата OpenSees. Чтение волокон — фоновым
      /// потоком, на время чтения в статусбаре показывается индикатор занятости.</summary>
      async void OpenFemSectionState(ViewModels.FemSectionStateRequest request)
      {
         var section = db.CrossSections.FirstOrDefault(s => s.Id == request.SectionId);
         if (section == null) return;

         BeginBusy(Loc.S("FemSectionStateLoading"), indeterminate: true);
         IReadOnlyDictionary<int, (double StressPa, double Strain)> recorded;
         try
         {
            recorded = await System.Threading.Tasks.Task.Run(request.LoadRecordedFibers);
         }
         finally
         {
            EndBusy();
         }

         if (recorded.Count == 0)
         {
            ShowSectionStateUnavailable("FemSectionStateUnavailable");
            return;
         }

         var calcType = Enum.TryParse<CalcType>(request.CalcTypeName, out var parsed) ? parsed : CalcType.C;
         section.ResolveAndBuildDiagramms(CalcSettings.Sp63DescEtaMin,
            pool: Diagrams,
            rebarDifferentialDiagram: CalcSettings.RebarDifferentialDiagram, ekbEtaMin: CalcSettings.EkbDescEtaMin);
         var summary = OpenCS.OpenSees.CScore.FemRecordedSectionReducer.Reduce(section, calcType, recorded);
         var summaryVm = new ViewModels.FemSectionSummaryVM(request, summary, recorded);
         bool ten = CalcSettings.ResolveConcreteTension(calcType);
         var cutVm = new ViewModels.SectionCutVM(section, summary.Plane, calcType, FileDialogService, ten)
         {
            WindowTitleSuffix = string.Format(Loc.S("FemSectionStateSummaryTitle"),
               request.Location.SourceMemberTag, request.Location.IntegrationPoint)
         };
         var stressVm = new ViewModels.SectionPlotVM(section, summary.Plane, calcType,
            ViewModels.SectionPlotMode.Stress, CalcSettings, ten, recordedFibers: recorded);
         var strainVm = new ViewModels.SectionPlotVM(section, summary.Plane, calcType,
            ViewModels.SectionPlotMode.Strain, CalcSettings, ten, recordedFibers: recorded);
         stressVm.CutVM = cutVm;
         strainVm.CutVM = cutVm;
         var title = string.Format(Loc.S("FemSectionStateWindowTitle"),
            request.Location.SourceMemberTag, request.Location.IntegrationPoint, request.StepLabel);

         if (_femSectionStateWindow == null || !_femSectionStateWindow.IsVisible)
         {
            _femSectionStateWindow = new Views.FemSectionStateWindow
            {
               Owner = System.Windows.Application.Current.MainWindow
            };
            // После Close окно WPF нельзя переоткрыть — забываем ссылку и создаём новое.
            _femSectionStateWindow.Closed += (_, _) => _femSectionStateWindow = null;
            _femSectionStateWindow.Show();
         }
         _femSectionStateWindow.ShowContent(summaryVm, stressVm, strainVm, cutVm, CalcSettings, title);
         _femSectionStateWindow.Activate();
      }

      /// <summary>Показывает предупреждение о недоступном состоянии сечения ТИ.</summary>
      void ShowSectionStateUnavailable(string key)
         => MessageBox.Show(Loc.S(key), Loc.S("Warning"),
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

      /// <summary>Общие подписки событий результатного VM FEM-расчёта (все места создания).</summary>
      void AttachFemResultVmEvents(ViewModels.FemAnalysisResultVM vm, CScore.Fem.FemSchema schema)
      {
         vm.ShowMemberForceRequested += tag => ShowMemberForceDiagram(schema, tag, vm);
         vm.ShowMemberSectionRequested += tag => ShowFemMemberSectionDialog(schema, tag);
         vm.CreateMemberForceSetRequested += tag => CreateMemberForceSet(schema, tag, vm);
         vm.ShowNodeValuesRequested += tag => ShowFemNodeResult(vm, tag);
         vm.SectionStateRequested += OpenFemSectionState;
         vm.SectionStateUnavailable += ShowSectionStateUnavailable;
      }

      void DeleteFemCheck(CScore.Fem.FemCheck? check = null)
      {
         check ??= currentFemCheck;
         if (check == null) return;
         db.DeleteFemCheck(check);
         if (currentFemCheck == check)
         {
            currentFemCheck = null;
            CurrentPage = null!;
         }
      }

      void DeleteAllFemChecks()
      {
         var res = MessageBox.Show(
            Loc.S("FemCheckDeleteAllConfirm"),
            Loc.S("Confirmation"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         db.DeleteAllFemChecks();
         currentFemCheck = null;
         CurrentPage = null!;
      }

      void DeleteFemSchemaForceSets(CScore.Fem.FemSchema? schema)
      {
         schema ??= currentFemSchema;
         if (schema == null) return;

         var sets = ForceSets.Where(fs => fs.SourceSchemaId == schema.Id).ToList();
         if (sets.Count == 0) return;

         var res = System.Windows.MessageBox.Show(
            string.Format(Loc.S("ConfirmDeleteFemForceSets"), sets.Count, schema.Tag),
            Loc.S("Warning"),
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
         if (res != System.Windows.MessageBoxResult.Yes) return;

         foreach (var fs in sets)
             db.DeleteForceSet(fs);
       }

       void DeleteSelectedForceSets(CScore.Fem.FemSchema? schema)
       {
          schema ??= currentFemSchema;
          if (schema == null) return;

          var sets = ForceSets.Where(fs => fs.SourceSchemaId == schema.Id).ToList();
          if (sets.Count == 0) return;

          var dlg = new Views.DeleteForceSetsDialog(sets);
          dlg.Owner = System.Windows.Application.Current.MainWindow;
          if (dlg.ShowDialog() != true) return;

          var selected = dlg.SelectedSets;
          if (selected.Count == 0) return;

          var res = System.Windows.MessageBox.Show(
             string.Format(Loc.S("DeleteSelectedForceSetsConfirm"), selected.Count),
             Loc.S("Warning"),
             System.Windows.MessageBoxButton.YesNo,
             System.Windows.MessageBoxImage.Warning);
          if (res != System.Windows.MessageBoxResult.Yes) return;

           foreach (var fs in selected)
              db.DeleteForceSet(fs);
        }

        void DeleteSelectedForceSets(string kind)
        {
           var sets = ForceSets.Where(fs => fs.Kind == kind).ToList();
           if (sets.Count == 0) return;

           var dlg = new Views.DeleteForceSetsDialog(sets);
           dlg.Owner = System.Windows.Application.Current.MainWindow;
           if (dlg.ShowDialog() != true) return;

           var selected = dlg.SelectedSets;
           if (selected.Count == 0) return;

           var res = System.Windows.MessageBox.Show(
              string.Format(Loc.S("DeleteSelectedForceSetsConfirm"), selected.Count),
              Loc.S("Warning"),
              System.Windows.MessageBoxButton.YesNo,
              System.Windows.MessageBoxImage.Warning);
           if (res != System.Windows.MessageBoxResult.Yes) return;

            foreach (var fs in selected)
            {
               if (fs == currentBarForceSet)
               {
                  currentBarForceSet = null;
                  OnPropertyChanged(nameof(CurrentBarForceSet));
               }
               else if (fs == currentShellForceSet)
               {
                  currentShellForceSet = null;
                  OnPropertyChanged(nameof(CurrentShellForceSet));
               }
               db.DeleteForceSet(fs);
            }
         }

         void DeleteAllForceSets(string kind)
         {
            var sets = ForceSets.Where(fs => fs.Kind == kind).ToList();
            if (sets.Count == 0) return;

            var res = System.Windows.MessageBox.Show(
               string.Format(Loc.S("ConfirmDeleteAllForceSets"), sets.Count),
               Loc.S("Warning"),
               System.Windows.MessageBoxButton.YesNo,
               System.Windows.MessageBoxImage.Warning);
            if (res != System.Windows.MessageBoxResult.Yes) return;

            foreach (var fs in sets)
            {
               if (fs == currentBarForceSet)
               {
                  currentBarForceSet = null;
                  OnPropertyChanged(nameof(CurrentBarForceSet));
               }
               else if (fs == currentShellForceSet)
               {
                  currentShellForceSet = null;
                  OnPropertyChanged(nameof(CurrentShellForceSet));
               }
               db.DeleteForceSet(fs);
            }
         }

         #endregion
     }

   /// <summary>Маркерный объект группы «Усиление» в дереве проекта.</summary>
   public sealed class SectionTreeGroup
   {
      public System.Collections.ObjectModel.ObservableCollection<CScore.CrossSection> Items { get; }
      public SectionTreeGroup(System.Collections.ObjectModel.ObservableCollection<CScore.CrossSection> items)
         => Items = items;
   }

   /// <summary>Маркерный объект группы «Пластины» в дереве проекта.</summary>
   public sealed class PlateSectionTreeGroup
   {
      public System.Collections.ObjectModel.ObservableCollection<CScore.PlateSection> Items { get; }
      public PlateSectionTreeGroup(System.Collections.ObjectModel.ObservableCollection<CScore.PlateSection> items)
          => Items = items;
   }

   /// <summary>Маркерный объект группы «Эквивалентные сечения» в дереве проекта.</summary>
   public sealed class EquivalentSectionTreeGroup
   {
      public System.Collections.ObjectModel.ObservableCollection<CScore.PlateStrip.EquivalentSection> Items { get; }
      public EquivalentSectionTreeGroup(System.Collections.ObjectModel.ObservableCollection<CScore.PlateStrip.EquivalentSection> items)
         => Items = items;
   }
}

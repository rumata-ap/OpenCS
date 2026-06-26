using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Text.Json;

using CScore;
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
      MaterialArea? currentMaterialArea;
      ForceSet? currentBarForceSet;
      ForceSet? currentShellForceSet;
      PlateSection? currentPlateSection;
      FireSectionDef? currentFireSection;
      CScore.Fem.FemSchema? currentFemSchema;
      CScore.Fem.FemMember? currentFemMember;
      CScore.Fem.FemCheck?  currentFemCheck;

      /// <summary>
      /// Путь к текущему файлу проекта. null если проект ещё не был сохранён.
      /// </summary>
      public string? CurrentProjectPath { get; private set; }

      /// <summary>
      /// Признак наличия несохранённых изменений. Устанавливается при изменении данных,
      /// сбрасывается при сохранении или после загрузки проекта.
      /// </summary>
      public bool IsDirty { get; set; }

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
         set { _isBusy = value; OnPropertyChanged(); }
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

      /// <summary>Одиночные стержни (Category == SingleBar).</summary>
      public ObservableCollection<MaterialArea> SingleBarsLive { get; set; } = [];

      /// <summary>Простые фибровые сечения (не TwoStageSection).</summary>
      public ObservableCollection<CrossSection> FiberSectionsLive { get; } = [];

      /// <summary>Двухстадийные сечения (TwoStageSection).</summary>
      public ObservableCollection<CrossSection> TwoStageSectionsLive { get; } = [];

      /// <summary>Плитные сечения для дерева (синхронизируется с PlateSections).</summary>
      public ObservableCollection<PlateSection> PlateSectionsLive { get; } = [];

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
            currentFemSchema = value;
            if (value != null)
               CurrentPage = new Views.FemSchemaPage(value, this);
            OnPropertyChanged();
         }
      }

      /// <summary>Текущий конструктивный элемент МКЭ. При установке открывает FemMemberEditorPage.</summary>
      public CScore.Fem.FemMember? CurrentFemMember
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
      /// <summary>Команда создания нового конструктивного элемента МКЭ (без диалога).</summary>
      public ICommand NewFemMemberCommand       { get; set; } = null!;
      /// <summary>Команда создания нового конструктивного элемента через диалог ввода имени/типа/КЭ.</summary>
      public ICommand NewFemMemberDialogCommand { get; set; } = null!;
      /// <summary>Команда удаления конструктивного элемента МКЭ.</summary>
      public ICommand DeleteFemMemberCommand { get; set; } = null!;
      /// <summary>Команда добавления нормативной проверки к элементу.</summary>
      public ICommand AddFemCheckCommand     { get; set; } = null!;
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

      /// <summary>Команда удаления всех наборов усилий схемы МКЭ.</summary>
      public ICommand DeleteFemSchemaForceSetsCommand { get; set; } = null!;

      /// <summary>Команда импорта расчётной схемы из CSV-файлов ЛираСАПР.</summary>
      public ICommand ImportLiraSchemaFromCsvCommand { get; set; } = null!;

      /// <summary>Команда импорта расчётной схемы из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraSchemaFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий ЗН из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraForcesFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий РСН из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraRsnFromApiCommand { get; set; } = null!;

      /// <summary>Команда импорта усилий РСУ из запущенной ЛираСАПР через COM API.</summary>
      public ICommand ImportLiraRsuFromApiCommand { get; set; } = null!;

      /// <summary>Команда создания нового плитного сечения.</summary>
      public ICommand NewPlateSectionCommand { get; set; } = null!;
      /// <summary>Команда удаления плитного сечения (параметр PlateSection или текущее).</summary>
      public ICommand DeletePlateSectionCommand { get; set; } = null!;
      /// <summary>Команда дублирования плитного сечения (параметр PlateSection).</summary>
      public ICommand DuplicatePlateSectionCommand { get; set; } = null!;

      /// <summary>Команда открытия страницы расчётных задач.</summary>
      public ICommand OpenCalcTasksCommand { get; set; } = null!;
      /// <summary>Команда создания новой задачи из контекстного меню дерева.</summary>
      public ICommand NewCalcTaskCommand    { get; set; } = null!;
      /// <summary>Команда запуска задачи (параметр CalcTask).</summary>
      public ICommand RunCalcTaskCommand    { get; set; } = null!;
      /// <summary>Команда редактирования задачи (параметр CalcTask).</summary>
      public ICommand EditCalcTaskCommand   { get; set; } = null!;
      /// <summary>Команда удаления задачи (параметр CalcTask).</summary>
      public ICommand DeleteCalcTaskCommand { get; set; } = null!;

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
               CurrentPage = value.Category == AreaCategory.RebarGroup
                  ? (System.Windows.Controls.UserControl)new Views.RebarGroupEditorPage(value, this)
                  : new Views.MaterialAreaPage(value, this);
            OnPropertyChanged();
         }
      }

      /// <summary>Команда создания новой полигональной области.</summary>
      public ICommand NewAreaCommand { get; set; } = null!;

      /// <summary>Команда удаления текущей MaterialArea.</summary>
      public ICommand DeleteMaterialAreaCommand { get; set; } = null!;

      /// <summary>Команда создания новой группы арматуры (реализуется в Блоке 3).</summary>
      public ICommand NewRebarGroupCommand { get; set; } = null!;

      /// <summary>Команда создания одиночного стержня (реализуется в Блоке 3).</summary>
      public ICommand NewSingleBarCommand { get; set; } = null!;

      /// <summary>Команда создания нового поперечного сечения.</summary>
      public ICommand NewCrossSectionCommand { get; set; } = null!;

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

      /// <summary>Команда удаления набора усилий (параметр ForceSet).</summary>
      public ICommand DeleteForceSetCommand { get; set; } = null!;

      /// <summary>Команда задания вида загружения / переименования набора усилий (параметр ForceSet).</summary>
      public ICommand SetForceSetLoadTypeCommand { get; set; } = null!;

      /// <summary>Команда дублирования набора усилий (параметр ForceSet).</summary>
      public ICommand DuplicateForceSetCommand { get; set; } = null!;

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
         set { currentPage = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPageTitle)); }
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

      /// <summary>Настройки импорта усилий LIRA SAPR (HTML).</summary>
      public Utilites.LiraImportSettings LiraImportSettings { get; set; } = Utilites.LiraImportSettings.Default;

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
       public AppViewModel(ILogService logService, IFileDialogService fileDialogService)
       {
          LogService = logService;
          FileDialogService = fileDialogService;
          SectionTreeItems = new System.Windows.Data.CompositeCollection
          {
             new System.Windows.Data.CollectionContainer { Collection = FiberSectionsLive },
             new SectionTreeGroup(TwoStageSectionsLive),
             new PlateSectionTreeGroup(PlateSectionsLive),
          };

          db = new DatabaseService(GetTempDbPath());
          InitNewDatabase();
          PlotSettings = db.LoadPlotSettings() ?? Utilites.PlotSettings.Default;
          CsvSettings = db.LoadCsvSettings() ?? Utilites.CsvExportSettings.Default;
          CalcSettings = db.LoadCalcSettings() ?? Utilites.CalcSettings.Default;
          LiraImportSettings = db.LoadLiraImportSettings() ?? Utilites.LiraImportSettings.Default;
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
         db.ReinitializeDatabase(GetTempDbPath());
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
      /// Проверяет, нужно ли сохранять проект, и показывает диалог при необходимости.
      /// Возвращает true, если можно продолжить закрытие; false, если пользователь отменил.
      /// </summary>
      public bool ConfirmSaveIfNeeded()
      {
         if (CurrentProjectPath != null && !IsDirty)
            return true;

         if (CurrentProjectPath != null && IsDirty)
         {
            try { db.SaveAll(); } catch { }
            IsDirty = false;
            return true;
         }

         var result = MessageBox.Show(
            Loc.S("ConfirmSaveOnExit"),
            Loc.S("Confirmation"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

         if (result == MessageBoxResult.Yes)
         {
            SaveProject();
            return CurrentProjectPath != null;
         }
         if (result == MessageBoxResult.No)
            return true;

         return false;
      }

      /// <summary>
      /// Сохраняет проект. Если путь не задан — показывает диалог «Сохранить как».
      /// </summary>
      void SaveProject()
      {
         if (CurrentProjectPath == null)
         {
            SaveAsProject();
            return;
         }
         try
         {
            db.SaveAll();
            IsDirty = false;
            LogService.Info(Loc.S("ProjectSaved"));
         }
         catch (Exception ex)
         {
            LogService.Error(string.Format(Loc.S("ProjectSaveError"), ex.Message));
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
         CrossSections.CollectionChanged += (_, _) => IsDirty = true;
         ForceSets = db.ForceSets;
         BarForceSets   = new ObservableCollection<ForceSet>(ForceSets.Where(fs => fs.Kind == "bar"));
         ShellForceSets = new ObservableCollection<ForceSet>(ForceSets.Where(fs => fs.Kind == "shell"));
         ForceSets.CollectionChanged += (_, e) =>
         {
            IsDirty = true;
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
               BarForceSets.Clear();
               ShellForceSets.Clear();
               return;
            }
            if (e.NewItems != null)
               foreach (ForceSet fs in e.NewItems)
               {
                  if (fs.Kind == "shell") { if (!ShellForceSets.Contains(fs)) ShellForceSets.Add(fs); }
                  else                    { if (!BarForceSets.Contains(fs))   BarForceSets.Add(fs); }
               }
            if (e.OldItems != null)
               foreach (ForceSet fs in e.OldItems)
               {
                  BarForceSets.Remove(fs);
                  ShellForceSets.Remove(fs);
               }
         };
         PlateSections = db.PlateSections;
         PlateSections.CollectionChanged += (_, _) => { RefreshPlateSectionsLive(); IsDirty = true; };
         RefreshPlateSectionsLive();
         FireSections = db.FireSections;
         FireSections.CollectionChanged += (_, _) =>
         {
            RenumberFireSections();
            IsDirty = true;
         };
         RenumberFireSections();
         CalcTasks   = db.CalcTasks;
         CalcResults = db.CalcResults;
         FemSchemas  = db.FemSchemas;
         FemChecks   = db.FemChecks;
         BuildFemRootNodes();
         CalcTasks.CollectionChanged   += (_, _) => IsDirty = true;
         CalcResults.CollectionChanged += (_, _) => IsDirty = true;
         MaterialAreas = db.MaterialAreas;
         MaterialAreas.CollectionChanged += (_, _) => { RefreshMaterialAreaLiveCollections(); IsDirty = true; };

         Materials.CollectionChanged += Concretes_CollectionChanged;
         Contours.CollectionChanged += Contours_CollectionChanged;
         Materials.CollectionChanged += (_, _) => IsDirty = true;
         Contours.CollectionChanged += (_, _) => IsDirty = true;
         Circles.CollectionChanged += (_, _) => IsDirty = true;
         Diagrams.CollectionChanged += (_, _) => IsDirty = true;
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
         DelMaterialCommand = new RelayCommand(DelMaterial);
         FromDxfCommand = new RelayCommand(FromDxf);
         DelContourCommand = new RelayCommand(DelContour);
         NewProjectCommand = new RelayCommand(NewProject);
         OpenProjectCommand = new RelayCommand(OpenProject);
         SaveProjectCommand = new RelayCommand(SaveProject);
         SaveAsProjectCommand = new RelayCommand(SaveAsProject);
          ExitCommand = new RelayCommand(Exit);
          VacuumDbCommand = new RelayCommand(_ => VacuumDb());
         OpenSettingsCommand = new RelayCommand(_ => new Views.SettingsWindow(this).ShowDialog());
         SetLanguageCommand = new RelayCommand(SetLanguage);
         NewCrossSectionCommand    = new RelayCommand(_ => NewCrossSection());
         EditCrossSectionCommand   = new RelayCommand(_ => EditCrossSection());
         DeleteCrossSectionCommand = new RelayCommand(_ => DeleteCrossSection());
         NewTwoStageSectionCommand = new RelayCommand(_ => NewTwoStageSection());
         NewAreaCommand            = new RelayCommand(_ => NewArea());
         DeleteMaterialAreaCommand = new RelayCommand(_ => DeleteMaterialArea());
         NewRebarGroupCommand      = new RelayCommand(_ => NewRebarGroup());
         NewSingleBarCommand       = new RelayCommand(_ => NewSingleBar());
         NewBarForceSetCommand        = new RelayCommand(_ => NewBarForceSet());
         NewShellForceSetCommand      = new RelayCommand(_ => NewShellForceSet());
         DeleteForceSetCommand        = new RelayCommand(p => DeleteForceSet(p as CScore.ForceSet));
         SetForceSetLoadTypeCommand   = new RelayCommand(p => SetForceSetLoadType(p as CScore.ForceSet));
         DuplicateForceSetCommand     = new RelayCommand(p => DuplicateForceSet(p as CScore.ForceSet));
         NewPlateSectionCommand       = new RelayCommand(_ => NewPlateSection());
         DeletePlateSectionCommand    = new RelayCommand(p => DeletePlateSection(p as CScore.PlateSection));
         DuplicatePlateSectionCommand = new RelayCommand(p => DuplicatePlateSection(p as CScore.PlateSection));
         NewFireSectionCommand        = new RelayCommand(_ => NewFireSection());
         DeleteFireSectionCommand     = new RelayCommand(_ => DeleteFireSection());
         RenameFireSectionCommand     = new RelayCommand(_ => RenameFireSection());
         OpenCalcTasksCommand         = new RelayCommand(_ => CurrentPage = new Views.CalcTasksPage(this));
         NewCalcTaskCommand    = new RelayCommand(_ => NewCalcTask());
         RunCalcTaskCommand    = new RelayCommand(p => RunCalcTask(p as CalcTask),    p => p is CalcTask);
         EditCalcTaskCommand   = new RelayCommand(p => EditCalcTask(p as CalcTask),   p => p is CalcTask);
         DeleteCalcTaskCommand = new RelayCommand(p => DeleteCalcTask(p as CalcTask), p => p is CalcTask);
         ImportContoursFromDxfCommand = new RelayCommand(_ => ImportContoursFromDxf());
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
         NewFemMemberCommand       = new RelayCommand(p => NewFemMember(p as CScore.Fem.FemSchema));
         NewFemMemberDialogCommand = new RelayCommand(p => NewFemMemberDialog(p as CScore.Fem.FemSchema));
         DeleteFemMemberCommand    = new RelayCommand(_ => DeleteFemMember());
         AddFemCheckCommand     = new RelayCommand(p => AddFemCheck(p as CScore.Fem.FemMember));
         RunFemCheckCommand     = new RelayCommand(p => RunFemCheck(p as CScore.Fem.FemCheck));
         EditFemCheckCommand       = new RelayCommand(p => EditFemCheck(p as CScore.Fem.FemCheck));
         DeleteFemCheckCommand     = new RelayCommand(p => DeleteFemCheck(p as CScore.Fem.FemCheck));
         DeleteAllFemChecksCommand = new RelayCommand(_ => DeleteAllFemChecks());
         AddSlsFemCheckCommand     = new RelayCommand(_ => AddSlsFemCheck());
         AddFemCheckByGroupCommand = new RelayCommand(p =>
         {
             if (p is string g && g == "sls") AddSlsFemCheck();
             else                             AddFemCheck(null);
         });
         DeleteFemSchemaForceSetsCommand = new RelayCommand(p => DeleteFemSchemaForceSets(p as CScore.Fem.FemSchema));
         ImportLiraSchemaFromCsvCommand  = new RelayCommand(_ => ImportLiraSchemaFromCsv());
         ImportLiraSchemaFromApiCommand  = new RelayCommand(_ => ImportLiraSchemaFromApi());
         ImportLiraForcesFromApiCommand  = new RelayCommand(_ => ImportLiraForcesFromApi());
         ImportLiraRsnFromApiCommand     = new RelayCommand(_ => ImportLiraRsnFromApi());
         ImportLiraRsuFromApiCommand     = new RelayCommand(_ => ImportLiraRsuFromApi());
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
         if (CurrentPage is Views.MaterialAreaPage map && map.DataContext is ViewModels.MaterialAreaVM mavm)
            mavm.RefreshPlot();
         DxfBgApplied?.Invoke(PlotSettings.DxfCanvasBackground);
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
         var vm = new ContourVM { mvm = this, Contour = contour };
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
         IsDirty = true;
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
         currentFireSection   = null;
         OnPropertyChanged(nameof(CurrentCrossSection));
         OnPropertyChanged(nameof(CurrentMaterialArea));
         OnPropertyChanged(nameof(CurrentBarForceSet));
         OnPropertyChanged(nameof(CurrentShellForceSet));
         OnPropertyChanged(nameof(CurrentPlateSection));
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
         IsDirty = false;
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
         if (CurrentProjectPath == null)
         {
            SaveAsProject(o);
            return;
         }
         try
         {
            db.SaveAll();
             LogService.Info(Loc.S("ProjectSaved"));
          }
          catch (Exception ex)
          {
             LogService.Error(string.Format(Loc.S("ProjectSaveError"), ex.Message));
         }
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
             IsDirty = false;
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
         IsDirty = true;
      }

      public void RemoveMaterialArea(ViewModels.MaterialAreaVM vm)
      {
         var sec = CrossSections.FirstOrDefault(s => s.Areas.Contains(vm.Model));
         if (sec == null) return;
         sec.Areas.Remove(vm.Model);
         IsDirty = true;
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
         IsDirty = true;
      }

      void SetForceSetLoadType(CScore.ForceSet? fs)
      {
         if (fs == null) return;
         var dlg = new Views.ForceSetPropsDialog(fs);
         if (dlg.ShowDialog() != true) return;
         var vm = (ForceSetPropsVM)dlg.DataContext;
         fs.Tag = vm.ResultName;
         db.SaveForceSet(fs);
         // ForceSet не INPC — форсируем обновление TreeView через remove+insert
         var col = fs.Kind == "shell" ? ShellForceSets : BarForceSets;
         int idx = col.IndexOf(fs);
         if (idx >= 0) { col.RemoveAt(idx); col.Insert(idx, fs); }
         IsDirty = true;
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
               Vx = i.Vx, Vy = i.Vy, T = i.T
            }),
            ShellItems = src.ShellItems.ConvertAll(i => new CScore.ShellLoadItem
            {
               Label = i.Label, Nx = i.Nx, Ny = i.Ny, Nxy = i.Nxy,
               Mx = i.Mx, My = i.My, Mxy = i.Mxy, Qx = i.Qx, Qy = i.Qy
            }),
         };
         var col = src.Kind == "shell" ? ShellForceSets : BarForceSets;
         copy.Num = col.Count > 0 ? col.Max(s => s.Num) + 1 : 1;
         for (int i = 0; i < copy.Items.Count;      i++) copy.Items[i].Num      = i + 1;
         for (int i = 0; i < copy.ShellItems.Count; i++) copy.ShellItems[i].Num = i + 1;
         db.SaveForceSet(copy);
         ForceSets.Add(copy);
         IsDirty = true;
      }

      public void RefreshMaterialAreaLiveCollections()
      {
         AreasLive       = new(MaterialAreas.Where(a => a.Category == AreaCategory.Region));
         RebarGroupsLive = new(MaterialAreas.Where(a => a.Category == AreaCategory.RebarGroup));
         SingleBarsLive  = new(MaterialAreas.Where(a => a.Category == AreaCategory.SingleBar));
         OnPropertyChanged(nameof(AreasLive));
         OnPropertyChanged(nameof(RebarGroupsLive));
         OnPropertyChanged(nameof(SingleBarsLive));
      }

      public void RefreshSectionLiveCollections()
      {
         FiberSectionsLive.Clear();
         foreach (var s in CrossSections.Where(s => s is not TwoStageSection))
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

      void NewSingleBar()
      {
         var area = new MaterialArea
         {
            Tag = $"Стержень {SingleBarsLive.Count + 1}",
            Category = AreaCategory.SingleBar
         };
         CurrentPage = new Views.MaterialAreaPage(area, this);
      }

      void DeleteMaterialArea()
      {
         if (currentMaterialArea == null) return;
         db.DeleteMaterialArea(currentMaterialArea);
         RefreshMaterialAreaLiveCollections();
         currentMaterialArea = null;
         CurrentPage = null!;
         OnPropertyChanged(nameof(CurrentMaterialArea));
         IsDirty = true;
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
         IsDirty = true;
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
         IsDirty = true;
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
         IsDirty = true;
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
         IsDirty = true;
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
         IsDirty = true;
      }

      void NewCalcTask()
      {
         var dlg = new CalcTaskPropsDialog(this)
         {
            Owner = Application.Current.MainWindow
         };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;
         var ct = dlg.Result;
         ct.Num = CalcTasks.Count > 0 ? CalcTasks.Max(t => t.Num) + 1 : 1;
         db.SaveCalcTask(ct);
         IsDirty = true;
         LogService.Info(string.Format(Loc.S("CalcTaskCreated"), ct.Tag));
      }

      void RunCalcTask(CalcTask? ct)
      {
         if (ct == null) return;
         var section = CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
         if (section == null)
         {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
               MessageBoxButton.OK, MessageBoxImage.Error);
            return;
         }
         var fs = BarForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
         var fi = fs?.Items.FirstOrDefault(i => i.Id == ct.ForceItemId);
         if (fi == null)
         {
            if (CalcTaskForceHelper.UsesManualForces(ct))
            {
               fi = CalcTaskForceHelper.ResolveSingleForces(ct, BarForceSets);
               if (fi == null)
               {
                  MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
                     MessageBoxButton.OK, MessageBoxImage.Error);
                  return;
               }
            }
            else if (CalcTaskForceHelper.UsesDummyForceItem(ct))
            {
               fi = new LoadItem(); // обработчик читает усилия из ParamsJson/ctx.Database.ForceSets
            }
            else
            {
               MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
                  MessageBoxButton.OK, MessageBoxImage.Error);
               return;
            }
         }
         var result = TaskRunner.Run(ct, section, fi, CalcSettings, new TaskRunContext
         {
            Database     = db,
            FireSections = FireSections
         });
         db.SaveCalcResult(result);
         IsDirty = true;
         var statusKey = result.Status switch
         {
            "ok"            => "CalcResultOk",
            "not_converged" => "CalcResultNotConverged",
            "partial"       => "CalcResultPartial",
            "not_passed"    => "CalcResultNotPassed",
            _               => "CalcResultError"
         };
         LogService.Info(string.Format(Loc.S(statusKey), ct.Tag));
         CurrentPage = new CalcResultView(result, this);
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
         IsDirty = true;
         CalcTaskModified?.Invoke();
      }

      void DeleteCalcTask(CalcTask? ct)
      {
         if (ct == null) return;
         var res = MessageBox.Show(Loc.S("ConfirmDeleteCalcTask"), Loc.S("Warning"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         db.DeleteCalcTask(ct);
         IsDirty = true;
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

            var nodes    = CScore.Import.LiraSchemaConverter.ToFemNodes(raw, schema.Id);
            var elements = CScore.Import.LiraSchemaConverter.ToFemBarElements(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemShellElements(raw, schema.Id))
                .ToArray();
            var members  = CScore.Import.LiraSchemaConverter.ToFemMembersByStiffness(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMembersByPlateStiffness(raw, schema.Id))
                .ToArray();

            db.SaveFemTopology(schema.Id, nodes, elements, members);
            RefreshFemSchemaTreeCounts(schema);

            int barCount   = raw.Elements.Count(e => e.NodeIds.Length == 2);
            int shellCount = raw.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
            LogService.Info(string.Format(Loc.S("ImportLiraSchemaSuccess"),
               raw.Nodes.Count, barCount, shellCount, members.Length));
         }
         catch (Exception ex)
         {
            System.Windows.MessageBox.Show(ex.Message,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
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

      void RefreshFemSchemaTreeCounts(CScore.Fem.FemSchema schema)
          => femSchemasGroup?.Schemas
              .FirstOrDefault(vm => vm.Schema == schema)
              ?.ReloadTopology();



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
            var raw = await RunOnStaThread(() => Services.LiraApiSchemaReader.Read());
            var schema = new CScore.Fem.FemSchema { Tag = "Схема Лира (API)", SourceType = "lira" };
            db.SaveFemSchema(schema);
            var nodes    = CScore.Import.LiraSchemaConverter.ToFemNodes(raw, schema.Id);
            var elements = CScore.Import.LiraSchemaConverter.ToFemBarElements(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemShellElements(raw, schema.Id))
                .ToArray();
            var members  = CScore.Import.LiraSchemaConverter.ToFemMembersByStiffness(raw, schema.Id)
                .Concat(CScore.Import.LiraSchemaConverter.ToFemMembersByPlateStiffness(raw, schema.Id))
                .ToArray();
            db.SaveFemTopology(schema.Id, nodes, elements, members);
            RefreshFemSchemaTreeCounts(schema);
            int barCount   = raw.Elements.Count(e => e.NodeIds.Length == 2);
            int shellCount = raw.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
            string done = string.Format(Loc.S("ImportLiraSchemaSuccess"),
               raw.Nodes.Count, barCount, shellCount, members.Length);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (Exception ex)
         {
            EndBusy();
            string msg = ex.Message;
            if (msg.Length > 300)
            {
               LogService.Error("ДИАГНОСТИКА ЛираСАПР COM:\n" + msg);
               msg = msg.Split('\n')[0] + "\n\nПодробности — в журнале событий.";
            }
            System.Windows.MessageBox.Show(msg,
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Error);
         }
      }

      async void ImportLiraForcesFromApi()
      {
         if (currentFemMember == null)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoMember"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(
            currentFemMember.ElemIdsJson) ?? [];

         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoElements"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == currentFemMember.SchemaId);
         if (schema == null)
         {
            LogService.Warning(Loc.S("ImportLiraForcesNoSchema"));
            return;
         }

         var member = currentFemMember;
         BeginBusy(string.Format(Loc.S("ImportLiraForcesStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadLoadCaseForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member.Id);
            IsDirty = true;
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

      async void ImportLiraRsnFromApi()
      {
         if (currentFemMember == null)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoMember"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(
            currentFemMember.ElemIdsJson) ?? [];

         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(
               Loc.S("ImportLiraForcesNoElements"),
               Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK,
               System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == currentFemMember.SchemaId);
         if (schema == null)
         {
            LogService.Warning(Loc.S("ImportLiraForcesNoSchema"));
            return;
         }

         var member = currentFemMember;
         BeginBusy(string.Format(Loc.S("ImportLiraRsnStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadLoadCombinationForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member.Id);
            IsDirty = true;
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

      async void ImportLiraRsuFromApi()
      {
         if (currentFemMember == null)
         {
            System.Windows.MessageBox.Show(Loc.S("ImportLiraForcesNoMember"), Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
         }

         var elemIds = System.Text.Json.JsonSerializer.Deserialize<int[]>(currentFemMember.ElemIdsJson) ?? [];
         if (elemIds.Length == 0)
         {
            System.Windows.MessageBox.Show(Loc.S("ImportLiraForcesNoElements"), Loc.S("ImportLiraErrorTitle"),
               System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
         }

         var schema = FemSchemas.FirstOrDefault(s => s.Id == currentFemMember.SchemaId);
         if (schema == null) { LogService.Warning(Loc.S("ImportLiraForcesNoSchema")); return; }

         var member = currentFemMember;
         BeginBusy(string.Format(Loc.S("ImportLiraRsuStarted"), elemIds.Length, member.Tag));

         try
         {
            var liraSettings = LiraImportSettings;
            var memberTagCapture = member.Tag;
            var forceSets = await RunOnStaThread(() =>
               Services.LiraApiForceImporter.ReadDesignCombinationForces(schema, elemIds, liraSettings, memberTagCapture));

            SaveImportedForceSets(forceSets, member.Id);
            IsDirty = true;
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

      void SaveImportedForceSets(IReadOnlyList<CScore.ForceSet> forceSets, int memberId)
      {
         foreach (var fs in forceSets)
         {
            fs.SourceMemberId = memberId;
            db.SaveForceSet(fs);
            if (!ForceSets.Contains(fs))
               ForceSets.Add(fs);
         }
      }

      void BeginBusy(string message)
      {
         StatusMessage = message;
         IsBusy = true;
      }

      void EndBusy(string? message = null)
      {
         IsBusy = false;
         StatusMessage = message ?? "";
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
         var member = new CScore.Fem.FemMember { SchemaId = schema.Id, Tag = "Элемент" };
         db.SaveFemMember(member);
         schema.Members.Add(member);
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

      void DeleteFemMember()
      {
         if (currentFemMember == null) return;
         db.DeleteFemMember(currentFemMember);
         currentFemMember = null;
         CurrentPage = null!;
      }

      /// <summary>Создаёт FemMember из списка выбранных КЭ.</summary>
      public void CreateFemMemberFromSelection(CScore.Fem.FemSchema schema, IList<CScore.Fem.FemElement> elems)
      {
         if (elems.Count == 0) return;
         var ids = elems
            .Select(e => int.TryParse(e.ElemTag, out int id) ? id : 0)
            .Where(id => id > 0)
            .ToArray();
         var tag = elems.Count == 1
            ? (elems[0].SectionTag ?? elems[0].ElemTag)
            : $"Балка ({elems.Count} КЭ)";
         var member = new CScore.Fem.FemMember
         {
            SchemaId    = schema.Id,
            Tag         = tag,
            MemberType  = "Балка",
            ElemIdsJson = System.Text.Json.JsonSerializer.Serialize(ids),
         };
         db.SaveFemMember(member);
         schema.Members.Add(member);
      }

      /// <summary>Создаёт FemMember из явного списка LIRA-id КЭ (строка диапазонов уже распарсена).</summary>
      public void CreateFemMemberFromRange(
         CScore.Fem.FemSchema schema,
         IList<int>           elemIds,
         string               tag,
         string?              memberType)
      {
         var member = new CScore.Fem.FemMember
         {
            SchemaId    = schema.Id,
            Tag         = string.IsNullOrWhiteSpace(tag)
                             ? (elemIds.Count > 0
                                 ? $"{(string.IsNullOrWhiteSpace(memberType) ? "Группа" : memberType)} ({elemIds.Count} КЭ)"
                                 : "Новый элемент")
                             : tag,
            MemberType  = string.IsNullOrWhiteSpace(memberType) ? null : memberType,
            ElemIdsJson = System.Text.Json.JsonSerializer.Serialize(elemIds),
         };
         db.SaveFemMember(member);
         schema.Members.Add(member);
      }

      /// <summary>Авто-группирует стержни схемы по SectionTag. Пропускает уже существующие группы.</summary>
      public void AutoGroupFemMembersBySection(CScore.Fem.FemSchema schema)
      {
         var elements = db.GetFemElements(schema.Id)
            .Where(e => e.ElemType == "beam")
            .ToList();
         if (elements.Count == 0) return;

         var existingTags = schema.Members.Select(m => m.Tag).ToHashSet();
         var grouped = elements.GroupBy(e => e.SectionTag ?? "");
         int added = 0;
         foreach (var grp in grouped.OrderBy(g => g.Key))
         {
            if (existingTags.Contains(grp.Key)) continue;
            var ids = grp
               .Select(e => int.TryParse(e.ElemTag, out int id) ? id : 0)
               .Where(id => id > 0)
               .ToArray();
            var member = new CScore.Fem.FemMember
            {
               SchemaId    = schema.Id,
               Tag         = grp.Key,
               MemberType  = "Балка",
               ElemIdsJson = System.Text.Json.JsonSerializer.Serialize(ids),
            };
            db.SaveFemMember(member);
            schema.Members.Add(member);
            existingTags.Add(grp.Key);
            added++;
         }
         if (added > 0)
            LogService.Info(string.Format(Loc.S("FemGroupAutoResult"), added));
      }

      void AddFemCheck(CScore.Fem.FemMember? member)
      {
         var dlg = new Views.FemCheckDialog(this);
         if (dlg.ShowDialog() != true || dlg.ResultCheck == null) return;
         var check = dlg.ResultCheck;
         db.SaveFemCheck(check);
      }

      void AddSlsFemCheck()
      {
         // TODO Task 4: открыть FemSlsCheckDialog
         System.Windows.MessageBox.Show("SLS диалог — в разработке");
      }

      void EditFemCheck(CScore.Fem.FemCheck? check)
      {
         check ??= currentFemCheck;
         if (check == null) return;
         var dlg = new Views.FemCheckDialog(this, check);
         if (dlg.ShowDialog() != true) return;
         db.SaveFemCheck(check);
      }

      void RunFemCheck(CScore.Fem.FemCheck? check)
      {
         check ??= currentFemCheck;
         if (check == null) return;

         var member = FemSchemas.SelectMany(s => s.Members).FirstOrDefault(m => m.Id == check.MemberId);
         if (member == null) { LogService.Warning($"FemCheck #{check.Id}: конструктивный элемент не найден"); return; }

         CrossSection? barSection = null;
         PlateSection? plateSection = null;

         CScore.Material? concreteMat = null;
         CScore.Material? rebarMat    = null;

         if (check.NormCode == "rc_plate_check")
         {
            plateSection = PlateSections.FirstOrDefault(s => s.Id == member.PlateSectionId);
            if (plateSection != null)
            {
               concreteMat = Materials.FirstOrDefault(m => m.Id == plateSection.ConcreteMaterialId);
               rebarMat    = Materials.FirstOrDefault(m => m.Id == plateSection.RebarMaterialId);
            }
         }
         else
         {
            barSection = CrossSections.FirstOrDefault(s => s.Id == member.CrossSectionId);
         }

         var memberForceSets = ForceSets.Where(f => f.SourceMemberId == member.Id).ToList();

         var result = CScore.Fem.FemCheckRunner.RunMulti(
            check, member, barSection, plateSection, memberForceSets,
            (task, sect, item) => TaskRunner.Run(task, sect, item),
            concreteMat, rebarMat);

         db.SaveCalcResultRaw(result, check.Id);
         check.ResultId = result.Id;
         db.SaveFemCheck(check);

         CurrentFemCheck = check;
         CurrentPage = new Views.FemCheckResultView(result);
         LogService.Info($"FemCheck «{check.DisplayTag}»: {result.Status}");
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
}
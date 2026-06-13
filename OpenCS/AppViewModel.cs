using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;

using CScore;
using OpenCS.Services;
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
      internal DatabaseService db = new();

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

      /// <summary>Наборы усилий для стержней (Kind="bar").</summary>
      public ObservableCollection<ForceSet> BarForceSets { get; set; } = null!;

      /// <summary>Наборы усилий для пластин (Kind="shell").</summary>
      public ObservableCollection<ForceSet> ShellForceSets { get; set; } = null!;

      /// <summary>Плитные сечения.</summary>
      public ObservableCollection<PlateSection> PlateSections { get; set; } = null!;

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

      /// <summary>Команда создания нового плитного сечения.</summary>
      public ICommand NewPlateSectionCommand { get; set; } = null!;
      /// <summary>Команда удаления плитного сечения (параметр PlateSection или текущее).</summary>
      public ICommand DeletePlateSectionCommand { get; set; } = null!;
      /// <summary>Команда дублирования плитного сечения (параметр PlateSection).</summary>
      public ICommand DuplicatePlateSectionCommand { get; set; } = null!;

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

      /// <summary>
      /// Команда привязки для создания нового контура. Открывает пустую страницу контура.
      /// </summary>
      public ICommand NewContourCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для создания нового материала. Открывает пустую страницу материала.
      /// </summary>
      public ICommand NewMaterialCommand { get; set; } = null!;

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

      /// <summary>
      /// Команда открытия окна настройки отображения графиков.
      /// </summary>
      public ICommand OpenPlotSettingsCommand { get; set; } = null!;

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

      /// <summary>
      /// Команда открытия окна настройки экспорта CSV.
      /// </summary>
      public ICommand OpenCsvSettingsCommand { get; set; } = null!;

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

          InitNewDatabase();
          PlotSettings = db.LoadPlotSettings() ?? Utilites.PlotSettings.Default;
          CsvSettings = db.LoadCsvSettings() ?? Utilites.CsvExportSettings.Default;
          InitializeCollections();
           InitializeCommands();
        }

      /// <summary>
      /// Создаёт новую временную базу данных и переключается на неё.
      /// </summary>
      void InitNewDatabase()
      {
         var tempPath = Path.Combine(Path.GetTempPath(), "opencs_new.db");
         db.ReinitializeDatabase(tempPath);
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
         NewContourCommand = new RelayCommand(NewContour);
         NewMaterialCommand = new RelayCommand(NewMaterial);
         DelMaterialCommand = new RelayCommand(DelMaterial);
         FromDxfCommand = new RelayCommand(FromDxf);
         DelContourCommand = new RelayCommand(DelContour);
         NewProjectCommand = new RelayCommand(NewProject);
         OpenProjectCommand = new RelayCommand(OpenProject);
         SaveProjectCommand = new RelayCommand(SaveProject);
         SaveAsProjectCommand = new RelayCommand(SaveAsProject);
          ExitCommand = new RelayCommand(Exit);
         OpenPlotSettingsCommand = new RelayCommand(_ => new Views.SettingsWindow(this).ShowDialog());
         OpenCsvSettingsCommand = new RelayCommand(_ => new Views.CsvSettingsWindow(this).ShowDialog());
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
         ImportContoursFromDxfCommand = new RelayCommand(_ => ImportContoursFromDxf());
         AddCircleCommand             = new RelayCommand(_ => AddCircle());
         DeleteCircleCommand          = new RelayCommand(p => DeleteCircle(p as CircleP));
         ImportCirclesFromDxfCommand  = new RelayCommand(_ => ImportCirclesFromDxf());
         ExportCirclesToDxfCommand    = new RelayCommand(_ => ExportCirclesToDxf());
         ImportCirclesFromCsvCommand  = new RelayCommand(_ => ImportCirclesFromCsv());
         ExportCirclesToCsvCommand    = new RelayCommand(_ => ExportCirclesToCsv());
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
      /// Обработчик команды <see cref="NewContourCommand"/>. Создаёт новую пустую
      /// ViewModel контура и открывает страницу редактирования контура.
      /// </summary>
      void NewContour(object? o = null)
      {
         CurrentContour = new ContourVM { mvm = this };
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
         string fileName = FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");
         if (string.IsNullOrEmpty(fileName)) return;
         CurrentPage = new FromDxfPage(this, fileName);
      }

      private void AddCircle(object? _ = null)
      {
         var cp = new CircleP(0, 0, 0.01);
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
         string fileName = FileDialogService.OpenFile(
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
         string fileName = FileDialogService.OpenFile(
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

         string fileName = FileDialogService.SaveFile(
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
         string fileName = FileDialogService.OpenFile(
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

         string fileName = FileDialogService.SaveFile(
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
         OnPropertyChanged(nameof(CurrentCrossSection));
         OnPropertyChanged(nameof(CurrentMaterialArea));
         OnPropertyChanged(nameof(CurrentBarForceSet));
         OnPropertyChanged(nameof(CurrentShellForceSet));
         OnPropertyChanged(nameof(CurrentPlateSection));
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

      private record CircleCsvRow
      {
         public string Tag    { get; init; } = "";
         public double X      { get; init; }
         public double Y      { get; init; }
         public double Radius { get; init; }
      }
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
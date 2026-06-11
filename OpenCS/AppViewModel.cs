using System.Collections.ObjectModel;
using System.Linq;

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
      /// Текущее выбранное поперечное сечение. При установке открывает CrossSectionView.
      /// </summary>
      public CrossSection? CurrentCrossSection
      {
         get => currentCrossSection;
         set
         {
            currentCrossSection = value;
            CurrentPage = value != null ? new Views.CrossSectionView(value, this) : null!;
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
      public ObservableCollection<CrossSection> FiberSectionsLive { get; set; } = [];

      /// <summary>Двухстадийные сечения (TwoStageSection).</summary>
      public ObservableCollection<CrossSection> TwoStageSectionsLive { get; set; } = [];

      /// <summary>Текущая выбранная MaterialArea. Открывает MaterialAreaPage.</summary>
      public MaterialArea? CurrentMaterialArea
      {
         get => currentMaterialArea;
         set
         {
            currentMaterialArea = value;
            if (value != null)
               CurrentPage = new Views.MaterialAreaPage(value, this);
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
         set { currentPage = value; OnPropertyChanged(); }
      }
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
         set { currentContour = value; CurrentPage = value != null ? new ContourPlot(this) : null!; OnPropertyChanged(); }
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
         if (File.Exists(tempPath)) File.Delete(tempPath);
         if (File.Exists(tempPath + "-wal")) File.Delete(tempPath + "-wal");
         if (File.Exists(tempPath + "-shm")) File.Delete(tempPath + "-shm");
         db.ChangeDatabase(tempPath);
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
         NewAreaCommand            = new RelayCommand(_ => NewArea());
         DeleteMaterialAreaCommand = new RelayCommand(_ => DeleteMaterialArea());
         NewRebarGroupCommand      = new RelayCommand(_ => { });  // Блок 3
         NewSingleBarCommand       = new RelayCommand(_ => { }); // Блок 3
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
         CurrentPage = new ContourPlot(this, false);
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

      void RefreshAfterLoad()
      {
         CurrentPage = null!;
         CurrentMaterial = null;
         CurrentContour = null;
         currentCrossSection = null;
         currentMaterialArea = null;
         OnPropertyChanged(nameof(CurrentCrossSection));
         OnPropertyChanged(nameof(CurrentMaterialArea));
         MaterialsSort();
         this.ContoursRenumber();
         CirclesLive = new(Circles); this.CirclesRenumber();
         DiagramsLive = [.. Diagrams];
         CrossSectionsLive = new(CrossSections); CrossSectionsRenumber();
         RefreshMaterialAreaLiveCollections();
         RefreshSectionLiveCollections();
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
         CurrentPage = new Views.CrossSectionPage(this);
      }

      void EditCrossSection()
      {
         if (currentCrossSection == null) return;
         CurrentPage = new Views.CrossSectionPage(currentCrossSection, this);
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
         FiberSectionsLive    = new(CrossSections.Where(s => s is not TwoStageSection));
         TwoStageSectionsLive = new(CrossSections.OfType<TwoStageSection>());
         OnPropertyChanged(nameof(FiberSectionsLive));
         OnPropertyChanged(nameof(TwoStageSectionsLive));
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
   }
}
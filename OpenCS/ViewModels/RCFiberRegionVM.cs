using CScore;


using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Модель представления армированной волоконной области (RCFiberRegion).
   /// Обеспечивает привязку данных доменного объекта <see cref="RCFiberRegion"/>
   /// к элементам управления WPF, управляет операциями разбиения на волокна,
   /// триангуляции, добавления/удаления групп арматуры и отверстий.
   /// Является центральным звеном для работы с армированными сечениями.
   /// </summary>
   public class RCFiberRegionVM : ViewModelBase
   {

      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к базе данных, сервисам логирования и коллекциям общих данных.
      /// </summary>
      AppViewModel mvm;

      /// <summary>
      /// Доменный объект армированной волоконной области. Содержит геометрию,
      /// материал бетона и коллекции групп арматурных стержней.
      /// </summary>
      public RCFiberRegion region = new();

      /// <summary>
      /// Ссылка на ListBox окружностей в представлении. Используется
      /// для получения выбранных элементов при переносе окружностей в арматурные стержни.
      /// </summary>
      internal ListBox CirclesListBox { get; set; }

      /// <summary>
      /// Ссылка на ListBox арматурных стержней в представлении. Используется
      /// для получения выбранных элементов при переносе стержней между группами.
      /// </summary>
      internal ListBox RebarsListBox { get; set; }

      /// <summary>
      /// Сервис построения графиков. Используется для визуализации контура,
      /// волокон, отверстий и арматурных стержней.
      /// </summary>
      public IPlotService PlotService { get; set; }

      /// <summary>
      /// Флаг, указывающий, сохранена ли область в базу данных.
      /// Используется для различения операций создания и обновления.
      /// </summary>
      internal bool IsSaved { get; set; }

      /// <summary>
      /// Флаг, указывающий, находится ли область в режиме редактирования
      /// (после разбиения на волокна, но до сохранения изменений).
      /// </summary>
      internal bool IsEdit { get; set; }


      /// <summary>
      /// Доменный объект армированной волоконной области. При установке значения
      /// заполняет все связанные свойства ViewModel (бетон, контур, волокна, отверстия,
      /// группы арматуры) из загруженного объекта и устанавливает флаг отрисовки.
      /// </summary>
      public RCFiberRegion Region
      {
         get { return region; }
         set
         {
            region = value;
            Concrete = value.Material;
            Tag = value.Tag;
            Out = value.Hull;
            SelectedContour = value.Hull;
            Fibers = value.Fibers != null ? [.. value.Fibers] : [];
            Holes = value.Holes != null ? [.. value.Holes] : [];
            RebarGroups = [.. value.ReBarGroups];
            if (value.ReBarGroups.Count > 0)
            {
               SelectedRebarGroup = value.ReBarGroups[0];
               TagGroup = value.ReBarGroups[0].Tag;
            }
            IsDraw = true;
            GeoProps = value.Props;
         }
      }

      /// <summary>
      /// Ссылка на главную ViewModel приложения. При установке значения
      /// загружает коллекции окружностей, контуров, бетонов и арматурных сталей
      /// из <see cref="AppViewModel"/>, а также конвертирует окружности в арматурные стержни.
      /// </summary>
      public AppViewModel MVM
      {
         get { return mvm; }
         set
         {
            mvm = value;
            Circles = value.Circles;
            Contours = [.. value.Contours];
            Concretes = value.Concretes;
            RFsteels = value.Armatures;
            CirclesToRebars();
         }
      }

      /// <summary>Коллекция окружностей, доступных для добавления в группу арматуры.</summary>
      ObservableCollection<CircleP> circles = [];

      /// <summary>Коллекция контуров, доступных для выбора в качестве внешнего контура или отверстия.</summary>
      ObservableCollection<Contour> contours = [];

      /// <summary>Коллекция контуров-отверстий внутри области.</summary>
      ObservableCollection<Contour> holes = [];

      /// <summary>Коллекция арматурных стержней текущей группы арматуры.</summary>
      ObservableCollection<ReBar> rebars = [];

      /// <summary>Коллекция всех арматурных стержней (не распределённых по группам).</summary>
      ObservableCollection<ReBar> allrebars = [];

      /// <summary>Коллекция материалов типа «Бетон», доступных для выбора.</summary>
      ObservableCollection<Material> concretes = [];

      /// <summary>Коллекция материалов типа «Арматурная сталь», доступных для выбора.</summary>
      ObservableCollection<Material> rfsteels = [];

      /// <summary>Коллекция групп арматурных стержней в данной области.</summary>
      ObservableCollection<ReBarGroup> rebarGroups = [];

      /// <summary>Коллекция волокон, полученных при разбиении области.</summary>
      ObservableCollection<Fiber> fibers = [];

      /// <summary>Выбранный контур (внешний или отверстие) в ListBox.</summary>
      Contour selectedContour;

      /// <summary>Внешний контур (оболочка) армированной области.</summary>
      Contour outc;

      /// <summary>Выбранная группа арматурных стержней в ListBox.</summary>
      ReBarGroup selectedRebarGroup = new() { Num = 1 };

      /// <summary>Выбранный арматурный стержень в ListBox.</summary>
      ReBar rebar = new();

      /// <summary>Выбранная окружность в ListBox.</summary>
      CircleP circle = new();

      /// <summary>Геометрические свойства сечения (площадь, моменты инерции и т.д.).</summary>
      GeoProps geoProps;

      /// <summary>Счётчик для нумерации новых групп арматуры.</summary>
      int ig = 2;

      /// <summary>Флаг: используется ли метод разбиения перпендикулярными сечениями (Slice).</summary>
      bool isPerpendicular = true;

      /// <summary>Флаг: используется ли метод триангуляции (Delaney).</summary>
      bool isTriagulate = false;

      /// <summary>Флаг: нужно ли перерисовать график при изменении данных.</summary>
      bool isDraw;

      /// <summary>Выбранный материал арматурной стали для текущей группы.</summary>
      private Material rfsteel;

      /// <summary>Наименование (тег) текущей группы арматуры.</summary>
      string tagGroup;

      /// <summary>Описание текущей группы арматуры.</summary>
      string descriptionGroup;

      /// <summary>
      /// Геометрические свойства сечения (площадь, моменты инерции, центр тяжести).
      /// Вычисляются после разбиения области на волокна.
      /// </summary>
      public GeoProps GeoProps
      {
         get { return geoProps; }
         set { geoProps = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Максимальное отношение площади треугольника к минимальному углу при триангуляции.
      /// Ограничено диапазоном [10; 30]. При изменении автоматически запускает триангуляцию.
      /// </summary>
      public double Antr
      {
         get { return region.Antr; }
         set
         {
            if (value < 10) { region.Antr = 10; OnPropertyChanged(); }
            else if (value > 30) { region.Antr = 30; OnPropertyChanged(); }
            else { region.Antr = value; OnPropertyChanged(); }
            Triangulate();
         }
      }

      /// <summary>
      /// Отношение площади треугольника к минимальному углу при триангуляции (Antr).
      /// Ограничено диапазоном [0.05; 1]. При изменении автоматически запускает триангуляцию.
      /// </summary>
      public double Atr
      {
         get { return region.Atr; }
         set
         {
            if (value < 0.05) { region.Atr = 0.05; OnPropertyChanged(); }
            else if (value > 1) { region.Atr = 1; OnPropertyChanged(); }
            else { region.Atr = value; OnPropertyChanged(); }
            Triangulate();
         }
      }

      /// <summary>
      /// Количество разбиений по оси Y при делении области перпендикулярными сечениями.
      /// Ограничено диапазоном [1; 100]. При изменении автоматически запускает разбиение.
      /// </summary>
      public int Dy
      {
         get { return region.NY; }
         set
         {
            if (value < 1) { region.NY = 1; OnPropertyChanged(); }
            else if (value > 100) { region.NY = 100; OnPropertyChanged(); }
            else { region.NY = value; OnPropertyChanged(); }
            Slice();
         }
      }

      /// <summary>
      /// Количество разбиений по оси X при делении области перпендикулярными сечениями.
      /// Ограничено диапазоном [1; 100]. При изменении автоматически запускает разбиение.
      /// </summary>
      public int Dx
      {
         get { return region.NX; }
         set
         {
            if (value < 1) { region.NX = 1; OnPropertyChanged(); }
            else if (value > 100) { region.NX = 100; OnPropertyChanged(); }
            else { region.NX = value; OnPropertyChanged(); }
            Slice();
         }
      }

      /// <summary>
      /// Флаг использования метода разбиения перпендикулярными сечениями (Slice).
      /// Используется в привязке для переключения метода разбиения.
      /// </summary>
      public bool IsPerpendicular
      {
         get { return isPerpendicular; }
         set { isPerpendicular = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг использования метода триангуляции (Delaney) для разбиения области.
      /// Используется в привязке для переключения метода разбиения.
      /// </summary>
      public bool IsTriagulate
      {
         get { return isTriagulate; }
         set { isTriagulate = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг необходимости отрисовки области на графике. При установке значения
      /// в true автоматически вызывается <see cref="PlotUpdate"/>.
      /// </summary>
      public bool IsDraw
      {
         get { return isDraw; }
         set { isDraw = value; if (value) PlotUpdate(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Наименование (тег) текущей группы арматуры. При изменении обновляет
      /// тег в объекте <see cref="ReBarGroup"/> и пересчитывает описание группы.
      /// </summary>
      public string TagGroup
      {
         get { return tagGroup; }
         set
         {
            tagGroup = value;
            selectedRebarGroup.Tag = value;
            DescriptionGroup = selectedRebarGroup.ToString();
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Описание текущей группы арматуры. При изменении обновляет
      /// описание в объекте <see cref="ReBarGroup"/>.
      /// </summary>
      public string DescriptionGroup
      {
         get { return descriptionGroup; }
         set
         {
            descriptionGroup = value;
            selectedRebarGroup.Description = value;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Наименование (тег) армированной области. Проксирует <see cref="RCFiberRegion.Tag"/>
      /// с уведомлением об изменении и пересчётом описания.
      /// </summary>
      public string Tag
      {
         get { return region.Tag; }
         set
         {
            region.Tag = value;
            Description = region.ToString();
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Описание армированной области. Проксирует <see cref="RCFiberRegion.Description"/>
      /// с уведомлением об изменении.
      /// </summary>
      public string Description
      {
         get { return region.Description; }
         set { region.Description = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Материал бетона армированной области. Проксирует <see cref="RCFiberRegion.Material"/>
      /// с пересчётом описания при изменении. Используется для привязки в ComboBox выбора бетона.
      /// </summary>
      public Material Concrete
      {
         get { return region.Material; }
         set { region.Material = value; Description = region.ToString(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранный материал арматурной стали. Используется для привязки
      /// в ComboBox выбора арматурной стали для текущей группы.
      /// </summary>
      public Material RFsteel
      {
         get { return rfsteel; }
         set { rfsteel = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранная окружность в ListBox. Используется для добавления
      /// окружности как арматурного стержня в группу арматуры.
      /// </summary>
      public CircleP Circle
      {
         get { return circle; }
         set { circle = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранный арматурный стержень в ListBox. Используется для удаления
      /// стержня из группы арматуры или переноса между группами.
      /// </summary>
      public ReBar Rebar
      {
         get { return rebar; }
         set { rebar = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранный контур в ListBox контуров. Используется для назначения
      /// внешнего контура или добавления отверстия.
      /// </summary>
      public Contour SelectedContour
      {
         get { return selectedContour; }
         set { selectedContour = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Внешний контур (оболочка) армированной области. При установке значения
      /// обновляет WKT-геометрию, очищает волокна и привязывает область к контуру.
      /// </summary>
      public Contour Out
      {
         get { return outc; }
         set
         {
            outc = value;
            if (value != null)
            {
               region.Hull = value;
               region.SetWKT();

               if (Tag == "" || Tag == null) Tag = value.GeometrySet;
               TagGroup = selectedRebarGroup != null ? $"{Tag}-gr{selectedRebarGroup.Num}" : "";
               try
               {
                  mvm.db.RemoveFibers(region.Fibers, region);

                  Fibers.Clear();
               }
               catch
               {
                  Fibers.Clear();
               }
               region.Hull.Regions.Add(region);
            }
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Выбранная группа арматурных стержней в ListBox. При изменении
      /// загружает стержни выбранной группы и обновляет описание.
      /// </summary>
      public ReBarGroup SelectedRebarGroup
      {
         get { return selectedRebarGroup; }
         set
         {
            selectedRebarGroup = value;
            if (value != null) { Rebars = [.. value.ReBars]; DescriptionGroup = value.ToString(); };
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Коллекция волокон, полученных при разбиении области. Привязана к ListBox
      /// волокон в представлении.
      /// </summary>
      public ObservableCollection<Fiber> Fibers
      {
         get { return fibers; }
         set { fibers = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция материалов арматурной стали, доступных для выбора.
      /// Привязана к ComboBox выбора арматурной стали.
      /// </summary>
      public ObservableCollection<Material> RFsteels
      {
         get { return rfsteels; }
         set { rfsteels = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция материалов бетона, доступных для выбора.
      /// Привязана к ComboBox выбора бетона.
      /// </summary>
      public ObservableCollection<Material> Concretes
      {
         get { return concretes; }
         set { concretes = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция окружностей, доступных для добавления в группу арматуры.
      /// Привязана к ListBox окружностей в представлении.
      /// </summary>
      public ObservableCollection<CircleP> Circles
      {
         get { return circles; }
         set { circles = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция контуров-отверстий внутри армированной области.
      /// </summary>
      public ObservableCollection<Contour> Holes
      {
         get { return holes; }
         set { holes = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция контуров, доступных для выбора в качестве внешнего контура
      /// или отверстия. Привязана к ListBox контуров в представлении.
      /// </summary>
      public ObservableCollection<Contour> Contours
      {
         get { return contours; }
         set { contours = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция арматурных стержней текущей группы. Привязана к ListBox
      /// арматурных стержней в представлении.
      /// </summary>
      public ObservableCollection<ReBar> Rebars
      {
         get { return rebars; }
         set { rebars = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция всех арматурных стержней (не распределённых по группам).
      /// Привязана к ListBox всех стержней в представлении.
      /// </summary>
      public ObservableCollection<ReBar> AllRebars
      {
         get { return allrebars; }
         set { allrebars = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция групп арматурных стержней данной области.
      /// Привязана к ListBox групп в представлении.
      /// </summary>
      public ObservableCollection<ReBarGroup> RebarGroups
      {
         get { return rebarGroups; }
         set { rebarGroups = value; OnPropertyChanged(); }
      }

      /// <summary>Команда привязки для импорта геометрии из DXF-файла.</summary>
      public ICommand FromDxfCommand { get; set; }

      /// <summary>Команда привязки для сохранения армированной области в базу данных.</summary>
      public ICommand SaveCommand { get; set; }

      /// <summary>Команда привязки для создания новой армированной области.</summary>
      public ICommand NewCommand { get; set; }

      /// <summary>Команда привязки для добавления новой группы арматурных стержней.</summary>
      public ICommand AddRebarGroupCommand { get; set; }

      /// <summary>Команда привязки для добавления нового материала в проект.</summary>
      public ICommand AddMaterialCommand { get; set; }

      /// <summary>Команда привязки для добавления контура в список отверстий области.</summary>
      public ICommand HoleInCommand { get; set; }

      /// <summary>Команда привязки для удаления контура из списка отверстий области.</summary>
      public ICommand HoleOutCommand { get; set; }

      /// <summary>Команда привязки для переноса выбранного арматурного стержня в текущую группу.</summary>
      public ICommand RebarInCommand { get; set; }

      /// <summary>Команда привязки для переноса всех арматурных стержней в текущую группу.</summary>
      public ICommand RebarsInCommand { get; set; }

      /// <summary>Команда привязки для удаления арматурного стержня из текущей группы.</summary>
      public ICommand RebarOutCommand { get; set; }

      /// <summary>Команда привязки для обновления графика области.</summary>
      public ICommand PlotUpdateCommand { get; set; }

      /// <summary>Команда привязки для разбиения области перпендикулярными сечениями (Slice).</summary>
      public ICommand SliceCommand { get; set; }

      /// <summary>Команда привязки для триангуляции области (Delaney).</summary>
      public ICommand TriangulateCommand { get; set; }

      /// <summary>Команда привязки для конвертации окружностей в арматурные стержни.</summary>
      public ICommand RebarsUpdCommand { get; set; }

      /// <summary>Команда привязки для обновления списка контуров.</summary>
      public ICommand ContoursUpdCommand { get; set; }

      /// <summary>Команда привязки для обновления (сохранения изменений) армированной области.</summary>
      public ICommand UpdateCommand { get; set; }

      /// <summary>Команда привязки для назначения материала арматурной стали текущей группе.</summary>
      public ICommand RFsteelInCommand { get; set; }

      /// <summary>Команда привязки для разбиения области (вызывает Slice или Triangulate в зависимости от режима).</summary>
      public ICommand SplitCommand { get; set; }

      /// <summary>
      /// Инициализирует экземпляр <see cref="RCFiberRegionVM"/> с одной группой арматуры
      /// по умолчанию и создаёт все команды привязки.
      /// </summary>
      public RCFiberRegionVM()
      {

         RebarGroups = [selectedRebarGroup];
         region.ReBarGroups.Add(selectedRebarGroup);

         FromDxfCommand = new RelayCommand(FromDxf);
         SaveCommand = new RelayCommand(Save);
         AddRebarGroupCommand = new RelayCommand(AddRebarGroup);
         AddMaterialCommand = new RelayCommand(AddMaterial);
         HoleInCommand = new RelayCommand(HoleIn);
         HoleOutCommand = new RelayCommand(HoleOut);
         RebarInCommand = new RelayCommand(RebarIn);
         RebarsInCommand = new RelayCommand(RebarsIn);
         RebarOutCommand = new RelayCommand(RebarOut);
         PlotUpdateCommand = new RelayCommand(PlotUpdate);
         SliceCommand = new RelayCommand(Slice);
         TriangulateCommand = new RelayCommand(Triangulate);
         RebarsUpdCommand = new RelayCommand(CirclesToRebars);
         NewCommand = new RelayCommand(New);
         UpdateCommand = new RelayCommand(Update);
         RFsteelInCommand = new RelayCommand(RFsteelIn);
         ContoursUpdCommand = new RelayCommand(ContoursUpd);
         SplitCommand = new RelayCommand(Split);
      }

      /// <summary>
      /// Назначает выбранный материал арматурной стали текущей группе арматуры
      /// и обновляет описание группы.
      /// </summary>
      void RFsteelIn(object? o = null)
      {
         SelectedRebarGroup.Material = rfsteel;
         DescriptionGroup = selectedRebarGroup.ToString();
         int idx = rebarGroups.IndexOf(selectedRebarGroup);
         SelectedRebarGroup = new(); SelectedRebarGroup = null;
         SelectedRebarGroup = rebarGroups[idx];
      }

      /// <summary>
      /// Сбрасывает ViewModel в начальное состояние: создаёт новую пустую область,
      /// очищает все коллекции и устанавливает <see cref="IsSaved"/> в false.
      /// </summary>
      void New(object? o = null)
      {
         ig = 2;
         Region = new RCFiberRegion();
         Concrete = new(); Concrete = null;
         RFsteel = new(); RFsteel = null;
         SelectedContour = new(); SelectedContour = null;
         SelectedRebarGroup = new() { Num = 1 };
         RebarGroups = [selectedRebarGroup];
         region.ReBarGroups = [.. rebarGroups];
         Rebars = [];
         IsSaved = false;
      }

      /// <summary>
      /// Обновляет существующую армированную область в базе данных.
      /// Проверяет наличие волокон и флаг сохранения перед обновлением.
      /// </summary>
      void Update(object? o = null)
      {
         if (Fibers.Count == 0 || region.Fibers == null)
         {
            mvm.LogService.Error($"Область не разделена на элементарные площадки. Область '{region.Tag}' не изменена");
            return;
         }
         if (!IsSaved)
         {
            mvm.LogService.Error($"Сначала нужно сохранить область. Область '{region.Tag}' не изменена");
         }
         else
         {
            if (IsEdit)
            {
               mvm.db.RemoveFibers(region.Fibers, region);
               region.Fibers = [.. fibers];
               GeoProps = region.Props;
            }

            mvm.db.SaveRCFiberRegion(region);
            IsEdit = false;

            mvm.LogService.Info($"Область '{region.Tag}' успешно изменена");
            mvm.RCFiberRegionsRenumber();
         }
      }

      /// <summary>
      /// Добавляет новую группу арматурных стержней в область с автоматической нумерацией.
      /// </summary>
      void AddRebarGroup(object? o)
      {
         var rg = new ReBarGroup();
         RebarGroups.Add(rg);
         SelectedRebarGroup = rg;
         TagGroup = $"{Tag}-gr{ig}";
         rg.Num = ig; ig++;
         Rebars.Clear();
         region.ReBarGroups.Add(rg);
         rg.RCFiberRegion = region;
      }

      /// <summary>
      /// Открывает диалоговое окно импорта геометрии из DXF-файла.
      /// После закрытия диалога конвертирует окружности в арматурные стержни.
      /// </summary>
      void FromDxf(object? o)
      {
         FromDxfWindow window = new(MVM);
         if (window.ShowDialog() != true)
         {
            CirclesToRebars();
         }
      }

      /// <summary>
      /// Переносит выбранный арматурный стержень (или несколько стержней) из списка
      /// всех стержней в текущую группу арматуры.
      /// </summary>
      private void RebarIn(object? o = null)
      {
         if (selectedRebarGroup == null) return;
         if (circle == null) return;

         if (CirclesListBox.SelectedItems.Count == 0) return;
         else if (CirclesListBox.SelectedItems.Count > 1)
         {
            List<ReBar> si = []; int ri = selectedRebarGroup.ReBars.Count + 1;
            foreach (var item in CirclesListBox.SelectedItems)
            {
               ReBar rb = (ReBar)item; rb.Num = ri;
               rb.Group = selectedRebarGroup;
               si.Add(rb);
               ri++;
            }
            CirclesListBox.SelectedItems.Clear();
            CirclesListBox.SelectedIndex = -1;
            foreach (var item in si)
            {
               Rebars.Add(item);
               selectedRebarGroup.ReBars.Add(item);
               item.Group = selectedRebarGroup;
            }
            AllRebars.RemoveRange(si);
         }
         else
         {
            Rebars.Add(rebar);
            SelectedRebarGroup.ReBars.Add(rebar);
            rebar.Group = selectedRebarGroup;
            AllRebars.Remove(rebar);
            CirclesListBox.SelectedIndex = -1;
            RebarsListBox.SelectedIndex = -1;
         }

      }

      /// <summary>
      /// Переносит выбранный арматурный стержень (или несколько стержней) из текущей
      /// группы арматуры обратно в список всех стержней.
      /// </summary>
      private void RebarOut(object? o = null)
      {
         if (selectedRebarGroup == null) return;
         if (rebar == null) return;

         if (RebarsListBox.SelectedItems.Count == 0) return;
         else if (RebarsListBox.SelectedItems.Count > 1)
         {

            List<ReBar> si = [];
            foreach (var item in RebarsListBox.SelectedItems)
            {
               ReBar rb = (ReBar)item; rb.Group = null;
               si.Add(rb);
            }
            RebarsListBox.SelectedItems.Clear();
            RebarsListBox.SelectedIndex = -1;
            foreach (var item in si)
            {
               AllRebars.Add(item);
               Rebars.Remove(item);
               selectedRebarGroup.ReBars.Remove(item);
               item.Group = null;
            }
         }
         else
         {
            AllRebars.Add(rebar);
            rebar.Group = null;
            SelectedRebarGroup.ReBars.Remove(rebar);
            Rebars.Remove(rebar);
            RebarsListBox.SelectedIndex = -1;
            CirclesListBox.SelectedIndex = -1;
         }

      }

      /// <summary>
      /// Переносит все арматурные стержни из списка окружностей в текущую группу.
      /// </summary>
      private void RebarsIn(object? o = null)
      {
         if (selectedRebarGroup == null) return;
         if (Circles == null) return;
         if (Circles.Count == 0) return;

         if (CirclesListBox.SelectedItems.Count == 0)
         {
            List<ReBar> si = []; int i = selectedRebarGroup.ReBars.Count + 1;
            foreach (var item in CirclesListBox.Items)
            {
               ReBar rb = (ReBar)item;
               rb.Group = selectedRebarGroup;
               rb.Num = i;
               si.Add(rb);
               i++;
            }
            foreach (var item in si)
            {
               Rebars.Add(item);
               selectedRebarGroup.ReBars.Add(item);
               AllRebars.Remove(item);
            }
         }
      }

      /// <summary>
      /// Открывает диалоговое окно создания нового материала.
      /// </summary>
      private void AddMaterial(object? o = null)
      {
         MaterialWindow window = new(new Material(), MVM);
         window.ShowDialog();
      }

      /// <summary>
      /// Добавляет выбранный контур в список отверстий области.
      /// Контур не должен быть типа Hull (внешний контур).
      /// </summary>
      private void HoleIn(object? o = null)
      {
         if (selectedContour == null) return;
         if (selectedContour.Type == ContourType.Hull) return;
         if (holes.Contains(selectedContour)) return;
         selectedContour.Type = ContourType.Hole;
         Holes.Add(selectedContour);
         region.Contours.Add(selectedContour);
         selectedContour.Regions.Add(region);
         Contours.Remove(selectedContour);
         region.SetWKT(); Fibers.Clear();
      }

      /// <summary>
      /// Удаляет выбранный контур из списка отверстий области
      /// и возвращает его в общий список контуров.
      /// </summary>
      private void HoleOut(object? o = null)
      {
         if (selectedContour == null) return;
         if (!holes.Contains(selectedContour)) return;
         region.Contours.Remove(selectedContour);
         selectedContour.Regions.Remove(region);
         selectedContour.Type = ContourType.Hull;
         Holes.Remove(selectedContour);
         region.SetWKT(); Fibers.Clear();
      }

      /// <summary>
      /// Сохраняет армированную область в базу данных. Выполняет валидацию:
      /// проверяет наличие внешнего контура, материала, групп арматуры и волокон.
      /// При успешном сохранении устанавливает <see cref="IsSaved"/> в true.
      /// </summary>
      private void Save(object? o = null)
      {
         if (IsSaved)
         {
            mvm.LogService.Warning($"Область '{region.Tag}' уже сохранена");
            return;
         }
         if (region.Hull == null)
         {
            mvm.LogService.Error("Не задан внешний контур области. Область не сохранена");
            return;
         }
         if (region.Material == null)
         {
            mvm.LogService.Error("Не задан материал области. Область не сохранена");
            return;
         }
         if (region.ReBarGroups == null)
         {
            mvm.LogService.Error("Не задано ни одной группы армирования области. Область не сохранена");
            return;
         }
         if (region.ReBarGroups.Count == 0)
         {
            mvm.LogService.Error("Не задано ни одной группы армирования области. Область не сохранена");
            return;
         }
         if (Fibers == null)
         {
            mvm.LogService.Error("Область не разделена на элементарные площадки. Область не сохранена");
            return;
         }
         if (Fibers.Count == 0)
         {
            mvm.LogService.Error("Область не разделена на элементарные площадки. Область не сохранена");
            return;
         }
         foreach (var group in region.ReBarGroups)
            if (group.Material == null)
            {
               mvm.LogService.Error("Для одной из групп арматурных стержней не задан материал. Область не сохранена");
               return;
            }
         foreach (var group in region.ReBarGroups)
            if (group.ReBars == null)
            {
               mvm.LogService.Error("Для одной из групп арматурных стержней не задано ни одного арматурного стержня. Область не сохранена");
               return;
            }
         foreach (var group in region.ReBarGroups)
            if (group.ReBars.Count == 0)
            {
               mvm.LogService.Error("Для одной из групп арматурных стержней не задано ни одного арматурного стержня. Область не сохранена");
               return;
            }

         region.Fibers = [.. Fibers];
         GeoProps = region.Props;
         mvm.db.SaveRCFiberRegion(region);
         IsSaved = true;
         IsEdit = false;

         mvm.LogService.Info($"Армированная область '{region.Tag}' успешно сохранена");
         mvm.RCFiberRegionsRenumber();
      }

      /// <summary>
      /// Обновляет отрисовку области на графике: рисует волокна, внешний контур,
      /// отверстия и арматурные стержни с использованием <see cref="IPlotService"/>.
      /// </summary>
      void PlotUpdate(object? o = null)
      {
         if (PlotService == null) return;
         PlotService.Clear();
         ColorsCS clrs = new();

         if (Out == null) return;

         if (fibers.Count > 0)
         {
            NetTopologySuite.IO.WKTReader reader = new();
            foreach (var item in fibers)
            {
               NetTopologySuite.Geometries.Polygon p =
                  (NetTopologySuite.Geometries.Polygon)reader.Read(item.WKT);
               var crds = p.Coordinates;
               double[] xs = new double[crds.Length - 1];
               double[] ys = new double[crds.Length - 1];
               for (int j = 0; j < crds.Length - 1; j++)
               {
                  xs[j] = crds[j].X;
                  ys[j] = crds[j].Y;
               }
               PlotService.AddPolygon(xs, ys, fillColor: "#F0EACD50", lineColor: "#8C92AC");
            }
         }

         Out.PointsToXYs();
         PlotService.AddScatter(Out.X.ToArray(), Out.Y.ToArray(), lineWidth: 2, color: clrs[0]);

         if (holes.Count > 0)
         {
            foreach (var item in holes)
            {
               item.PointsToXYs();
               PlotService.AddScatter(item.X.ToArray(), item.Y.ToArray(), lineWidth: 2, color: clrs[0]);
            }
         }

         int i = 6;
         if (rebarGroups != null)
         {
            foreach (var group in rebarGroups)
            {
               foreach (var rb in group.ReBars)
               {
                  PlotService.AddCircle(rb.X, rb.Y, 0.5 * rb.Diameter, fillColor: clrs[i], lineColor: clrs[0]);
               }
               i++;
            }
         }

         PlotService.EnableSquareAxes();
         PlotService.AutoScale();
         PlotService.Refresh();
      }

      /// <summary>
      /// Выполняет триангуляцию области с использованием параметров <see cref="Atr"/>
      /// и <see cref="Antr"/>. Обновляет коллекцию волокон и отрисовывает результат.
      /// </summary>
      void Triangulate(object? o = null)
      {
         if (region.Hull == null)
         {
            mvm.LogService.Error("Не задан внешний контур области. Невозможно триангулировать область");
            return;
         }
         IsPerpendicular = false; IsTriagulate = true;
         Fiber[] res = Geo.Triangulation(region, Atr, Antr);
         Fibers = [.. res];

         if (IsEdit)
         {
            region.Fibers = [.. fibers];
            PlotUpdate();
         }
         else
         {
            mvm.db.RemoveFibers(region.Fibers, region);
            IsEdit = true;
            region.Fibers = [.. fibers];
            PlotUpdate();
         }
      }

      /// <summary>
      /// Выполняет разбиение области перпендикулярными сечениями с параметрами
      /// <see cref="Dx"/> и <see cref="Dy"/>. Обновляет коллекцию волокон и отрисовывает результат.
      /// </summary>
      void Slice(object? o = null)
      {
         if (region.Hull == null)
         {
            mvm.LogService.Error("Не задан внешний контур области. Невозможно разделить область");
            return;
         }
         IsPerpendicular = true; IsTriagulate = false;
         if (Dx > 1 && Dy > 1)
         {
            Fiber[] res = Geo.SliceXY(region, Dx, Dy);
            Fibers = [.. res];
         }
         if (Dx == 1 && Dy > 1)
         {
            Fiber[] res = Geo.SliceY(region, Dy);
            Fibers = [.. res];
         }
         if (Dx > 1 && Dy == 1)
         {
            Fiber[] res = Geo.SliceX(region, Dx);
            Fibers = [.. res];
         }

         IsEdit = true;
         PlotUpdate();
      }

      /// <summary>
      /// Вызывает разбиение области в зависимости от текущего режима:
      /// <see cref="IsPerpendicular"/> — Slice, иначе — <see cref="Triangulate"/>.
      /// </summary>
      void Split(object? o = null)
      {
         if (isPerpendicular) Slice();
         else Triangulate();
         PlotUpdate();
      }

      /// <summary>
      /// Обновляет описания области и текущей группы арматуры,
      /// а также назначает материал арматурной стали выбранной группе.
      /// </summary>
      void Upd()
      {
         Description = region.ToString();
         selectedRebarGroup.Tag = TagGroup;
         DescriptionGroup = selectedRebarGroup.ToString();
         if (rfsteel != null)
            SelectedRebarGroup.Material = rfsteel;
      }

      /// <summary>
      /// Конвертирует все окружности из главной ViewModel в арматурные стержни
      /// и заполняет коллекцию <see cref="AllRebars"/>.
      /// </summary>
      void CirclesToRebars(object? o = null)
      {
         if (mvm.Circles == null || mvm.Circles.Count == 0) return;

         AllRebars = [];
         foreach (var item in mvm.Circles)
         {
            ReBar re = new(item)
            {
               Tag = $"{item.Tag} [{item.GeometrySet}]",
               Num = item.Num
            };
            AllRebars.Add(re);
         }
      }

      /// <summary>
      /// Обновляет список контуров из главной ViewModel и сбрасывает типы контуров.
      /// </summary>
      void ContoursUpd(object? o = null)
      {
         Contours = [.. mvm.Contours];
         foreach (var item in contours)
            item.Type = ContourType.None;
         region.Contours = [];
         SelectedContour = new();
         SelectedContour = null;
      }

      /// <summary>
      /// Создаёт список ViewModel контуров для всех контуров, привязанных к данной области.
      /// </summary>
      List<ContourVM> ContoursVMs()
      {
         var res = new List<ContourVM>(region.Contours.Count);
         foreach (var item in region.Contours)
            res.Add(new ContourVM() { Contour = item});

         return res;
      }

      /// <summary>
      /// Список ViewModel контуров, привязанных к данной области.
      /// Вызывает <see cref="ContoursVMs"/> для формирования списка.
      /// </summary>
      public List<ContourVM> ContourVMs { get => ContoursVMs();}

      /// <summary>Выбранная ViewModel контура в ListBox.</summary>
      ContourVM selectedContourVM;

      /// <summary>
      /// Выбранная ViewModel контура. При изменении синхронизирует выбранный
      /// доменный контур в <see cref="SelectedContour"/>.
      /// </summary>
      public ContourVM SelectedContourVM { get => selectedContourVM; set { selectedContourVM = value; SelectedContour = value.Contour; OnPropertyChanged(); } }
   }
}
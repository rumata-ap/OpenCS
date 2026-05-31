using CScore;

using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;

using netDxf;
using netDxf.Entities;

using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Модель представления для импорта геометрии из DXF-файлов. Обеспечивает загрузку
   /// DXF-документа, выбор контуров и окружностей для добавления в проект,
   /// а также управление масштабом и единицами измерения. Работает в связке
   /// с представлением <see cref="FromDxfPage"/>.
   /// </summary>
   public class FromDxfVM : ViewModelBase
   {
      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к базе данных, сервису логирования и коллекциям общих данных.
      /// </summary>
      public AppViewModel mvm;

      /// <summary>Масштабный коэффициент для преобразования координат DXF в проектные единицы.</summary>
      double scale = 0.001;

      /// <summary>Индекс выбранной единицы измерения (0=мм, 1=см, 2=м).</summary>
      int unitIdx = 0;

      /// <summary>Имя набора геометрии, получаемое из имени DXF-файла.</summary>
      string geometrySet = "dxf";

      /// <summary>Коллекция контуров, загруженных из DXF-файла.</summary>
      ObservableCollection<Contour> contours = [];

      /// <summary>Коллекция контуров, выбранных для добавления в проект.</summary>
      ObservableCollection<Contour> contoursPrj = [];

      /// <summary>Коллекция полилиний, загруженных из DXF-файла.</summary>
      ObservableCollection<Polyline2D> plines = [];

      /// <summary>Коллекция окружностей, загруженных из DXF-файла.</summary>
      ObservableCollection<CScore.CircleP> circles = [];

      /// <summary>Коллекция окружностей, выбранных для добавления в проект.</summary>
      ObservableCollection<CScore.CircleP> circlesPrj = [];

      /// <summary>Коллекция имён слоёв DXF-документа.</summary>
      ObservableCollection<string> layers = [];

      /// <summary>Коллекция контуров-образцов (не используется в текущей реализации).</summary>
      ObservableCollection<Contour> master = [];

      /// <summary>Выбранный контур в ListBox контуров DXF.</summary>
      Contour? selectedContour;

      /// <summary>Выбранная окружность в ListBox окружностей DXF.</summary>
      CircleP? selectedCircle;

      /// <summary>Текущий элемент управления для отображения DXF-чертежа.</summary>
      UserControl? currentPlot;

      /// <summary>
      /// Ссылка на ListBox контуров в представлении. Используется для получения
      /// выбранных элементов при переносе контуров в проект.
      /// </summary>
      internal ListBox ContoursListBox {  get; set; }

      /// <summary>
      /// Ссылка на ListBox окружностей в представлении. Используется для получения
      /// выбранных элементов при переносе окружностей в проект.
      /// </summary>
      internal ListBox CirclesListBox {  get; set; }

      /// <summary>
      /// Список единиц измерения для ComboBox: мм, см, м.
      /// Используется для привязки в представлении.
      /// </summary>
      public List<string> Units { get; set; } = ["мм", "см", "м"];

      /// <summary>
      /// Загруженный DXF-документ. Содержит геометрию для импорта.
      /// </summary>
      public DxfDocument? Dxf { get; set; }

      /// <summary>
      /// Индекс выбранной единицы измерения. При изменении автоматически
      /// пересчитывает масштабный коэффициент (мм=0.001, см=0.01, м=1).
      /// </summary>
      public int UnitIdx
      {
         get { return unitIdx; }
         set
         {
            unitIdx = value;
            if (value == 0) scale = 0.001;
            else if (value == 1) scale = 0.01;
            else scale = 1;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Имя набора геометрии, обычно совпадающее с имен DXF-файла.
      /// Используется для идентификации происхождения контуров и окружностей.
      /// </summary>
      public string GeometrySet
      {
         get { return geometrySet; }
         set { geometrySet = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Текущий элемент управления для отображения DXF-чертежа.
      /// Привязан к ContentControl в представлении.
      /// </summary>
      public UserControl? CurrentPlot
      {
         get { return currentPlot; }
         set { currentPlot = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранный контур в ListBox. Используется для переноса
      /// отдельного контура в проект или из проекта.
      /// </summary>
      public Contour? SelectedContour
      {
         get { return selectedContour; }
         set { selectedContour = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранная окружность в ListBox. Используется для переноса
      /// отдельной окружности в проект или из проекта.
      /// </summary>
      public CScore.CircleP? SelectedCircle
      {
         get { return selectedCircle; }
         set { selectedCircle = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция контуров, загруженных из DXF-файла. Привязана к ListBox
      /// контуров в представлении.
      /// </summary>
      public ObservableCollection<Contour> Contours
      {
         get { return contours; }
         set { contours = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция контуров, выбранных для добавления в проект. Привязана
      /// к ListBox выбранных контуров в представлении.
      /// </summary>
      public ObservableCollection<Contour> ContoursPrj
      {
         get { return contoursPrj; }
         set { contoursPrj = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция окружностей, загруженных из DXF-файла. Привязана к ListBox
      /// окружностей в представлении.
      /// </summary>
      public ObservableCollection<CScore.CircleP> Circles
      {
         get { return circles; }
         set { circles = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Коллекция окружностей, выбранных для добавления в проект. Привязана
      /// к ListBox выбранных окружностей в представлении.
      /// </summary>
      public ObservableCollection<CScore.CircleP> CirclesPrj
      {
         get { return circlesPrj; }
         set { circlesPrj = value; OnPropertyChanged(); }
      }

      /// <summary>Команда привязки для открытия DXF-файла через диалог.</summary>
      public ICommand OpenDXFCommand { get; set; }

      /// <summary>Команда привязки для сохранения выбранных контуров в базу данных проекта.</summary>
      public ICommand SaveContoursCommand { get; set; }

      /// <summary>Команда привязки для сохранения выбранных окружностей в базу данных проекта.</summary>
      public ICommand SaveCirclesCommand { get; set; }

      /// <summary>Команда привязки для переноса выбранного контура в список для добавления в проект.</summary>
      public ICommand ContourInCommand { get; set; }

      /// <summary>Команда привязки для переноса всех контуров в список для добавления в проект.</summary>
      public ICommand ContoursInCommand { get; set; }

      /// <summary>Команда привязки для возврата выбранного контура из проекта обратно в список DXF.</summary>
      public ICommand ContourOutCommand { get; set; }

      /// <summary>Команда привязки для переноса выбранной окружности в список для добавления в проект.</summary>
      public ICommand CircleInCommand { get; set; }

      /// <summary>Команда привязки для переноса всех окружностей в список для добавления в проект.</summary>
      public ICommand CirclesInCommand { get; set; }

      /// <summary>Команда привязки для возврата выбранной окружности из проекта обратно в список DXF.</summary>
      public ICommand CircleOutCommand { get; set; }

      /// <summary>
      /// Инициализирует экземпляр <see cref="FromDxfVM"/> и создаёт все команды привязки.
      /// </summary>
      public FromDxfVM()
      {
         OpenDXFCommand = new RelayCommand(OpenDxf);
         SaveCirclesCommand = new RelayCommand(SaveCircles);
         SaveContoursCommand = new RelayCommand(SaveContours);
         CircleInCommand = new RelayCommand(CircleIn);
         CirclesInCommand = new RelayCommand(CirclesIn);
         CircleOutCommand = new RelayCommand(CircleOut);
         ContourInCommand = new RelayCommand(ContourIn);
         ContoursInCommand = new RelayCommand(ContoursIn);
         ContourOutCommand = new RelayCommand(ContourOut);
      }

      /// <summary>
      /// Переносит выбранную окружность из списка DXF в список для добавления в проект.
      /// </summary>
      private void CircleIn(object? o = null)
      {
         if (selectedCircle == null) return;
         CirclesPrj.Add(selectedCircle);
         Circles.Remove(selectedCircle);
      }

      /// <summary>
      /// Переносит все окружности из списка DXF в список для добавления в проект.
      /// </summary>
      private void CirclesIn(object? o = null)
      {
         if (Circles == null) return;
         CirclesPrj = new(Circles);
         Circles.Clear();
      }

      /// <summary>
      /// Возвращает выбранную окружность из списка проекта обратно в список DXF.
      /// </summary>
      private void CircleOut(object? o = null)
      {
         if (selectedCircle == null) return;
         Circles.Add(selectedCircle);
         CirclesPrj.Remove(selectedCircle);
      }

      /// <summary>
      /// Переносит выбранный контур из списка DXF в список для добавления в проект.
      /// </summary>
      private void ContourIn(object? o = null)
      {
         if (selectedContour == null) return;
         ContoursPrj.Add(selectedContour);
         Contours.Remove(selectedContour);
      }

      /// <summary>
      /// Переносит все контуры из списка DXF в список для добавления в проект.
      /// </summary>
      private void ContoursIn(object? o = null)
      {
         if (Contours == null) return;
         ContoursPrj = new(Contours);
         Contours.Clear();
      }

      /// <summary>
      /// Возвращает выбранный контур из списка проекта обратно в список DXF.
      /// </summary>
      private void ContourOut(object? o = null)
      {
         if (selectedContour == null) return;
         Contours.Add(selectedContour);
         ContoursPrj.Remove(selectedContour);
      }

      /// <summary>
      /// Сохраняет выбранные окружности в базу данных проекта и перенумеровывает их.
      /// </summary>
      private void SaveCircles(object? o = null)
      {
         if (circlesPrj == null || circlesPrj.Count == 0) return;
         mvm.db.AddRange(circlesPrj);
         mvm.LogService.Info($"В проект добавлено {circlesPrj.Count} окружностей");
         CirclesPrj.Clear();
         mvm.CirclesRenumber();
      }

      /// <summary>
      /// Сохраняет выбранные контуры в базу данных проекта и перенумеровывает их.
      /// </summary>
      private void SaveContours(object? o = null)
      {
         if (contoursPrj == null || contoursPrj.Count == 0) return;
         mvm.db.AddRange(contoursPrj);
         mvm.LogService.Info($"В проект добавлено {contoursPrj.Count} контуров");
         ContoursPrj.Clear();
         mvm.ContoursRenumber();
      }

      /// <summary>
      /// Открывает диалог выбора DXF-файла, загружает документ и заполняет
      /// коллекции контуров и окружностей из полилиний и кругов DXF.
      /// </summary>
      void OpenDxf(object? o = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: "Файл обмена чертежами (*.dxf)|*.dxf",
            title: "Импорт данных из файла DXF");

         if (string.IsNullOrEmpty(fileName)) return;

         Contours.Clear();
         ContoursPrj.Clear();
         Circles.Clear();
         CirclesPrj.Clear();

         Dxf = DxfDocument.Load(fileName);
         GeometrySet = Dxf.Name;
         CurrentPlot = new DxfPlot(Dxf);

         plines = new(Dxf.Entities.Polylines2D);
         List<Circle> circls = new(Dxf.Entities.Circles);

         int i = 1;
         foreach (var c in circls)
         {
            Circles.Add(CircleDxfToCircle(c, i));
            i++;
         }
         i = 1;
         foreach (var p in plines)
         {
            Contours.Add(PolylineToContour(p, i));
            i++;
         }
      }


      /// <summary>
      /// Преобразует полилинию DXF в доменный объект <see cref="Contour"/>.
      /// Координаты вершин умножаются на масштабный коэффициент.
      /// </summary>
      /// <param name="pline">Полилиния DXF для преобразования.</param>
      /// <param name="i">Порядковый номер контура.</param>
      /// <returns>Объект <see cref="Contour"/>, созданный из вершин полилинии.</returns>
      public Contour PolylineToContour(Polyline2D pline, int i)
      {
         List<StressPoint> points = new(pline.Vertexes.Count);
         int j = 1;
         foreach (var item in pline.Vertexes)
         {
            points.Add(new StressPoint(item.Position.X * scale, item.Position.Y * scale) { Num = j });
            j++;
         }
         Vector2 first = pline.Vertexes.First().Position;
         Vector2 last = pline.Vertexes.Last().Position;
         if (pline.IsClosed && !first.Equals(last, 1e-4))
            points.Add(new StressPoint(first.X * scale, first.Y * scale) { Num = j });

         var res = new Contour(points, $"{pline.Layer.Name}") { Num = i, GeometrySet = geometrySet };
         res.SetWKT();

         return res;
      }

      /// <summary>
      /// Преобразует круг DXF в доменный объект <see cref="CircleP"/>.
      /// Координаты центра и радиус умножаются на масштабный коэффициент.
      /// </summary>
      /// <param name="circle">Круг DXF для преобразования.</param>
      /// <param name="i">Порядковый номер окружности.</param>
      /// <returns>Объект <see cref="CircleP"/>, созданный из круга DXF.</returns>
      public CircleP CircleDxfToCircle(Circle circle, int i)
      {
         CircleP res = new(circle.Center.X * scale, circle.Center.Y * scale, circle.Radius * scale )
         {
            Num = i,
            Tag = $"{circle.Layer.Name}",
            GeometrySet = $"{geometrySet}"
         };
         return res;
      }
   }
}
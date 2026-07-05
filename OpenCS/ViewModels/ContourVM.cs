using CScore;

using CsvHelper;
using CsvHelper.Configuration;

using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views.Dialogs;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
   /// <summary>Этап интерактивного рисования контура на холсте.</summary>
   public enum ContourDrawingPhase { Setup, Draw }

   /// <summary>
   /// Вспомогательный класс для сериализации/десериализации координат при
   /// импорте и экспорте контуров в формате CSV.
   /// </summary>
   class Coord
   {
      /// <summary>Координата X точки.</summary>
      public double X { get; set; }

      /// <summary>Координата Y точки.</summary>
      public double Y { get; set; }
   }

   /// <summary>
   /// Модель представления контура. Обеспечивает привязку данных доменного объекта
   /// <see cref="Contour"/> к элементам управления WPF, управляет редактированием,
   /// сохранением и обменом координат через CSV-файлы. Является промежуточным звеном
   /// между доменной моделью контура и представлением <see cref="ContourPlot"/>.
   /// </summary>
   public class ContourVM : ViewModelBase
   {
      /// <summary>Выбранная точка контура, используемая для редактирования в представлении.</summary>
      StressPoint? point;

      /// <summary>Флаг, указывающий, находится ли контур в режиме редактирования.</summary>
      bool isEdit;

      ContourDrawingPhase drawingPhase = ContourDrawingPhase.Setup;
      double viewWidth = 1.0;
      double viewHeight = 1.0;
      int gridStepMm = 10;
      bool snapToGrid = true;

      NotifyCollectionChangedEventHandler? _pointsChangedHandler;

      /// <summary>
      /// Доменный объект контура, содержащий геометрию и метаданные.
      /// Изменения свойств ViewModel проксируются в этот объект.
      /// </summary>
      public Contour Contour { get; set; } = new();

      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к базе данных, сервисам логирования и файловых диалогов.
      /// </summary>
      public AppViewModel mvm { get; set; } = null!;

      /// <summary>
      /// Список допустимых типов контура (Оболочка, Отверстие, Нет) для привязки в ComboBox.
      /// </summary>
      public List<ContourType> Types { get; set; } = null!;

      /// <summary>
      /// Сервис построения графиков (устаревший путь; контурный холст обновляется через <see cref="CanvasRefreshRequested"/>).
      /// </summary>
      public IPlotService PlotService { get; set; } = null!;

      /// <summary>Запрос перерисовки интерактивного холста контура.</summary>
      public event Action? CanvasRefreshRequested;

      /// <summary>
      /// Флаг, указывающий, сохранён ли контур в базу данных.
      /// Используется для различения операций создания и обновления.
      /// </summary>
      public bool IsSaved {  get; set; }

      /// <summary>
      /// Флаг режима редактирования контура. При установке значения
      /// вызывается <c>OnPropertyChanged()</c> для обновления привязки.
      /// </summary>
      public bool IsEdit
      {
         get => isEdit;
         set { isEdit = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedsClosingHint)); OnPropertyChanged(nameof(IsDrawingSetup)); OnPropertyChanged(nameof(IsDrawingActive)); }
      }

      /// <summary>
      /// Тип контура (Оболочка, Отверстие, Нет). Проксирует свойство
      /// <see cref="Contour.Type"/> с уведомлением об изменении.
      /// </summary>
      public ContourType Type { get => Contour.Type; set { Contour.Type = value; OnPropertyChanged(); } }

      /// <summary>
      /// Номер контура в порядке следования. Проксирует <see cref="Contour.Num"/>
      /// с уведомлением об изменении.
      /// </summary>
      public int Num
      {
         get => Contour.Num;
         set { Contour.Num = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Наименование (тег) контура. При изменении сбрасывает описание.
      /// Проксирует <see cref="Contour.Tag"/> с уведомлением об изменении.
      /// </summary>
      public string Tag
      {
         get => Contour.Tag;
         set { Contour.Tag = value; OnPropertyChanged(); Description = ""; }
      }

      /// <summary>
      /// Имя набора геометрии (GeometrySet), к которому принадлежит контур.
      /// При изменении сбрасывает описание.
      /// </summary>
      public string? Set
      {
         get => Contour.GeometrySet;
         set { Contour.GeometrySet = value; OnPropertyChanged(); Description = ""; }
      }

      /// <summary>
      /// Описание контура, формируемое автоматически из его параметров.
      /// Проксирует <see cref="Contour.Description"/>.
      /// </summary>
      public string Description { get => Contour.Description; set { Contour.Description = value; OnPropertyChanged(); } }

      /// <summary>
      /// Коллекция точек контура (StressPoint). При изменении вызывается
      /// <c>OnPropertyChanged()</c> для обновления привязки в ListBox точек.
      /// </summary>
      public ObservableCollection<StressPoint> Points
      {
         get => Contour.Points;
         set { Contour.Points = value; WirePointsCollection(); OnPropertyChanged(); NotifyContourGeometryChanged(); }
      }

      /// <summary>Показывать предупреждение о незамкнутом контуре (режим редактирования, ≥3 вершин).</summary>
      public bool NeedsClosingHint => IsEdit && Contour.Points.Count >= 3 && !Contour.IsClosed;

      /// <summary>Этап рисования: задание области или расстановка вершин.</summary>
      public ContourDrawingPhase DrawingPhase
      {
         get => drawingPhase;
         set
         {
            drawingPhase = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDrawingSetup));
            OnPropertyChanged(nameof(IsDrawingActive));
         }
      }

      /// <summary>Панель габарита: полная ширина области (центр в начале координат), м.</summary>
      public double ViewWidth
      {
         get => viewWidth;
         set
         {
            viewWidth = value;
            if (DrawingPhase == ContourDrawingPhase.Setup)
               SetViewFromOrigin(viewWidth, viewHeight);
            OnPropertyChanged();
            RefreshPlot();
         }
      }

      /// <summary>Панель габарита: полная высота области (центр в начале координат), м.</summary>
      public double ViewHeight
      {
         get => viewHeight;
         set
         {
            viewHeight = value;
            if (DrawingPhase == ContourDrawingPhase.Setup)
               SetViewFromOrigin(viewWidth, viewHeight);
            OnPropertyChanged();
            RefreshPlot();
         }
      }

      /// <summary>Шаг строительной сетки, мм.</summary>
      public int GridStepMm
      {
         get => gridStepMm;
         set { gridStepMm = value > 0 ? value : 1; OnPropertyChanged(); OnPropertyChanged(nameof(GridStepM)); RefreshPlot(); }
      }

      /// <summary>Шаг сетки в метрах (для расчётов и отрисовки).</summary>
      public double GridStepM => gridStepMm / 1000.0;

      /// <summary>Доступные значения шага сетки, мм.</summary>
      public IReadOnlyList<int> GridStepMmOptions { get; } = [1, 5, 10, 25, 50, 100];

      /// <summary>Привязка координат вершин к сетке.</summary>
      public bool SnapToGrid
      {
         get => snapToGrid;
         set { snapToGrid = value; OnPropertyChanged(); }
      }

      /// <summary>Левая граница видимой области модели, м.</summary>
      public double ViewXMin { get; private set; } = -0.5;

      /// <summary>Нижняя граница видимой области модели, м.</summary>
      public double ViewYMin { get; private set; } = -0.5;

      /// <summary>Правая граница видимой области модели, м.</summary>
      public double ViewXMax { get; private set; } = 0.5;

      /// <summary>Верхняя граница видимой области модели, м.</summary>
      public double ViewYMax { get; private set; } = 0.5;

      /// <summary>Настройка габарита перед первым рисованием.</summary>
      public bool IsDrawingSetup => IsEdit && DrawingPhase == ContourDrawingPhase.Setup && Contour.Points.Count == 0;

      /// <summary>Интерактивное рисование вершин на холсте.</summary>
      public bool IsDrawingActive => IsEdit && DrawingPhase == ContourDrawingPhase.Draw;

      /// <summary>
      /// Выбранная точка контура в ListBox. Используется для редактирования
      /// координат отдельной точки.
      /// </summary>
      public StressPoint? Point { get => point; set { point = value; OnPropertyChanged(); } }

      /// <summary>
      /// Изображение контура (DrawingImage), получаемое путём вызова
      /// <see cref="Contour.Draw()"/>. Используется для привязки в представлении.
      /// </summary>
      public DrawingImage? DI { get { return Contour.Draw(); } }

      /// <summary>
      /// Команда привязки для перенумерации точек контура.
      /// Вызывает метод <c>RenumPoint</c>.
      /// </summary>
      public ICommand RenumPointCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для добавления замыкающей вершины контура.
      /// </summary>
      public ICommand CloseContourCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для импорта координат точек из CSV-файла.
      /// Вызывает метод <c>ImportCsv</c>.
      /// </summary>
      public ICommand ImportCsvCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для экспорта координат точек в CSV-файл.
      /// Вызывает метод <c>ExportCsv</c>.
      /// </summary>
      public ICommand ExportCsvCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для переключения режима редактирования контура.
      /// Вызывает метод <c>SaveChanges</c>.
      /// </summary>
      public ICommand SaveChangesCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для сохранения контура в базу данных.
      /// Вызывает метод <c>Save</c>.
      /// </summary>
      public ICommand SaveCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для отображения геометрических свойств контура.
      /// Вызывает метод <c>ShowProperties</c>.
      /// </summary>
      public ICommand ShowPropertiesCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для сдвига контура.
      /// Вызывает метод <c>Translate</c>.
      /// </summary>
      public ICommand TranslateCommand { get; set; } = null!;

      /// <summary>
      /// Команда привязки для масштабирования контура относительно центра тяжести.
      /// Вызывает метод <c>Scale</c>.
      /// </summary>
      public ICommand ScaleCommand { get; set; } = null!;

      /// <summary>Начать рисование после задания габарита области.</summary>
      public ICommand BeginDrawingCommand { get; set; } = null!;

      /// <summary>
      /// Инициализирует экземпляр <see cref="ContourVM"/> с пустым контуром
      /// и создаёт все команды привязки.
      /// </summary>
      public ContourVM()
      {
         Types = [ContourType.Hull, ContourType.Hole, ContourType.None];

          RenumPointCommand = new RelayCommand(RenumPoint);
          CloseContourCommand = new RelayCommand(CloseContour);
          ExportCsvCommand = new RelayCommand(ExportCsv);
          ImportCsvCommand = new RelayCommand(ImportCsv);
          SaveChangesCommand = new RelayCommand(SaveChanges);
          SaveCommand = new RelayCommand(Save);
          ShowPropertiesCommand = new RelayCommand(_ => ShowProperties());
          TranslateCommand = new RelayCommand(_ => Translate());
          ScaleCommand = new RelayCommand(_ => Scale());
          BeginDrawingCommand = new RelayCommand(_ => BeginDrawing(), _ => CanBeginDrawing());
          SetViewFromOrigin(viewWidth, viewHeight);
          WirePointsCollection();
       }

       /// <summary>
       /// Инициализирует экземпляр <see cref="ContourVM"/> с заданным доменным объектом контура
       /// и создаёт все команды привязки.
       /// </summary>
       /// <param name="contour">Существующий объект контура для редактирования.</param>
       public ContourVM(Contour contour)
       {
          Contour = contour;
          IsSaved = true;
          Types = [ContourType.Hull, ContourType.Hole, ContourType.None];

          RenumPointCommand = new RelayCommand(RenumPoint);
          CloseContourCommand = new RelayCommand(CloseContour);
          ExportCsvCommand = new RelayCommand(ExportCsv);
          ImportCsvCommand = new RelayCommand(ImportCsv);
          SaveChangesCommand = new RelayCommand(SaveChanges);
          SaveCommand = new RelayCommand(Save);
          ShowPropertiesCommand = new RelayCommand(_ => ShowProperties());
          TranslateCommand = new RelayCommand(_ => Translate());
          ScaleCommand = new RelayCommand(_ => Scale());
          BeginDrawingCommand = new RelayCommand(_ => BeginDrawing(), _ => CanBeginDrawing());
          if (Contour.Points.Count > 0)
          {
             DrawingPhase = ContourDrawingPhase.Draw;
             FitViewToPoints();
          }
          else
             SetViewFromOrigin(viewWidth, viewHeight);
          WirePointsCollection();
       }

       void WirePointsCollection()
       {
          if (_pointsChangedHandler != null)
             Contour.Points.CollectionChanged -= _pointsChangedHandler;
          _pointsChangedHandler = (_, _) =>
          {
             NotifyContourGeometryChanged();
             RefreshPlot();
          };
          Contour.Points.CollectionChanged += _pointsChangedHandler;
       }

       bool CanBeginDrawing() => IsDrawingSetup && viewWidth > 0 && viewHeight > 0;

       /// <summary>Задаёт область рисования с началом координат (0; 0) в центре.</summary>
       public void SetViewFromOrigin(double width, double height)
       {
          double w = Math.Max(width, 1e-6);
          double h = Math.Max(height, 1e-6);
          ViewXMin = -w / 2;
          ViewYMin = -h / 2;
          ViewXMax = w / 2;
          ViewYMax = h / 2;
       }

       /// <summary>Подгоняет видимую область под существующие вершины.</summary>
       public void FitViewToPoints()
       {
          if (Contour.Points.Count == 0) return;

          double xMin = double.MaxValue, xMax = double.MinValue;
          double yMin = double.MaxValue, yMax = double.MinValue;
          foreach (var p in Contour.Points)
          {
             if (p.X < xMin) xMin = p.X;
             if (p.X > xMax) xMax = p.X;
             if (p.Y < yMin) yMin = p.Y;
             if (p.Y > yMax) yMax = p.Y;
          }
          if (xMax - xMin < 1e-9) { xMin -= 0.5; xMax += 0.5; }
          if (yMax - yMin < 1e-9) { yMin -= 0.5; yMax += 0.5; }

          double padX = (xMax - xMin) * 0.08 + 0.01;
          double padY = (yMax - yMin) * 0.08 + 0.01;
          ViewXMin = xMin - padX;
          ViewYMin = yMin - padY;
          ViewXMax = xMax + padX;
          ViewYMax = yMax + padY;
          RefreshPlot();
       }

       void BeginDrawing()
       {
          if (!CanBeginDrawing()) return;
          SetViewFromOrigin(viewWidth, viewHeight);
          DrawingPhase = ContourDrawingPhase.Draw;
          mvm?.LogService.Info(Loc.S("ContourDrawingStartedLog"));
          RefreshPlot();
       }

       /// <summary>Привязка координаты к шагу сетки.</summary>
       public double SnapCoord(double v)
       {
          double step = GridStepM;
          return SnapToGrid && step > 0 ? Math.Round(v / step) * step : v;
       }

       /// <summary>Добавляет вершину в режиме рисования.</summary>
       public void AddPoint(double x, double y)
       {
          if (!IsDrawingActive) return;

          x = SnapCoord(x);
          y = SnapCoord(y);
          int n = Contour.Points.Count + 1;
          Contour.Points.Add(new StressPoint(x, y) { Num = n });
          RenumPointLocal();
          Point = Contour.Points[^1];
          OnPropertyChanged(nameof(Points));
          RefreshPlot();
       }

       /// <summary>Перемещает вершину (с синхронизацией замыкающей).</summary>
       public void MovePoint(StressPoint pt, double x, double y)
       {
          if (!IsDrawingActive) return;

          x = SnapCoord(x);
          y = SnapCoord(y);
          pt.X = x;
          pt.Y = y;

          int idx = Contour.Points.IndexOf(pt);
          if (idx >= 0 && Contour.IsClosed)
          {
             if (idx == 0)
             {
                Contour.Points[^1].X = x;
                Contour.Points[^1].Y = y;
             }
             else if (idx == Contour.Points.Count - 1)
             {
                Contour.Points[0].X = x;
                Contour.Points[0].Y = y;
             }
          }
          RefreshPlot();
       }

       /// <summary>Завершает перетаскивание вершины — обновляет привязку таблицы.</summary>
       public void CommitPointMove()
       {
          OnPropertyChanged(nameof(Points));
          NotifyContourGeometryChanged();
       }

       /// <summary>Удаляет последнюю вершину.</summary>
       public void RemoveLastPoint()
       {
          if (!IsDrawingActive || Contour.Points.Count == 0) return;
          Contour.Points.RemoveAt(Contour.Points.Count - 1);
          RenumPointLocal();
          Point = Contour.Points.Count > 0 ? Contour.Points[^1] : null;
          OnPropertyChanged(nameof(Points));
          NotifyContourGeometryChanged();
          RefreshPlot();
       }

       /// <summary>Замыкание по клику на первую вершину.</summary>
       public bool TryCloseAtFirstVertex()
       {
          if (!IsDrawingActive || Contour.Points.Count < 3 || Contour.IsClosed) return false;
          CloseContour();
          return Contour.IsClosed;
       }

       void RenumPointLocal()
       {
          int i = 1;
          foreach (var item in Contour.Points)
             item.Num = i++;
       }

       void NotifyContourGeometryChanged()
       {
          OnPropertyChanged(nameof(NeedsClosingHint));
       }

       void CloseContour(object? o = null)
       {
          if (!Contour.TryClose())
          {
             MessageBox.Show(Loc.S("ContourTooFewPoints"), Loc.S("Warning"),
                 MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
          }
          RenumPoint();
          RefreshPlot();
          NotifyContourGeometryChanged();
          mvm?.LogService.Info(Loc.S("ContourClosedLog"));
       }

       /// <summary>
       /// Замыкает контур при необходимости, обновляет WKT и проверяет минимальное число вершин.
       /// </summary>
       bool PrepareContourForSave()
       {
          if (Contour.Points.Count < 3)
          {
             MessageBox.Show(Loc.S("ContourTooFewPoints"), Loc.S("Warning"),
                 MessageBoxButton.OK, MessageBoxImage.Warning);
             return false;
          }
          if (!Contour.TryClose())
          {
             MessageBox.Show(Loc.S("ContourTooFewPoints"), Loc.S("Warning"),
                 MessageBoxButton.OK, MessageBoxImage.Warning);
             return false;
          }
          RenumPoint();
          RefreshPlot();
          NotifyContourGeometryChanged();
          return true;
       }

       /// <summary>
       /// Предупреждает, если вершин контура меньше требуемого минимума.
       /// </summary>
       bool WarnContourVerticesBelow(int minCount)
       {
          if (Contour.Points.Count >= minCount) return false;
          MessageBox.Show(Loc.S("ContourTooFewPoints"), Loc.S("Warning"),
              MessageBoxButton.OK, MessageBoxImage.Warning);
          return true;
       }

       void Translate()
       {
           if (WarnContourVerticesBelow(4)) return;

           var dlg = new Views.Dialogs.DoubleInputDialog(
               "Сдвиг контура",
               "Смещение по X (м):",
               "Смещение по Y (м):");
           if (dlg.ShowDialog() != true) return;

           double dx = dlg.Value1, dy = dlg.Value2;
           if (dx == 0 && dy == 0) return;

           foreach (var p in Contour.Points)
           {
               p.X += dx;
               p.Y += dy;
           }
           Contour.Points = new ObservableCollection<StressPoint>(Contour.Points);
           Contour.SetWKT();

           RefreshPlot();
           mvm.db.SaveContour(Contour);
           mvm.LogService.Info($"Контур сдвинут на ({dx}, {dy})");
       }

       void Scale()
       {
           if (WarnContourVerticesBelow(4)) return;

           var dlg = new Views.Dialogs.DoubleInputDialog(
               "Масштабирование контура",
               "Коэффициент по X:",
               "Коэффициент по Y:",
               1.0, 1.0);
           if (dlg.ShowDialog() != true) return;

           double sx = dlg.Value1, sy = dlg.Value2;
           if (sx <= 0 || sy <= 0)
           {
               MessageBox.Show("Коэффициенты масштабирования должны быть положительными.",
                   "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
           }
           if (sx == 1 && sy == 1) return;

           // Centroid
           double cx = 0, cy = 0;
           int n = Contour.Points.Count;
           foreach (var p in Contour.Points) { cx += p.X; cy += p.Y; }
           cx /= n; cy /= n;

           foreach (var p in Contour.Points)
           {
               p.X = cx + (p.X - cx) * sx;
               p.Y = cy + (p.Y - cy) * sy;
           }
           Contour.Points = new ObservableCollection<StressPoint>(Contour.Points);
           Contour.SetWKT();

           RefreshPlot();
           mvm.db.SaveContour(Contour);
           mvm.LogService.Info($"Контур масштабирован ({sx}, {sy})");
       }

       void RefreshPlot() => CanvasRefreshRequested?.Invoke();

       void ShowProperties()
       {
          if (WarnContourVerticesBelow(4)) return;
          var dlg = new Views.Dialogs.ContourPropsWindow(Contour, Tag);
          dlg.ShowDialog();
       }

      /// <summary>
      /// Перенумеровывает точки контура начиная с 1 и сохраняет изменения в базу данных.
      /// </summary>
      void RenumPoint(object? o = null)
      {
         int i = 1;
         foreach (var item in Points)
         {
            item.Num = i; i++;
         }
         Points = [.. Contour.Points];
         mvm.db.SaveContour(Contour);
      }

      /// <summary>
      /// Экспортирует координаты точек контура в CSV-файл с разделителем «;».
      /// Использует <see cref="IFileDialogService"/> для выбора файла сохранения.
      /// </summary>
      void ExportCsv(object? o = null)
      {
         string fileName = mvm.FileDialogService.SaveFile(
            filter: "Текстовый файл (*.csv)|*.csv",
            defaultExt: "*.csv",
            title: "Экспорт координат в файл csv")!;

         if (string.IsNullOrEmpty(fileName)) return;

         if(Contour.Points.Count == 0) return;

         List<Coord> coord = [];
         foreach (var item in Points)
         {
            coord.Add(new() { X = item.X, Y = item.Y });
         }

         var config = new CsvConfiguration(CultureInfo.InvariantCulture)
         {
            Delimiter = ";"
         };

         using var writer = new StreamWriter(fileName);
         using var csv = new CsvWriter(writer, config);
         csv.WriteRecords(coord);

         mvm.LogService.Info($"Данные успешно записаны в файл '{fileName}'");
      }

      /// <summary>
      /// Импортирует координаты точек контура из CSV-файла с разделителем «;».
      /// После загрузки обновляет контур и перерисовывает график.
      /// </summary>
      void ImportCsv(object? o = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: "Текстовый файл (*.csv)|*.csv",
            title: "Импорт координат из файла csv")!;

         if (string.IsNullOrEmpty(fileName)) return;

         var config = new CsvConfiguration(CultureInfo.InvariantCulture)
         {
            Delimiter = ";"
         };
         using var reader = new StreamReader(fileName);
         using var csv = new CsvReader(reader, config);
         var records = csv.GetRecords<Coord>();

         if(records == null) return;

         List<StressPoint> points = [];
         int i = 1;
         foreach (var r in records)
         {
            points.Add(new StressPoint(r.X, r.Y) { Num = i});
            i++;
         }
         Contour.Points = new ObservableCollection<StressPoint>(points);
         WirePointsCollection();
         if (Contour.Points.Count >= 3 && !Contour.IsClosed)
            Contour.TryClose();
         DrawingPhase = ContourDrawingPhase.Draw;
         RenumPoint();
         FitViewToPoints();
         RefreshPlot();
         OnPropertyChanged(nameof(Points));
         NotifyContourGeometryChanged();
      }

      /// <summary>
      /// Логирует состояние режима редактирования контура (доступен/недоступен).
      /// </summary>
      void SaveChanges(object? o = null)
      {
         if(isEdit)
         {
            mvm.LogService.Info("Контур доступен для редактирования");
         }
         else
         {
            mvm.LogService.Info("Контур не доступен для редактирования");
         }
      }

      /// <summary>
      /// Сохраняет контур в базу данных. Если контур уже сохранён (<see cref="IsSaved"/> = true),
      /// обновляет существующую запись. Иначе — добавляет новую запись и устанавливает
      /// <see cref="IsSaved"/> в true. После сохранения перенумеровывает контуры.
      /// </summary>
      void Save(object? o = null)
      {
         if (!PrepareContourForSave()) return;

         if (IsSaved)
         {
            mvm.db.SaveContour(Contour);
            mvm.LogService.Info($"Изменения контура '{Tag}' сохранены");
         }
         else
         {
            mvm.Contours.Add(Contour);
            mvm.db.SaveContour(Contour);
            IsSaved = true;

            mvm.LogService.Info($"Контур '{Tag}' успешно сохранен");
         }
      }

   }
}
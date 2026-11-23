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
   public class ContourVM : ViewModelBase, IUnsavedChanges
   {
      /// <summary>Записанное состояние: обозначение, набор, тип и вершины (снимок для «*» и «Не сохранять»).</summary>
      sealed record SavedState(string Tag, string? Set, ContourType Type, (double X, double Y)[] Points);

      SavedState _saved = null!;
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

      /// <summary>Контур записан в пул проекта (новый — пока не нажата «Сохранить»).</summary>
      public bool IsSaved => Contour.Id > 0;

      /// <summary>Есть правки, не записанные в проект (заголовок со «*», вопрос при уходе).</summary>
      public bool IsDirty => !Same(Capture(), _saved);

      /// <summary>Обозначение для вопроса о сохранении.</summary>
      public string DisplayName => string.IsNullOrWhiteSpace(Contour.Tag) ? Loc.S("NewContour") : Contour.Tag;

      SavedState Capture() => new(Contour.Tag ?? "", Contour.GeometrySet, Contour.Type,
         Contour.Points.Select(p => (p.X, p.Y)).ToArray());

      static bool Same(SavedState a, SavedState b) =>
         a.Tag == b.Tag && a.Set == b.Set && a.Type == b.Type && a.Points.SequenceEqual(b.Points);

      void MarkSaved()
      {
         _saved = Capture();
         Touch();
      }

      /// <summary>Уведомляет о возможной смене <see cref="IsDirty"/>.</summary>
      void Touch() => OnPropertyChanged(nameof(IsDirty));

      /// <summary>
      /// Флаг режима редактирования контура. При установке значения
      /// вызывается <c>OnPropertyChanged()</c> для обновления привязки.
      /// </summary>
      public bool IsEdit
      {
         get => isEdit;
         set { isEdit = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReadOnlyTable)); OnPropertyChanged(nameof(NeedsClosingHint)); OnPropertyChanged(nameof(IsDrawingSetup)); OnPropertyChanged(nameof(IsDrawingActive)); }
      }

      /// <summary>Таблица вершин только для чтения вне режима правки (защита от случайных правок).</summary>
      public bool IsReadOnlyTable => !isEdit;

      /// <summary>
      /// Тип контура (Оболочка, Отверстие, Нет). Проксирует свойство
      /// <see cref="Contour.Type"/> с уведомлением об изменении.
      /// </summary>
      public ContourType Type { get => Contour.Type; set { Contour.Type = value; OnPropertyChanged(); Touch(); } }

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
         set { Contour.Tag = value; OnPropertyChanged(); Description = ""; Touch(); }
      }

      /// <summary>
      /// Имя набора геометрии (GeometrySet), к которому принадлежит контур.
      /// При изменении сбрасывает описание.
      /// </summary>
      public string? Set
      {
         get => Contour.GeometrySet;
         set { Contour.GeometrySet = string.IsNullOrWhiteSpace(value) ? null : value; OnPropertyChanged(); Description = ""; Touch(); }
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
          SaveCommand = new RelayCommand(Save);
          ShowPropertiesCommand = new RelayCommand(_ => ShowProperties());
          TranslateCommand = new RelayCommand(_ => Translate());
          ScaleCommand = new RelayCommand(_ => Scale());
          BeginDrawingCommand = new RelayCommand(_ => BeginDrawing(), _ => CanBeginDrawing());
          SetViewFromOrigin(viewWidth, viewHeight);
          WirePointsCollection();
          _saved = Capture();
       }

       /// <summary>
       /// Инициализирует экземпляр <see cref="ContourVM"/> с заданным доменным объектом контура
       /// и создаёт все команды привязки.
       /// </summary>
       /// <param name="contour">Существующий объект контура для редактирования.</param>
       public ContourVM(Contour contour)
       {
          Contour = contour;
          Types = [ContourType.Hull, ContourType.Hole, ContourType.None];

          RenumPointCommand = new RelayCommand(RenumPoint);
          CloseContourCommand = new RelayCommand(CloseContour);
          ExportCsvCommand = new RelayCommand(ExportCsv);
          ImportCsvCommand = new RelayCommand(ImportCsv);
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
          _saved = Capture();
       }

       void WirePointsCollection()
       {
          if (_pointsChangedHandler != null)
             Contour.Points.CollectionChanged -= _pointsChangedHandler;
          _pointsChangedHandler = (_, _) =>
          {
             NotifyContourGeometryChanged();
             RefreshPlot();
             Touch();
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
          RefreshPlot();
          Touch();
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
       /// Предупреждает, если вершин контура меньше требуемого минимума.
       /// </summary>
       bool WarnContourVerticesBelow(int minCount)
       {
          if (Contour.Points.Count >= minCount) return false;
          MessageBox.Show(Loc.S("ContourTooFewPoints"), Loc.S("Warning"),
              MessageBoxButton.OK, MessageBoxImage.Warning);
          return true;
       }

       /// <summary>Синхронизирует Points → X/Y → WKT и обновляет таблицу координат.</summary>
       void ApplyContourGeometryChange()
       {
          Contour.TryClose();
          Contour.PointsToXYs();
          Contour.SetWKT();

          // Клонирование точек: StressPoint не уведомляет об изменении X/Y,
          // поэтому DataGrid не обновится без замены элементов коллекции.
          var refreshed = new ObservableCollection<StressPoint>();
          foreach (var p in Contour.Points)
          {
             var c = p.Clone();
             c.Contour = Contour;
             refreshed.Add(c);
          }
          Points = refreshed;
       }

       void Translate()
       {
           if (WarnContourVerticesBelow(4)) return;

           var dlg = new Views.Dialogs.DoubleInputDialog(
               Loc.S("ContourTranslateTitle"),
               Loc.S("ContourTranslateDx"),
               Loc.S("ContourTranslateDy"));
           if (dlg.ShowDialog() != true) return;

           double dx = dlg.Value1, dy = dlg.Value2;
           if (dx == 0 && dy == 0) return;

           foreach (var p in Contour.Points)
           {
               p.X += dx;
               p.Y += dy;
           }
           ApplyContourGeometryChange();
           FitViewToPoints();
           Touch();
           mvm.LogService.Info(string.Format(Loc.S("ContourTranslatedLog"), dx, dy));
       }

       void Scale()
       {
           if (WarnContourVerticesBelow(4)) return;

           var dlg = new Views.Dialogs.DoubleInputDialog(
               Loc.S("ContourScaleTitle"),
               Loc.S("ContourScaleKx"),
               Loc.S("ContourScaleKy"),
               1.0, 1.0);
           if (dlg.ShowDialog() != true) return;

           double sx = dlg.Value1, sy = dlg.Value2;
           if (sx <= 0 || sy <= 0)
           {
               MessageBox.Show(Loc.S("ContourScaleMustBePositive"),
                   Loc.S("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
           }
           if (sx == 1 && sy == 1) return;

           ApplyContourGeometryChange();
           var props = new GeoProps(Contour);
           double cx = props.Centroid.X;
           double cy = props.Centroid.Y;

           foreach (var p in Contour.Points)
           {
               p.X = cx + (p.X - cx) * sx;
               p.Y = cy + (p.Y - cy) * sy;
           }
           ApplyContourGeometryChange();
           FitViewToPoints();
           Touch();
           mvm.LogService.Info(string.Format(Loc.S("ContourScaledLog"), sx, sy));
       }

       void RefreshPlot() => CanvasRefreshRequested?.Invoke();

       void ShowProperties()
       {
          if (WarnContourVerticesBelow(4)) return;
          ApplyContourGeometryChange();
          var dlg = new Views.Dialogs.ContourPropsWindow(Contour, Tag);
          dlg.ShowDialog();
       }

      /// <summary>
      /// Перенумеровывает точки контура начиная с 1 (в базу не пишет — только «Сохранить»).
      /// </summary>
      void RenumPoint(object? o = null)
      {
         int i = 1;
         foreach (var item in Points)
         {
            item.Num = i; i++;
         }
         Points = [.. Contour.Points];
      }

      /// <summary>
      /// Экспортирует координаты точек контура в CSV-файл: «X;Y», метры, десятичная точка.
      /// </summary>
      void ExportCsv(object? o = null)
      {
         string fileName = mvm.FileDialogService.SaveFile(
            filter: Loc.S("CsvFileFilter"),
            defaultExt: "*.csv",
            title: Loc.S("ContourCsvExportTitle"))!;

         if (string.IsNullOrEmpty(fileName)) return;
         if (Contour.Points.Count == 0) return;

         var lines = new List<string> { "X;Y" };
         lines.AddRange(Points.Select(p => FormattableString.Invariant($"{p.X:R};{p.Y:R}")));
         File.WriteAllLines(fileName, lines);
         mvm.LogService.Info(string.Format(Loc.S("CsvExportedLog"), fileName));
      }

      /// <summary>
      /// Импортирует вершины из CSV: строки «X;Y» в метрах (первая строка может быть заголовком). Разделитель полей —
      /// «;» или табуляция; «,» — только если их нет. Десятичный разделитель — «,» или «.». Неразобранные строки
      /// пропускаются (число — в журнал). Правка не записывается до «Сохранить».
      /// </summary>
      public int ImportCsv(string path)
      {
         var lines = File.ReadAllLines(path);
         char delim = lines.Any(l => l.Contains(';')) ? ';' : lines.Any(l => l.Contains('\t')) ? '\t' : ',';
         var points = new List<StressPoint>();
         int skipped = 0;
         foreach (var raw in lines)
         {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(delim);
            if (parts.Length >= 2 && NumberInput.TryParse(parts[0], out double x) && NumberInput.TryParse(parts[1], out double y))
               points.Add(new StressPoint(x, y) { Num = points.Count + 1, Contour = Contour });
            else if (points.Count > 0 || !char.IsLetter(line[0]))
               skipped++;
         }
         if (points.Count == 0) return skipped;

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
         Touch();
         return skipped;
      }

      void ImportCsv(object? o = null)
      {
         string fileName = mvm.FileDialogService.OpenFile(
            filter: Loc.S("CsvFileFilter"),
            title: Loc.S("ContourCsvImportTitle"))!;
         if (string.IsNullOrEmpty(fileName)) return;
         try
         {
            int skipped = ImportCsv(fileName);
            if (skipped > 0) mvm.LogService.Warning(string.Format(Loc.S("CsvRowsSkippedLog"), skipped));
         }
         catch (IOException ex)
         {
            mvm.LogService.Error(ex.Message);
         }
      }

      /// <summary>
      /// Проверяет и записывает контур через <see cref="ContourAuthoring"/> (новый — добавляется в пул). Ошибки —
      /// в журнал, ничего не записано.
      /// </summary>
      public IReadOnlyList<FieldError> Save()
      {
         bool isNew = Contour.Id == 0;
         var errors = ContourAuthoring.Save(mvm.db, Contour);
         if (errors.Count > 0)
         {
            foreach (var e in errors) mvm.LogService.Warning(Describe(e));
            return errors;
         }
         Points = [.. Contour.Points];
         RefreshPlot();
         NotifyContourGeometryChanged();
         mvm.MarkDirty(SaveCategory.Contours);
         MarkSaved();
         OnPropertyChanged(nameof(IsSaved));
         mvm.LogService.Info(string.Format(Loc.S(isNew ? "ContourSavedNewLog" : "ContourSavedLog"), Tag));
         return [];
      }

      void Save(object? o = null) => Save();

      /// <summary>Текст ошибки для журнала: «Вершина N: …» для вершин таблицы.</summary>
      static string Describe(FieldError e)
      {
         if (e.Path.StartsWith("points[") && int.TryParse(e.Path[7..^1], out int i))
            return string.Format(Loc.S("ContourErrVertex"), i + 1, e.Message);
         return e.Message;
      }

      bool IUnsavedChanges.SaveChanges() => Save().Count == 0;

      /// <summary>Возвращает обозначение, набор, тип и вершины к записанному состоянию.</summary>
      void IUnsavedChanges.DiscardChanges()
      {
         Contour.Tag = _saved.Tag;
         Contour.GeometrySet = _saved.Set;
         Contour.Type = _saved.Type;
         var pts = new ObservableCollection<StressPoint>();
         int k = 1;
         foreach (var (x, y) in _saved.Points)
            pts.Add(new StressPoint(x, y) { Num = k++, Contour = Contour });
         Contour.Points = pts;
         WirePointsCollection();
         if (Contour.Points.Count >= 4)
         {
            Contour.PointsToXYs();
            Contour.SetWKT();
         }
         OnPropertyChanged(string.Empty);
         RefreshPlot();
      }

   }
}
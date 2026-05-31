using CScore;

using CsvHelper;
using CsvHelper.Configuration;

using OpenCS.Services;
using OpenCS.Utilites;

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCS.ViewModels
{
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
      StressPoint point;

      /// <summary>Кэш отрисованного изображения контура.</summary>
      DrawingImage dI;

      /// <summary>Флаг, указывающий, находится ли контур в режиме редактирования.</summary>
      bool isEdit;

      /// <summary>
      /// Доменный объект контура, содержащий геометрию и метаданные.
      /// Изменения свойств ViewModel проксируются в этот объект.
      /// </summary>
      public Contour Contour { get; set; } = new();

      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к базе данных, сервисам логирования и файловых диалогов.
      /// </summary>
      public AppViewModel mvm { get; set; }

      /// <summary>
      /// Список допустимых типов контура (Оболочка, Отверстие, Нет) для привязки в ComboBox.
      /// </summary>
      public List<ContourType> Types { get; set; }

      /// <summary>
      /// Сервис построения графиков. Используется для визуализации контура и точек
      /// на плоскости с автоматическим масштабированием.
      /// </summary>
      public IPlotService PlotService { get; set; }

      /// <summary>
      /// Флаг, указывающий, сохранён ли контур в базу данных.
      /// Используется для различения операций создания и обновления.
      /// </summary>
      public bool IsSaved {  get; set; }

      /// <summary>
      /// Флаг режима редактирования контура. При установке значения
      /// вызывается <c>OnPropertyChanged()</c> для обновления привязки.
      /// </summary>
      public bool IsEdit { get => isEdit; set { isEdit = value; OnPropertyChanged(); } }

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
      /// Коллекция областей материалов, привязанных к контуру. При изменении
      /// сбрасывает описание. Используется для привязки в ListBox областей.
      /// </summary>
      public ObservableCollection<Region> Regions
      {
         get => Contour.Regions;
         set { Contour.Regions = value; OnPropertyChanged(); Description = ""; }
      }

      /// <summary>
      /// Имя набора геометрии (GeometrySet), к которому принадлежит контур.
      /// При изменении сбрасывает описание.
      /// </summary>
      public string Set
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
      public ObservableCollection<StressPoint> Points { get => Contour.Points; set { Contour.Points = value; OnPropertyChanged(); } }

      /// <summary>
      /// Выбранная точка контура в ListBox. Используется для редактирования
      /// координат отдельной точки.
      /// </summary>
      public StressPoint Point { get => point; set { point = value; OnPropertyChanged(); } }

      /// <summary>
      /// Изображение контура (DrawingImage), получаемое путём вызова
      /// <see cref="Contour.Draw()"/>. Используется для привязки в представлении.
      /// </summary>
      public DrawingImage DI { get { return Contour.Draw(); } }

      /// <summary>
      /// Команда привязки для перенумерации точек контура.
      /// Вызывает метод <c>RenumPoint</c>.
      /// </summary>
      public ICommand RenumPointCommand { get; set; }

      /// <summary>
      /// Команда привязки для импорта координат точек из CSV-файла.
      /// Вызывает метод <c>ImportCsv</c>.
      /// </summary>
      public ICommand ImportCsvCommand { get; set; }

      /// <summary>
      /// Команда привязки для экспорта координат точек в CSV-файл.
      /// Вызывает метод <c>ExportCsv</c>.
      /// </summary>
      public ICommand ExportCsvCommand { get; set; }

      /// <summary>
      /// Команда привязки для переключения режима редактирования контура.
      /// Вызывает метод <c>SaveChanges</c>.
      /// </summary>
      public ICommand SaveChangesCommand { get; set; }

      /// <summary>
      /// Команда привязки для сохранения контура в базу данных.
      /// Вызывает метод <c>Save</c>.
      /// </summary>
      public ICommand SaveCommand { get; set; }

      /// <summary>
      /// Инициализирует экземпляр <see cref="ContourVM"/> с пустым контуром
      /// и создаёт все команды привязки.
      /// </summary>
      public ContourVM()
      {
         Types = [ContourType.Hull, ContourType.Hole, ContourType.None];

         RenumPointCommand = new RelayCommand(RenumPoint);
         ExportCsvCommand = new RelayCommand(ExportCsv);
         ImportCsvCommand = new RelayCommand(ImportCsv);
         SaveChangesCommand = new RelayCommand(SaveChanges);
         SaveCommand = new RelayCommand(Save);
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
         ExportCsvCommand = new RelayCommand(ExportCsv);
         ImportCsvCommand = new RelayCommand(ImportCsv);
         SaveChangesCommand = new RelayCommand(SaveChanges);
         SaveCommand = new RelayCommand(Save);
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
            title: "Экспорт координат в файл csv");

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
            title: "Импорт координат из файла csv");

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
         Points = [.. points];
         Contour = new Contour(points, "");

         PlotService.Clear();
         PlotService.AddScatter(Contour.X.ToArray(), Contour.Y.ToArray(), lineWidth: 2);
         PlotService.EnableSquareAxes();
         PlotService.AutoScale();
         PlotService.Refresh();
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
         if (IsSaved)
         {
            mvm.db.SaveContour(Contour);
            mvm.ContoursRenumber();

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
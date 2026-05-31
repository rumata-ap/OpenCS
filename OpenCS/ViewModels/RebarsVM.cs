using CScore;
using OpenCS.Utilites;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Модель представления для управления арматурными стержнями.
   /// Обеспечивает создание, конвертацию из окружностей и сохранение
   /// арматурных стержней в базу данных. Работает в связке с представлением <see cref="RebarsPage"/>.
   /// </summary>
   public class RebarsVM : ViewModelBase
   {
      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к коллекциям окружностей и базе данных.
      /// </summary>
      AppViewModel mvm;

      /// <summary>
      /// Ссылка на главную ViewModel приложения. При установке значения
      /// загружает коллекцию окружностей. Используется для привязки в представлении.
      /// </summary>
      public AppViewModel MVM { get => mvm; set { mvm = value; Circles = value.Circles; } }

      /// <summary>Счётчик для автоматической нумерации арматурных стержней.</summary>
      int ii = 1;

      /// <summary>Координата X центра арматурного стержня.</summary>
      double x;

      /// <summary>Координата Y центра арматурного стержня.</summary>
      double y;

      /// <summary>Диаметр арматурного стержня (м).</summary>
      double d = 0.01;

      /// <summary>Наименование (тег) арматурного стержня.</summary>
      string tag;

      /// <summary>Имя набора геометрии, к которому принадлежит стержень.</summary>
      string set = "No Geometry Set";

      /// <summary>Выбранный арматурный стержень в ListBox.</summary>
      ReBar rebar;

      /// <summary>Выбранная окружность в ListBox.</summary>
      CircleP circle;

      /// <summary>Коллекция арматурных стержней для добавления в проект.</summary>
      ObservableCollection<ReBar> rebars = [];

      /// <summary>Коллекция окружностей, загруженных из главной ViewModel.</summary>
      ObservableCollection<CircleP> circles = [];

      /// <summary>
      /// Выбранная окружность в ListBox. Используется для привязки в представлении.
      /// </summary>
      public CircleP Circle
      {
         get { return circle; }
         set { circle = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Выбранный арматурный стержень в ListBox. Используется для привязки в представлении.
      /// </summary>
      public ReBar Rebar
      {
         get { return rebar; }
         set { rebar = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Коллекция арматурных стержней для добавления в проект.
      /// Привязана к ListBox в представлении.
      /// </summary>
      public ObservableCollection<ReBar> ReBars
      {
         get { return rebars; }
         set { rebars = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Коллекция окружностей, доступных для конвертации в арматурные стержни.
      /// Привязана к ListBox в представлении.
      /// </summary>
      public ObservableCollection<CircleP> Circles
      {
         get { return circles; }
         set { circles = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Координата X центра арматурного стержня. Используется для привязки в TextBox.
      /// </summary>
      public double X
      {
         get { return x; }
         set { x = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Координата Y центра арматурного стержня. Используется для привязки в TextBox.
      /// </summary>
      public double Y
      {
         get { return y; }
         set { y = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Диаметр арматурного стержня (м). Используется для привязки в TextBox.
      /// </summary>
      public double D
      {
         get { return d; }
         set { d = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Наименование (тег) арматурного стержня. Формируется автоматически
      /// как «d={D}мм». Используется для привязки в представлении.
      /// </summary>
      public string Tag
      {
         get { return $"d={d}мм"; }
         set { tag = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Имя набора геометрии арматурного стержня. Используется для привязки в представлении.
      /// </summary>
      public string Set
      {
         get { return set; }
         set { set = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Ссылка на ListBox окружностей в представлении. Используется
      /// для получения выбранных элементов при конвертации.
      /// </summary>
      internal ListBox CirclesListBox { get; set; }

      /// <summary>
      /// Ссылка на ListBox арматурных стержней в представлении. Используется
      /// для получения выбранных элементов при удалении.
      /// </summary>
      internal ListBox RebarsListBox { get; set; }

      /// <summary>Команда привязки для конвертации выбранных окружностей в арматурные стержни.</summary>
      public ICommand CirclesToRebarsCommand { get; set; }

      /// <summary>Команда привязки для конвертации всех окружностей в арматурные стержни.</summary>
      public ICommand CirclesAllToRebarsCommand { get; set; }

      /// <summary>Команда привязки для сохранения арматурных стержней в базу данных проекта.</summary>
      public ICommand AddCommand { get; set; }

      /// <summary>Команда привязки для добавления окружности как арматурного стержня в базу данных.</summary>
      public ICommand AddCircleCommand { get; set; }

      /// <summary>Команда привязки для удаления выбранных арматурных стержней из списка.</summary>
      public ICommand DeleteCommand { get; set; }

      /// <summary>Команда привязки для удаления всех арматурных стержней из списка.</summary>
      public ICommand DeleteAllCommand { get; set; }

      /// <summary>
      /// Инициализирует экземпляр <see cref="RebarsVM"/> без главной ViewModel
      /// и создаёт все команды привязки.
      /// </summary>
      public RebarsVM()
      {
         CirclesAllToRebarsCommand = new RelayCommand(CirclesAllToRebars);
         CirclesToRebarsCommand = new RelayCommand(CirclesToRebars);
         AddCommand = new RelayCommand(Add);
         AddCircleCommand = new RelayCommand(AddCircle);
         DeleteCommand = new RelayCommand(Delete);
         DeleteAllCommand = new RelayCommand(DeleteAll);
      }

      /// <summary>
      /// Инициализирует экземпляр <see cref="RebarsVM"/> с заданной главной ViewModel
      /// и создаёт все команды привязки.
      /// </summary>
      /// <param name="mvm">Главная ViewModel приложения для доступа к коллекциям и базе данных.</param>
      public RebarsVM(AppViewModel mvm)
      {
         this.MVM = mvm;
         CirclesAllToRebarsCommand = new RelayCommand(CirclesAllToRebars);
         CirclesToRebarsCommand = new RelayCommand(CirclesToRebars);
         AddCommand = new RelayCommand(Add);
         AddCircleCommand = new RelayCommand(AddCircle);
         DeleteCommand = new RelayCommand(Delete);
         DeleteAllCommand = new RelayCommand(DeleteAll);
      }

      /// <summary>Удаляет все арматурные стержни из локального списка.</summary>
      void DeleteAll(object? o = null)
      {
         ReBars.Clear();
      }
      /// <summary>Удаляет выбранные в ListBox арматурные стержни из локального списка.</summary>
      void Delete(object? o = null)
      {
         if (RebarsListBox.SelectedItems == null) return;
         if (RebarsListBox.SelectedItems.Count == 0) return;
         List<ReBar> rs = [];
         foreach (var item in RebarsListBox.SelectedItems)
         {
            rs.Add((ReBar)item);
         }
         ReBars.RemoveRange(rs);
      }
      /// <summary>Создаёт окружность с текущими координатами и диаметром и сохраняет её в базу данных.</summary>
      void AddCircle(object? o = null)
      {
         CircleP c = new(x, y, 0.5 * d)
         {
            Tag = Tag,
            GeometrySet = set
         };
         MVM.db.AddCircle(c);
      }
      /// <summary>Конвертирует все окружности из коллекции главной ViewModel в арматурные стержни.</summary>
      void CirclesAllToRebars(object? o = null)
      {
         if(MVM.Circles == null) return;
         if(MVM.Circles.Count == 0) return;
         for (int i = 0; i < MVM.Circles.Count; i++)
         {
            CircleP? c = MVM.Circles[i];
            rebars.Add(new ReBar(c)
            {
               Num = ii,
               Tag = c.Tag
            });
            ii++;
         }
      }
      /// <summary>Конвертирует выбранные в ListBox окружности в арматурные стержни.</summary>
      void CirclesToRebars(object? o = null)
      {
         if(CirclesListBox.SelectedItems == null) return;
         if(CirclesListBox.SelectedItems.Count == 0) return;
         for (int i = 0; i < CirclesListBox.SelectedItems.Count; i++)
         {
            CircleP? c = (CircleP?)CirclesListBox.SelectedItems[i];
            rebars.Add(new ReBar(c)
            {
               Num = ii,
               Tag = c.Tag
            });
            ii++;
         }
      }
      /// <summary>Сохраняет арматурные стержни из локального списка в базу данных проекта.</summary>
      void Add(object? o = null)
      {
         if (rebars == null || rebars.Count == 0) return;

         if (MVM.Rebars.Any())
         {
            List<ReBar> list = new(rebars.Count);
            for (int i = 0; i < rebars.Count; i++)
            {
               if (!MVM.Rebars.Contains(rebars[i]))
                  list.Add(rebars[i]);

               if (list.Count > 0)
               {
                  foreach (var rb in list) MVM.Rebars.Add(rb);

                  MVM.LogService.Info($"В проект добавлено {list.Count} арматурных стержней");
               }
            }
         }
         else
         {
            foreach (var rb in rebars) MVM.Rebars.Add(rb);

            MVM.LogService.Info($"В проект добавлено {rebars.Count} арматурных стержней");
         }
      }
   }
}
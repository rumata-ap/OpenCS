using CScore;


using OpenCS.Utilites;
using OpenCS.Views;

using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Модель представления материала. Обеспечивает привязку данных доменного объекта
   /// <see cref="Material"/> к элементам управления WPF, управляет операциями
   /// добавления, редактирования и загрузки материала из справочника.
   /// Используется в представлениях <see cref="MaterialPage"/> и <see cref="FromDataSourceWindow"/>.
   /// </summary>
   public class MaterialVM : ViewModelBase
   {
      /// <summary>
      /// Ссылка на главную ViewModel приложения. Используется для доступа
      /// к базе данных и сервису логирования.
      /// </summary>
      public AppViewModel mvm = null!;

      /// <summary>
      /// Доменный объект материала, содержащий характеристики по всем видам расчётов.
      /// </summary>
      Material material = new();

      /// <summary>
      /// Доменный объект материала. Проксирует все свойства ViewModel
      /// к объекту <see cref="Material"/> из доменной модели.
      /// </summary>
      public Material Material
      {
         get { return material; }
         set { material = value;}
      }

      /// <summary>
      /// Инициализирует экземпляр <see cref="MaterialVM"/> с новым пустым материалом
      /// и создаёт все команды привязки.
      /// </summary>
      public MaterialVM()
      {
         material = new Material();
         AddCommand = new RelayCommand(Add);
         EditCommand = new RelayCommand(Edit);
         DataSourceCommand = new RelayCommand(DataSource);
      }

      /// <summary>
      /// Флаг, указывающий, сохранён ли материал в базу данных.
      /// Используется для различения операций добавления и редактирования.
      /// </summary>
      public bool IsSaved {  get; set; }

      /// <summary>
      /// Команда привязки для добавления нового материала в базу данных.
      /// Вызывает метод <c>Add</c>.
      /// </summary>
      public ICommand AddCommand { get; set; }

      /// <summary>
      /// Команда привязки для сохранения изменений существующего материала.
      /// Вызывает метод <c>Edit</c>.
      /// </summary>
      public ICommand EditCommand { get; set; }

      /// <summary>
      /// Команда привязки для удаления материала (не реализована в данном классе).
      /// </summary>
      public ICommand DelCommand { get; set; }

      /// <summary>
      /// Команда привязки для загрузки характеристик материала из справочника (CSV).
      /// Открывает диалоговое окно <see cref="FromDataSourceWindow"/>.
      /// </summary>
      public ICommand DataSourceCommand { get; set; }

      /// <summary>
      /// Добавляет новый материал в базу данных. Проверяет, что материал ещё не сохранён
      /// и что тип материала задан. После сохранения устанавливает <see cref="IsSaved"/> в true.
      /// </summary>
      private void Add(object? obj)
      {
         if (IsSaved)
         {
            mvm.LogService.Warning($"Материал '{material.Tag}' уже сохранен");
         }
         else
         {
            material.SetJson();
            if (Type==MatType.None)
            {
               mvm.LogService.Error("Не задан тип материала. Материал не сохранен");
               return;
            }
            mvm.db.AddMaterial(material);
            IsSaved = true;

            mvm.LogService.Info($"Материал '{material.Tag}' успешно сохранен");
         }
      }
      /// <summary>
      /// Сохраняет изменения существующего материала в базу данных.
      /// Проверяет, что материал был предварительно сохранён.
      /// </summary>
      private void Edit(object? obj)
      {
         if (!IsSaved)
         {
            mvm.LogService.Warning($"Сначала нужно сохранить материал. Материал '{material.Tag}' не изменен");
         }
         else
         {
            material.SetJson();
            mvm.db.SaveMaterial(material);

            mvm.MaterialsSort();

            mvm.LogService.Info($"Материал '{material.Tag}' успешно изменен");
         }
      }

      /// <summary>
      /// Открывает диалоговое окно загрузки характеристик материала из справочника (CSV).
      /// Сбрасывает текущее состояние ViewModel перед открытием.
      /// </summary>
      private void DataSource(object? obj)
      {
         Reset();
         FromDataSourceWindow window = new(this);
         window.ShowDialog();
      }

      /// <summary>
      /// Сбрасывает ViewModel в начальное состояние: очищает флаг сохранения,
      /// создаёт новый пустой материал и сбрасывает все свойства.
      /// </summary>
      void Reset()
      {
         IsSaved = false;
         Material = new Material(0);
         Tag = ""; Description = ""; Type = MatType.None; AggregateType = "silicate";
      }

      /// <summary>
      /// Наименование (тег) материала. Проксирует <see cref="Material.Tag"/>
      /// с уведомлением об изменении.
      /// </summary>
      public string Tag
      {
         get { return material.Tag; }
         set { material.Tag = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Описание материала. Проксирует <see cref="Material.Description"/>
      /// с уведомлением об изменении.
      /// </summary>
      public string Description
      {
         get { return material.Description;}
         set { material.Description = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Модуль упругости материала (E). Проксирует <see cref="Material.E"/>
      /// с уведомлением об изменении.
      /// </summary>
      public double E
      {
         get => material.E;
         set { material.E = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Характеристики материала для расчёта на продолжительное действие нагрузки (C).
      /// Проксирует <see cref="Material.C"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars C
      {
         get { return material.C; }
         set { material.C = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Характеристики материала для расчёта на продолжительное действие длительной нагрузки (CL).
      /// Проксирует <see cref="Material.CL"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars CL
      {
         get { return material.CL; }
         set { material.CL = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Характеристики материала для расчёта на кратковременное действие нагрузки (N).
      /// Проксирует <see cref="Material.N"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars N
      {
         get { return material.N; }
         set { material.N = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Характеристики материала для расчёта на кратковременное действие длительной нагрузки (NL).
      /// Проксирует <see cref="Material.NL"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars NL
      {
         get { return material.NL; }
         set { material.NL = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Тип материала (Бетон, Арматурная сталь, Сталь). Проксирует <see cref="Material.Type"/>
      /// с уведомлением об изменении. Используется для привязки в ComboBox типа материала.
      /// </summary>
      public MatType Type
      {
         get { return material.Type; }
         set { material.Type = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsConcrete)); }
      }

      /// <summary>
      /// Признак бетона — для отображения полей, специфичных для бетона.
      /// </summary>
      public bool IsConcrete => material.Type == MatType.Concrete;

      /// <summary>
      /// Тип заполнителя бетона для огнестойкости: silicate, carbonate, lightweight.
      /// </summary>
      public string AggregateType
      {
         get => material.AggregateType;
         set { material.AggregateType = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Сериализует характеристики материала (C, CL, N, NL) в JSON-строку
      /// внутри объекта <see cref="Material"/> для сохранения в базе данных.
      /// </summary>
      public void SetJson()
      {
         material.SetJson();
      }
   }
}
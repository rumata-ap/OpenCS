using CsvHelper;
using CsvHelper.Configuration;
using CScore;
using OpenCS.Utilites;
using OpenCS.Views;

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
         set
         {
            material = value;
            OnPropertyChanged(nameof(ConcreteDampness));
         }
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
         DelCommand = new RelayCommand(_ => { });
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
         material.BaseType = MatType.None;
         material.CustomDiagramIds = [];
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
         get { return material.C!; }
         set { material.C = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Характеристики материала для расчёта на продолжительное действие длительной нагрузки (CL).
      /// Проксирует <see cref="Material.CL"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars CL
      {
         get { return material.CL!; }
         set { material.CL = value; OnPropertyChanged(); }
      }
      /// <summary>
      /// Характеристики материала для расчёта на кратковременное действие нагрузки (N).
      /// Проксирует <see cref="Material.N"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars N
      {
         get { return material.N!; }
         set
         {
            material.N = value;
            OnPropertyChanged();
         }
      }
      /// <summary>
      /// Характеристики материала для расчёта на кратковременное действие длительной нагрузки (NL).
      /// Проксирует <see cref="Material.NL"/> с уведомлением об изменении.
      /// </summary>
      public MaterialChars NL
      {
         get { return material.NL!; }
         set
         {
            material.NL = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConcreteDampness));
         }
      }

      /// <summary>
      /// Влажность среды для бетонного набора NL. При изменении в UI
      /// подгружает соответствующую строку СП63-справочника и обновляет
      /// деформационные параметры набора <see cref="NL"/>.
      /// </summary>
      public Dampness ConcreteDampness
      {
         get
         {
            var dampness = material.NL?.Dampness ?? Dampness.от40_до70;
            return dampness == Dampness.any ? Dampness.от40_до70 : dampness;
         }
         set
         {
            var normalized = value == Dampness.any ? Dampness.от40_до70 : value;
            if (material.NL == null)
               material.NL = new MaterialChars(CalcType.NL);

            material.NL.Dampness = normalized;
            if (IsConcrete)
               TryRefreshConcreteNlByDampness();

            OnPropertyChanged();
            OnPropertyChanged(nameof(NL));
         }
      }

      /// <summary>
      /// Тип материала (Бетон, Арматурная сталь, Сталь). Проксирует <see cref="Material.Type"/>
      /// с уведомлением об изменении. Используется для привязки в ComboBox типа материала.
      /// </summary>
      public MatType Type
      {
         get { return material.Type; }
         set
         {
            material.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConcrete));
            OnPropertyChanged(nameof(IsCustom));
         }
      }

      /// <summary>true если материал — Custom-тип.</summary>
      public bool IsCustom => material.Type == MatType.Custom;

      /// <summary>Базовый тип поведения σ(ε) для Custom-материала.</summary>
      public MatType BaseType
      {
         get => material.BaseType;
         set { material.BaseType = value; OnPropertyChanged(); }
      }

      /// <summary>Словарь Id диаграмм по видам расчёта для Custom-материала.</summary>
      public Dictionary<CScore.CalcType, int> CustomDiagramIds => material.CustomDiagramIds;

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

      bool TryRefreshConcreteNlByDampness()
      {
         if (!IsConcrete || material.N == null || material.NL == null)
            return false;

         string? prefix = ResolveConcreteCatalogPrefix();
         if (prefix == null)
            return false;

         string? tag = FirstNonEmpty(material.NL.Tag, material.N.Tag, material.C?.Tag, material.Tag);
         double classValue = material.NL.Class > 0 ? material.NL.Class
            : material.N.Class > 0 ? material.N.Class
            : material.C?.Class ?? 0;

         var sourceN = LoadConcreteChars(prefix, CalcType.N, Dampness.any, tag, classValue);
         var sourceNl = LoadConcreteChars(prefix, CalcType.NL, material.NL.Dampness, tag, classValue);
         if (sourceN == null || sourceNl == null)
            return false;

         double kE = sourceN.E > 0 ? material.N.E / sourceN.E : 1.0;
         if (!double.IsFinite(kE) || kE <= 0)
            kE = 1.0;

         var refreshed = sourceNl.Clone();
         refreshed.E *= kE;
         if (refreshed.E != 0)
         {
            refreshed.Et1 = 0.6 * refreshed.Ft / refreshed.E;
            refreshed.Ec1 = 0.6 * refreshed.Fc / refreshed.E;
         }

         NL = refreshed;
         return true;
      }

      string? ResolveConcreteCatalogPrefix()
      {
         string description = material.Description ?? "";
         if (description.Contains("Бетон тяжел", System.StringComparison.OrdinalIgnoreCase))
            return "Бетон_тяжелый";
         if (description.Contains("группы А", System.StringComparison.OrdinalIgnoreCase))
            return "Мелкозернистый группы А";
         if (description.Contains("группы Б", System.StringComparison.OrdinalIgnoreCase))
            return "Мелкозернистый группы Б";

         string? tag = FirstNonEmpty(material.N?.Tag, material.C?.Tag, material.Tag);
         double classValue = material.N?.Class > 0 ? material.N.Class
            : material.C?.Class ?? 0;
         double currentE = material.N?.E ?? 0;

         string[] prefixes =
         [
            "Бетон_тяжелый",
            "Мелкозернистый группы А",
            "Мелкозернистый группы Б"
         ];

         string? bestPrefix = null;
         double bestDiff = double.MaxValue;

         foreach (var prefix in prefixes)
         {
            var candidate = LoadConcreteChars(prefix, CalcType.N, Dampness.any, tag, classValue);
            if (candidate == null)
               continue;

            double diff = currentE > 0 && candidate.E > 0
               ? System.Math.Abs(candidate.E - currentE) / currentE
               : 0.0;

            if (diff < bestDiff)
            {
               bestDiff = diff;
               bestPrefix = prefix;
            }
         }

         return bestPrefix;
      }

      static string? FirstNonEmpty(params string?[] values)
         => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

      static MaterialChars? LoadConcreteChars(string prefix, CalcType calcType, Dampness dampness, string? tag, double classValue)
      {
         string? fileName = calcType switch
         {
            CalcType.N => $"{prefix}_N.csv",
            CalcType.NL => $"{prefix}_{dampness switch
            {
               Dampness.ниже_40 => "NL_1",
               Dampness.свыше_70 => "NL_3",
               _ => "NL_2"
            }}.csv",
            _ => null
         };
         if (fileName == null)
            return null;

         string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DataSource", fileName);
         if (!File.Exists(filePath))
            return null;

         var config = new CsvConfiguration(CultureInfo.InvariantCulture)
         {
            Delimiter = ";"
         };

         using var reader = new StreamReader(filePath);
         using var csv = new CsvReader(reader, config);
         csv.Context.RegisterClassMap<MaterialCharsMap>();
         var rows = csv.GetRecords<MaterialChars>().ToList();

         if (!string.IsNullOrWhiteSpace(tag))
         {
            var byTag = rows.FirstOrDefault(r => string.Equals(r.Tag, tag, System.StringComparison.OrdinalIgnoreCase));
            if (byTag != null)
               return byTag;
         }

         if (classValue > 0)
         {
            var byClass = rows.FirstOrDefault(r => System.Math.Abs(r.Class - classValue) < 1e-9);
            if (byClass != null)
               return byClass;

            return rows.OrderBy(r => System.Math.Abs(r.Class - classValue)).FirstOrDefault();
         }

         return null;
      }
   }
}
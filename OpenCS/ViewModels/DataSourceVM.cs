using CScore;

using CsvHelper.Configuration;
using CsvHelper;

using OpenCS.Services;
using OpenCS.Utilites;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Модель представления для выбора характеристик материала из справочника (CSV-файлов).
   /// Управляет вкладками типов материалов (бетон, арматурная сталь, сталь конструкций),
   /// коэффициентами условий работы и загрузкой данных из CSV-файлов в соответствии
   /// с СП 63.13330. Работает в связке с <see cref="MaterialVM"/> и представлением FromDataSourceWindow.
   /// </summary>
   public class DataSourceVM : ViewModelBase
   {
      /// <summary>Список характеристик материала для расчёта на продолжительное действие нагрузки (C).</summary>
      List<MaterialChars> c = new();

      /// <summary>Список характеристик материала для расчёта на продолжительное действие длительной нагрузки (CL).</summary>
      List<MaterialChars> cl = new();

      /// <summary>Список характеристик материала для расчёта на кратковременное действие нагрузки (N).</summary>
      List<MaterialChars> n = new();

      /// <summary>Список характеристик материала для расчёта на кратковременное действие длительной нагрузки (NL).</summary>
      List<MaterialChars> nl = new();

      /// <summary>Выбранная строка характеристик материала в таблице справочника.</summary>
      MaterialChars? selectedMaterial;

      /// <summary>Коэффициент условий работы бетона γb2 к Rb: 0,9 для бетонных конструкций, 1,0 для железобетонных (п. 6.1.12 б СП 63).</summary>
      double gb2 = 1;

      /// <summary>Коэффициент условий работы бетона γb3 к Rb: 0,85 при бетонировании в вертикальном положении слоем более 1,5 м (п. 6.1.12 в СП 63).</summary>
      double gb3 = 1;

      /// <summary>Коэффициент условий работы арматуры kE (0.89 при термообработке).</summary>
      double kE = 1;

      /// <summary>Флаг: выбрана ли вкладка «Бетон».</summary>
      bool concreteTabIsSelected = true;

      /// <summary>Флаг: выбрана ли вкладка «Арматурная сталь».</summary>
      bool rfsteelTabIsSelected;

      /// <summary>Флаг: выбрана ли вкладка «Сталь конструкций».</summary>
      bool steelTabIsSelected;

      /// <summary>Флаг: учитывается ли коэффициент gb3 при высоте сечения более 1.5 м.</summary>
      bool isH1_5 = false;

      /// <summary>Флаг: учитывается ли коэффициент kE для термообработанной арматуры.</summary>
      bool isTerm = false;

      /// <summary>Флаг: доступен ли переключатель термообработки (только для мелкозернистого бетона).</summary>
      bool isTermEnable = false;

      /// <summary>Тип конструкции: 0 — железобетонная (gb2=0.9), 1 — бетонная (gb2=1.0).</summary>
      int isRC = 0;

      /// <summary>Индекс типа бетона: 0 — тяжёлый, 1 — мелкозернистый группы А, 2 — мелкозернистый группы Б.</summary>
      int concreteTypeIndex = 0;

      /// <summary>Индекс типа арматурной стали: 0 — стальная, 1 — полимерная композитная.</summary>
      int rfSteelTypeIndex = 0;

      /// <summary>Полный список записей стали: (C-запись, N-запись, тип проката).</summary>
      List<(MaterialChars mcC, MaterialChars mcN, string prokatType)> steelAll = new();

      /// <summary>Список типов проката для ComboBox (уникальные, отсортированные).</summary>
      List<string> steelProkatTypes = new();

      /// <summary>Список марок стали для ComboBox (отфильтрованные по типу проката).</summary>
      List<string> steelMarks = new();

      /// <summary>Индекс выбранного типа проката.</summary>
      int steelProkatTypeIndex = -1;

      /// <summary>Индекс выбранной марки стали.</summary>
      int steelMarkIndex = -1;

      /// <summary>
      /// Флаг учёта коэффициента gb3 для высоты сечения более 1.5 м.
      /// </summary>
      public bool IsH1_5
      {
         get { return isH1_5; }
         set { isH1_5 = value; gb3 = value ? 0.85 : 1; OnPropertyChanged(); SelectMaterial(); }
      }

      /// <summary>
      /// Флаг учёта коэффициента kE для термообработанной арматуры.
      /// </summary>
      public bool IsTerm
      {
         get { return isTerm; }
         set { isTerm = value; kE = value ? 0.89 : 1; OnPropertyChanged(); SelectMaterial(); }
      }

      /// <summary>
      /// Флаг доступности переключателя термообработки.
      /// </summary>
      public bool IsTermEnable
      {
         get { return isTermEnable; }
         set { isTermEnable = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Тип конструкции: 0 — железобетонная (γb2 = 1), 1 — бетонная (γb2 = 0,9, п. 6.1.12 б СП 63).
      /// Смена типа пересчитывает уже выбранный класс бетона: раньше γb2 вводился только
      /// при следующем щелчке по классу, а повторный щелчок по тому же классу выбор не менял.
      /// </summary>
      public int IsRC
      {
         get { return isRC; }
         set { isRC = value; gb2 = value == 1 ? 0.9 : 1; OnPropertyChanged(); SelectMaterial(); }
      }

      /// <summary>
      /// Индекс типа бетона. Смена типа перечитывает таблицу классов (тяжёлый/мелкозернистый)
      /// и сбрасывает выбор класса.
      /// </summary>
      public int ConcreteTypeIndex
      {
         get { return concreteTypeIndex; }
         set
         {
            if (concreteTypeIndex == value) return;
            concreteTypeIndex = value;
            IsTermEnable = value == 1;
            if (!IsTermEnable && isTerm) { isTerm = false; kE = 1; OnPropertyChanged(nameof(IsTerm)); }
            OnPropertyChanged();
            if (Material != null && concreteTabIsSelected)
            {
               selectedMaterial = null;
               OnPropertyChanged(nameof(SelectedMaterial));
               Select();
            }
         }
      }

      /// <summary>
      /// Индекс типа арматурной стали.
      /// </summary>
      public int RfSteelTypeIndex
      {
         get { return rfSteelTypeIndex; }
         set { rfSteelTypeIndex = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Список характеристик материала для привязки ListBox.
      /// </summary>
      public List<MaterialChars> N
      {
         get { return n; }
         set { n = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранная строка характеристик материала.
      /// </summary>
      public MaterialChars? SelectedMaterial
      {
         get { return selectedMaterial; }
         set { selectedMaterial = value; SelectMaterial(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг выбора вкладки «Бетон».
      /// </summary>
      public bool ConcreteTabIsSelected
      {
         get { return concreteTabIsSelected; }
         set
         {
            concreteTabIsSelected = value;
            if (value) { rfsteelTabIsSelected = false; steelTabIsSelected = false; }
            Select(); OnPropertyChanged();
            OnPropertyChanged(nameof(RfsteelTabIsSelected));
            OnPropertyChanged(nameof(SteelTabIsSelected));
         }
      }

      /// <summary>
      /// Флаг выбора вкладки «Арматурная сталь».
      /// </summary>
      public bool RfsteelTabIsSelected
      {
         get { return rfsteelTabIsSelected; }
         set
         {
            rfsteelTabIsSelected = value;
            if (value) { concreteTabIsSelected = false; steelTabIsSelected = false; }
            Select(); OnPropertyChanged();
            OnPropertyChanged(nameof(ConcreteTabIsSelected));
            OnPropertyChanged(nameof(SteelTabIsSelected));
         }
      }

      /// <summary>
      /// Флаг выбора вкладки «Сталь конструкций».
      /// </summary>
      public bool SteelTabIsSelected
      {
         get { return steelTabIsSelected; }
         set
         {
            steelTabIsSelected = value;
            if (value) { concreteTabIsSelected = false; rfsteelTabIsSelected = false; }
            Select(); OnPropertyChanged();
            OnPropertyChanged(nameof(ConcreteTabIsSelected));
            OnPropertyChanged(nameof(RfsteelTabIsSelected));
         }
      }

      /// <summary>Список типов арматурной стали для ComboBox.</summary>
      public List<string> RfSteelTypes { get; set; } = new()
      { "Стальная","Полимерная композитная"};

      /// <summary>Список типов бетона для ComboBox.</summary>
      public List<string> ConcreteTypes { get; set; } = new()
      { "Тяжелый","Мелкозернистый группы А", "Мелкозернистый группы Б"};

      /// <summary>Список типов конструкций для ComboBox.</summary>
      public List<string> ConstructionTypes { get; set; } = new()
      { "Железобетонная","Бетонная" };

      /// <summary>Список норм конструкционной стали для ComboBox.</summary>
      public List<string> SteelNorms { get; set; } = new()
      { "СП 16.13330.2017", "СП 16.13330.2011", "ГОСТ 27772-2015" };

      /// <summary>Индекс выбранной нормы конструкционной стали.</summary>
      int steelNormIndex;
      public int SteelNormIndex
      {
         get { return steelNormIndex; }
         set { steelNormIndex = value; if (steelTabIsSelected) Select(); OnPropertyChanged(); }
      }

      /// <summary>Список типов проката для текущей нормы.</summary>
      public List<string> SteelProkatTypes
      {
         get { return steelProkatTypes; }
         set { steelProkatTypes = value; OnPropertyChanged(); }
      }

      /// <summary>Индекс выбранного типа проката.</summary>
      public int SteelProkatTypeIndex
      {
         get { return steelProkatTypeIndex; }
         set { steelProkatTypeIndex = value; FilterSteelMarks(); OnPropertyChanged(); }
      }

      /// <summary>Список марок стали для текущего типа проката.</summary>
      public List<string> SteelMarks
      {
         get { return steelMarks; }
         set { steelMarks = value; OnPropertyChanged(); }
      }

      /// <summary>Индекс выбранной марки стали.</summary>
      public int SteelMarkIndex
      {
         get { return steelMarkIndex; }
         set { steelMarkIndex = value; FilterSteelRecords(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Ссылка на ViewModel материала.
      /// </summary>
      public MaterialVM Material { get; set; } = null!;



      /// <summary>
      /// Применяет выбранный материал из справочника к ViewModel материала.
      /// </summary>
      public void SelectMaterial()
      {
         if (selectedMaterial == null) return;
         int i = n.IndexOf(selectedMaterial);

         if (rfsteelTabIsSelected && rfSteelTypeIndex == 0)
            Material.Description = "Арматура стальная по СП 63.13330";
         if (rfsteelTabIsSelected && rfSteelTypeIndex == 1)
            Material.Description = "Арматура композитная по ";
         if (steelTabIsSelected)
         {
            var normName = SteelNorms[steelNormIndex];
            var prokatType = steelProkatTypeIndex >= 0 && steelProkatTypeIndex < steelProkatTypes.Count
               ? steelProkatTypes[steelProkatTypeIndex] : "";
            Material.Description = $"Сталь {n[i].Tag} — {prokatType} — {normName}";
         }

         if (concreteTabIsSelected)
         {
            // Общий расчёт со справочником CLI: коэффициенты и их запись в описании (материал хранит уже
            // умноженное Rb, а флаги окна при следующем открытии не восстанавливаются).
            var recipe = new ConcreteRecipe(n[i].Tag, (ConcreteKind)concreteTypeIndex,
               Material.NL?.Dampness ?? Dampness.от40_до70, Plain: isRC == 1, TallLayer: isH1_5,
               HeatTreated: isTerm && isTermEnable);
            var (cc, ccl, cn, cnl) = MaterialCatalog.ApplyConcreteFactors(c[i], cl[i], n[i], nl[i], recipe);
            Material.C = cc;
            Material.CL = ccl;
            Material.N = cn;
            Material.NL = cnl;
            Material.Description = MaterialCatalog.ConcreteDescription(recipe);
         }
         else if (steelTabIsSelected)
         {
            // Для стали C=CL, N=NL
            Material.C = c[i];
            Material.CL = c[i].Clone();
            Material.N = n[i];
            Material.NL = n[i].Clone();
         }
         else
         {
            Material.C = c[i];
            Material.CL = cl[i];
            Material.N = n[i];
            Material.NL = nl[i];
         }


         if (concreteTabIsSelected)
            Material.Type = MatType.Concrete;
         if (rfsteelTabIsSelected)
            Material.Type = n[i].Type;
         if (steelTabIsSelected)
            Material.Type = MatType.Steel;

         Material.Tag = n[i].Tag;
         Material.E = Material.N.E;
      }

      /// <summary>
      /// Загружает данные характеристик материала из CSV-файлов.
      /// </summary>
      public void Select()
      {
         var config = new CsvConfiguration(CultureInfo.InvariantCulture)
         {
            Delimiter = ";"
         };

         string basedir = AppDomain.CurrentDomain.BaseDirectory;
         string dir = "DataSource";
         var fileC = "";
         var fileCL = "";
         var fileN = "";
         var fileNL = "";

         if (concreteTabIsSelected && concreteTypeIndex==0)
         {
            fileC = Path.Combine(basedir, dir, "Бетон_тяжелый_C.csv");
            fileCL = Path.Combine(basedir, dir, "Бетон_тяжелый_CL.csv");
            fileN = Path.Combine(basedir, dir, "Бетон_тяжелый_N.csv");
            fileNL = Path.Combine(basedir, dir, "Бетон_тяжелый_NL_2.csv");
            switch (Material.NL.Dampness)
            {
               case Dampness.ниже_40:
                  fileNL = Path.Combine(basedir, dir, "Бетон_тяжелый_NL_1.csv");
                  break;
               case Dampness.свыше_70:
                  fileNL = Path.Combine(basedir, dir, "Бетон_тяжелый_NL_3.csv");
                  break;
            }
         }
         else if (concreteTabIsSelected && concreteTypeIndex == 1)
         {
            fileC = Path.Combine(basedir, dir, "Мелкозернистый группы А_C.csv");
            fileCL = Path.Combine(basedir, dir, "Мелкозернистый группы А_CL.csv");
            fileN = Path.Combine(basedir, dir, "Мелкозернистый группы А_N.csv");
            fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы А_NL_2.csv");
            switch (Material.NL.Dampness)
            {
               case Dampness.ниже_40:
                  fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы А_NL_1.csv");
                  break;
               case Dampness.свыше_70:
                  fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы А_NL_3.csv");
                  break;
            }
         }
         else if (concreteTabIsSelected && concreteTypeIndex == 2)
         {
            fileC = Path.Combine(basedir, dir, "Мелкозернистый группы Б_C.csv");
            fileCL = Path.Combine(basedir, dir, "Мелкозернистый группы Б_CL.csv");
            fileN = Path.Combine(basedir, dir, "Мелкозернистый группы Б_N.csv");
            fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы Б_NL_2.csv");
            switch (Material.NL.Dampness)
            {
               case Dampness.ниже_40:
                  fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы Б_NL_1.csv");
                  break;
               case Dampness.свыше_70:
                  fileNL = Path.Combine(basedir, dir, "Мелкозернистый группы Б_NL_3.csv");
                  break;
            }
         }
         else if (rfsteelTabIsSelected)
         {
            fileC = Path.Combine(basedir, dir, "Арматура стальная_C.csv");
            fileCL = Path.Combine(basedir, dir, "Арматура стальная_CL.csv");
            fileN = Path.Combine(basedir, dir, "Арматура стальная_N.csv");
            fileNL = Path.Combine(basedir, dir, "Арматура стальная_NL.csv");
         }
         else if (steelTabIsSelected)
         {
            LoadSteel(basedir, dir, config);
            return;
         }

         if (string.IsNullOrEmpty(fileC)) return;

         using (var reader = new StreamReader(fileC))
         using (var csv = new CsvReader(reader, config))
         {
            csv.Context.RegisterClassMap<MaterialCharsMap>();
            c = csv.GetRecords<MaterialChars>().ToList();
         }

         using (var reader = new StreamReader(fileCL))
         using (var csv = new CsvReader(reader, config))
         {
            csv.Context.RegisterClassMap<MaterialCharsMap>();
            cl = csv.GetRecords<MaterialChars>().ToList();
         }

         using (var reader = new StreamReader(fileN))
         using (var csv = new CsvReader(reader, config))
         {
            csv.Context.RegisterClassMap<MaterialCharsMap>();
            n = csv.GetRecords<MaterialChars>().ToList();
         }

         using (var reader = new StreamReader(fileNL))
         using (var csv = new CsvReader(reader, config))
         {
            csv.Context.RegisterClassMap<MaterialCharsMap>();
            nl = csv.GetRecords<MaterialChars>().ToList();
         }

         N = n;
      }

      /// <summary>
      /// Загружает CSV стали (C + N), извлекает типы проката и заполняет каскадные ComboBox.
      /// </summary>
      void LoadSteel(string basedir, string dir, CsvConfiguration config)
      {
         var normFile = steelNormIndex switch
         {
            1 => "СП_16_13330_2011",
            2 => "ГОСТ_27772-2015",
            _ => "СП_16_13330_2017"
         };
         var steelDir = Path.Combine(basedir, dir);
         var fileC = Path.Combine(steelDir, $"Конструкционная_сталь_C_{normFile}.csv");
         var fileN = Path.Combine(steelDir, $"Конструкционная_сталь_N_{normFile}.csv");
         if (!File.Exists(fileC) || !File.Exists(fileN)) return;

         var cList = ReadSteelCsv(fileC, config);
         var nList = ReadSteelCsv(fileN, config);

         steelAll.Clear();
         for (int i = 0; i < cList.Count && i < nList.Count; i++)
            steelAll.Add((cList[i].mc, nList[i].mc, cList[i].prokatType));

         steelProkatTypes = steelAll.Select(s => s.prokatType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct().OrderBy(t => t).ToList();
         OnPropertyChanged(nameof(SteelProkatTypes));

         steelProkatTypeIndex = steelProkatTypes.Count > 0 ? 0 : -1;
         OnPropertyChanged(nameof(SteelProkatTypeIndex));

         FilterSteelMarks();
      }

      /// <summary>
      /// Читает CSV стали, возвращает список (MaterialChars, prokatType).
      /// </summary>
      List<(MaterialChars mc, string prokatType)> ReadSteelCsv(string filePath, CsvConfiguration config)
      {
         var result = new List<(MaterialChars, string)>();
         using var reader = new StreamReader(filePath);
         using var csv = new CsvReader(reader, config);

         csv.Read();
         csv.ReadHeader();
         int prokatIdx = -1;
         string[]? headerRecord = csv.Parser!.Context!.Reader!.HeaderRecord;
         if (headerRecord != null)
            prokatIdx = Array.IndexOf(headerRecord, "ProkatType");

         while (csv.Read())
         {
            var mc = new MaterialChars();
            mc.Tag = csv.GetField("Tag") ?? "";
            if (double.TryParse(csv.GetField("Class"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cls)) mc.Class = cls;
            if (double.TryParse(csv.GetField("Fc"), NumberStyles.Float, CultureInfo.InvariantCulture, out var fc)) mc.Fc = fc;
            if (double.TryParse(csv.GetField("Ft"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ft)) mc.Ft = ft;
            if (double.TryParse(csv.GetField("Ry"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ry)) mc.Ry = ry;
            if (double.TryParse(csv.GetField("Ru"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ru)) mc.Ru = ru;
            if (double.TryParse(csv.GetField("E"), NumberStyles.Float, CultureInfo.InvariantCulture, out var e)) mc.E = e;
            if (double.TryParse(csv.GetField("Ec0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) mc.Ec0 = v;
            if (double.TryParse(csv.GetField("Ec1"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Ec1 = v;
            if (double.TryParse(csv.GetField("Ec2"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Ec2 = v;
            if (double.TryParse(csv.GetField("Ec1Red"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Ec1Red = v;
            if (double.TryParse(csv.GetField("Et1Red"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Et1Red = v;
            if (double.TryParse(csv.GetField("Et0"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Et0 = v;
            if (double.TryParse(csv.GetField("Et1"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Et1 = v;
            if (double.TryParse(csv.GetField("Et2"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) mc.Et2 = v;
            mc.Type = MatType.Steel;

            string prokatType = prokatIdx >= 0 ? (csv.GetField(prokatIdx) ?? "") : "";
            result.Add((mc, prokatType));
         }
         return result;
      }

      /// <summary>
      /// Фильтрует марки по выбранному типу проката.
      /// </summary>
      void FilterSteelMarks()
      {
         if (steelProkatTypeIndex < 0 || steelProkatTypeIndex >= steelProkatTypes.Count)
         {
            SteelMarks = new();
            return;
         }

         var selectedType = steelProkatTypes[steelProkatTypeIndex];
         steelMarks = steelAll
            .Where(s => s.prokatType == selectedType)
            .Select(s => s.mcC.Tag)
            .Distinct().OrderBy(t => t).ToList();
         OnPropertyChanged(nameof(SteelMarks));

         steelMarkIndex = steelMarks.Count > 0 ? 0 : -1;
         OnPropertyChanged(nameof(SteelMarkIndex));

         FilterSteelRecords();
      }

      /// <summary>
      /// Фильтрует записи по выбранной марке и типу проката.
      /// C/CL из C-файла (Ry/Ru), N/NL из N-файла (Ryn/Run).
      /// </summary>
      void FilterSteelRecords()
      {
         if (steelProkatTypeIndex < 0 || steelProkatTypeIndex >= steelProkatTypes.Count)
         {
            c = new(); cl = new(); n = new(); nl = new(); N = n;
            return;
         }

         var selectedType = steelProkatTypes[steelProkatTypeIndex];
         var filtered = steelAll
            .Where(s => s.prokatType == selectedType);

         if (steelMarkIndex >= 0 && steelMarkIndex < steelMarks.Count)
         {
            var selectedMark = steelMarks[steelMarkIndex];
            filtered = filtered.Where(s => s.mcC.Tag == selectedMark);
         }

         var pairs = filtered.ToList();
         c  = pairs.Select(p => p.mcC).ToList();
         cl = pairs.Select(p => p.mcC).ToList();
         n  = pairs.Select(p => p.mcN).ToList();
         nl = pairs.Select(p => p.mcN).ToList();
         N = n;
      }
   }
}

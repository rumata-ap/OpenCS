using CScore;

using CsvHelper.Configuration;
using CsvHelper;

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

      /// <summary>Коэффициент условий работы бетона gb2 (0.9 для железобетонных, 1.0 для бетонных конструкций).</summary>
      double gb2 = 1;

      /// <summary>Коэффициент условий работы бетона gb3 (0.85 при высоте сечения более 1.5 м).</summary>
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

      /// <summary>
      /// Флаг учёта коэффициента gb3 для высоты сечения более 1.5 м.
      /// При установке значения пересчитывает gb3 (0.85 или 1.0).
      /// Используется для привязки в CheckBox.
      /// </summary>
      public bool IsH1_5
      {  
         get { return isH1_5; } 
         set { isH1_5 = value; gb3 = value ? 0.85 : 1; OnPropertyChanged(); } 
      }
      
      /// <summary>
      /// Флаг учёта коэффициента kE для термообработанной арматуры.
      /// При установке значения пересчитывает kE (0.89 или 1.0).
      /// Используется для привязки в CheckBox.
      /// </summary>
      public bool IsTerm
      {
         get { return isTerm; }
         set { isTerm = value; kE = value ? 0.89 : 1; OnPropertyChanged(); }
      }
            
      /// <summary>
      /// Флаг доступности переключателя термообработки. Активен только для мелкозернистого бетона.
      /// Используется для привязки в представлении (IsEnabled).
      /// </summary>
      public bool IsTermEnable 
      {  
         get { return isTermEnable; } 
         set { isTermEnable = value; OnPropertyChanged(); } 
      }
      
      /// <summary>
      /// Тип конструкции: 0 — железобетонная (gb2=0.9), 1 — бетонная (gb2=1.0).
      /// При изменении пересчитывает коэффициент gb2. Используется для привязки в RadioButton.
      /// </summary>
      public int IsRC 
      {  
         get { return isRC; } 
         set { isRC = value; gb2 = value == 1 ? 0.9 : 1; OnPropertyChanged(); } 
      }
            
      /// <summary>
      /// Индекс типа бетона: 0 — тяжёлый, 1 — мелкозернистый группы А,
      /// 2 — мелкозернистый группы Б. При изменении обновляет доступность
      /// переключателя термообработки и перезагружает данные из CSV.
      /// </summary>
      public int ConcreteTypeIndex 
      {  
         get { return concreteTypeIndex; } 
         set { concreteTypeIndex = value; IsTermEnable = value == 1; OnPropertyChanged(); } 
      }
                  
      /// <summary>
      /// Индекс типа арматурной стали: 0 — стальная, 1 — полимерная композитная.
      /// Используется для привязки в ComboBox.
      /// </summary>
      public int RfSteelTypeIndex 
      {  
         get { return rfSteelTypeIndex; } 
         set { rfSteelTypeIndex = value; OnPropertyChanged(); } 
      }

      /// <summary>
      /// Список характеристик материала для расчёта на кратковременное действие нагрузки (N).
      /// Используется для привязки в DataGrid справочника.
      /// </summary>
      public List<MaterialChars> N
      {
         get { return n; }
         set { n = value; OnPropertyChanged(); }
      }

      /// <summary>
      /// Выбранная строка характеристик материала в DataGrid справочника.
      /// При изменении вызывает <see cref="SelectMaterial"/> для заполнения
      /// всех видов расчётных характеристик в <see cref="MaterialVM.Material"/>.
      /// </summary>
      public MaterialChars? SelectedMaterial
      {
         get { return selectedMaterial; }
         set { selectedMaterial = value; SelectMaterial(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг выбора вкладки «Бетон». При изменении вызывает <see cref="Select"/>
      /// для перезагрузки данных из CSV-файла.
      /// </summary>
      public bool ConcreteTabIsSelected
      {
         get { return concreteTabIsSelected; }
         set { concreteTabIsSelected = value; Select(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг выбора вкладки «Арматурная сталь». При изменении вызывает <see cref="Select"/>
      /// для перезагрузки данных из CSV-файла.
      /// </summary>
      public bool RfsteelTabIsSelected
      {
         get { return rfsteelTabIsSelected; }
         set { rfsteelTabIsSelected = value; Select(); OnPropertyChanged(); }
      }

      /// <summary>
      /// Флаг выбора вкладки «Сталь конструкций». При изменении вызывает <see cref="Select"/>
      /// для перезагрузки данных из CSV-файла.
      /// </summary>
      public bool SteelTabIsSelected
      {
         get { return steelTabIsSelected; }
         set { steelTabIsSelected = value; Select(); OnPropertyChanged(); }
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

      /// <summary>
      /// Ссылка на ViewModel материала, в которую загружаются выбранные
      /// характеристики из справочника.
      /// </summary>
      public MaterialVM Material { get; set; } = null!;



      /// <summary>
      /// Применяет выбранный материал из справочника к ViewModel материала.
      /// Устанавливает описание, коэффициенты условий работы (gb2, gb3, kE),
      /// тип материала и все четыре набора характеристик (C, CL, N, NL).
      /// Для бетона коэффициенты умножаются на gb2, gb3 и kE.
      /// </summary>
      public void SelectMaterial()
      {
         if (selectedMaterial == null) return;
         int i = n.IndexOf(selectedMaterial);
         
         if (concreteTabIsSelected && concreteTypeIndex == 0)
            Material.Description = "Бетон тяжелый по СП 63.13330";
         if (concreteTabIsSelected && concreteTypeIndex == 1)
            Material.Description = "Бетон мелкозернистый группы А (естественного твердения) по СП 63.13330";
         if (concreteTabIsSelected && concreteTypeIndex == 2)
            Material.Description = "Бетон мелкозернистый группы Б (автоклавного твердения) по СП 63.13330";
         if (rfsteelTabIsSelected && rfSteelTypeIndex == 0)
            Material.Description = "Арматура стальная по СП 63.13330";
         if (rfsteelTabIsSelected && rfSteelTypeIndex == 1)
            Material.Description = "Арматура композитная по ";
         if (steelTabIsSelected)
            Material.Description = "Сталь для строительных конструкций по СП 16.13330";

         if (concreteTabIsSelected)
         {
            var cc = c[i].Clone();
            cc.E *= kE;
            cc.Fc *= gb2 * gb3;
            cc.Et1 = 0.6 * cc.Ft / cc.E;
            cc.Ec1 = 0.6 * cc.Fc / cc.E;
            Material.C = cc;
            var cn = n[i].Clone();
            cn.E *= kE;
            cn.Et1 = 0.6 * cn.Ft / cn.E;
            cn.Ec1 = 0.6 * cn.Fc / cn.E;
            Material.N = cn;
            var cnl = nl[i].Clone();
            cnl.E *= kE;
            cnl.Et1 = 0.6 * cnl.Ft / cnl.E;
            cnl.Ec1 = 0.6 * cnl.Fc / cnl.E;
            Material.NL = cnl;
            var ccl = cl[i].Clone();
            ccl.E *= kE;
            ccl.Fc *= gb2 * gb3;
            ccl.Et1 = 0.6 * ccl.Ft / ccl.E;
            ccl.Ec1 = 0.6 * ccl.Fc / ccl.E;
            Material.CL = ccl;
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
      /// Загружает данные характеристик материала из CSV-файлов в соответствии
      /// с выбранной вкладкой (бетон, арматурная сталь, сталь) и типом материала.
      /// Заполняет списки C, CL, N, NL и свойство <see cref="N"/>.
      /// </summary>
      public void Select()
      {
         // Настраиваем конфигурацию
         var config = new CsvConfiguration(CultureInfo.InvariantCulture)
         {
            Delimiter = ";" // Устанавливаем разделитель
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
         else if (rfsteelTabIsSelected/* && concreteTypeIndex == 0*/)
         {
            fileC = Path.Combine(basedir, dir, "Арматура стальная_C.csv");
            fileCL = Path.Combine(basedir, dir, "Арматура стальная_CL.csv");
            fileN = Path.Combine(basedir, dir, "Арматура стальная_N.csv");
            fileNL = Path.Combine(basedir, dir, "Арматура стальная_NL.csv");
         }

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
   }
}

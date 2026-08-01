using System.Collections.Generic;
using CScore;
using CScore.Fem;
using CScore.Fire.Entities;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Services
{
   /// <summary>Результат диалога создания контура из прямоугольного шаблона.</summary>
   public sealed record TemplateRectResult(double WidthMm, double HeightMm, string ContourName);

   /// <summary>Результат диалога создания контура из таврового шаблона.</summary>
   public sealed record TemplateTeeResult(double WidthMm, double HeightMm, double TwMm, double TfMm, string ContourName);

   /// <summary>Результат диалога создания контура из двутаврового шаблона.</summary>
   public sealed record TemplateIBeamResult(double HeightMm, double WidthMm, double TwMm, double TfMm, string ContourName);

   /// <summary>Результат диалога создания контура из уголкового шаблона.</summary>
   public sealed record TemplateAngleResult(double WidthMm, double HeightMm, double TwMm, double TfMm, string ContourName);

   /// <summary>Результат диалога создания контура из круглого шаблона.</summary>
   public sealed record TemplateCircleResult(double DiameterMm, int Segments, string ContourName);

   /// <summary>Результат диалога создания контура из сортамента (профиль по ГОСТ).</summary>
   public sealed record ProfilePolyResult(string ShapeType, int ProfileId, string ContourName,
       bool IsHollow, int NArc, double Slope);

   /// <summary>Результат диалога ввода нового конструктивного элемента МКЭ (по диапазонам ЛИРА).</summary>
   public sealed record FemMemberInput(string Range, string MemberTag, string? MemberType);

   /// <summary>Результат диалога импорта усилий SCAD из XLS (фильтр по элементам и толщина пластин).</summary>
   public sealed record ScadForceImportOptions(bool ImportAllElements, string ElementText, double ThicknessMm);

   /// <summary>
   /// Фабрика страниц и диалогов. Единственная точка, где VM-слой создаёт GUI-объекты:
   /// GUI-проект (WPF сейчас, Avalonia позже) реализует интерфейс и регистрирует его в UiServices.Pages.
   /// Конкретные классы страниц/окон живут в GUI-проектах; сюда попадают только данные и результат.
   /// </summary>
   public interface IAppPageFactory
   {
      /// <summary>Локализованный заголовок страницы для GroupBox центральной области.</summary>
      string GetPageTitle(IContentPage page);

      #region Страницы навигации

      /// <summary>Новая страница редактора двухстадийного сечения.</summary>
      IContentPage CreateTwoStageSectionEditorPage();

      /// <summary>Страница редактора существующего двухстадийного сечения.</summary>
      IContentPage CreateTwoStageSectionEditorPage(TwoStageSection section);

      /// <summary>Новая страница редактора поперечного сечения.</summary>
      IContentPage CreateCrossSectionPage();

      /// <summary>Страница редактора существующего поперечного сечения.</summary>
      IContentPage CreateCrossSectionPage(CrossSection section);

      /// <summary>Новая страница редактора пластины.</summary>
      IContentPage CreatePlateSectionPage();

      /// <summary>Страница редактора существующей пластины.</summary>
      IContentPage CreatePlateSectionPage(PlateSection section);

      /// <summary>Страница огнестойкости указанного сечения.</summary>
      IContentPage CreateFireSectionPage(FireSectionDef section);

      /// <summary>Страница FEM-схемы.</summary>
      IContentPage CreateFemSchemaPage(FemSchema schema);

      /// <summary>Страница редактора группы конструктивных элементов МКЭ.</summary>
      IContentPage CreateFemMemberEditorPage(FemMemberGroup group);

      /// <summary>Новая страница набора усилий стержня.</summary>
      IContentPage CreateBarForceSetPage();

      /// <summary>Страница набора усилий стержня.</summary>
      IContentPage CreateBarForceSetPage(ForceSet set);

      /// <summary>Новая страница набора усилий пластины.</summary>
      IContentPage CreateShellForceSetPage();

      /// <summary>Страница набора усилий пластины.</summary>
      IContentPage CreateShellForceSetPage(ForceSet set);

      /// <summary>Страница области материала.</summary>
      IContentPage CreateMaterialAreaPage(MaterialArea area);

      /// <summary>Страница группы арматуры.</summary>
      IContentPage CreateRebarGroupEditorPage(MaterialArea area);

      /// <summary>Страница просмотра контура (ContourPlot).</summary>
      IContentPage CreateContourPlotPage(bool isSaved);

      /// <summary>Страница диаграммы σ(ε).</summary>
      IContentPage CreateDiagramPage(Diagramm diagram, bool isNew);

      /// <summary>Страница редактирования материала.</summary>
      IContentPage CreateMaterialPage(Material material);

      /// <summary>Страница материала с немедленным открытием окна хранилища материалов
      /// (вкладка — по типу: 0=бетон, 1=арматура, 2=сталь).</summary>
      IContentPage CreateMaterialFromSourcePage(int tabIndex);

      /// <summary>Страница импорта геометрии из DXF-файла.</summary>
      IContentPage CreateFromDxfPage(string fileName);

      /// <summary>Страница расчётных задач.</summary>
      IContentPage CreateCalcTasksPage();

      /// <summary>Страница результата расчёта.</summary>
      IContentPage CreateCalcResultPage(CalcResult result);

      /// <summary>Страница результата нормативной проверки МКЭ.</summary>
      IContentPage CreateFemCheckResultPage(CalcResult result);

      /// <summary>Страница результата FEM-анализа. VM создаётся внутри фабрики (содержит
      /// платформенные типы 3D); события узлов пробрасываются через делегаты.</summary>
      FemAnalysisResultPage CreateFemAnalysisResultPage(CalcResult result,
          AppViewModel app, FemSchema schema,
          System.Action<string> showMemberForce,
          System.Action<string> goToSection,
          System.Action<string> showNodeValues);

      /// <summary>Страница 2D-эпюр усилий по одному конструктивному стержню.</summary>
      IContentPage CreateFemMemberForcePage(DatabaseService db, FemSchema schema, string memberTag, CalcResult result);

      #endregion

      #region Диалоги

      /// <summary>Окно настроек приложения (модальное).</summary>
      void ShowSettingsWindow(AppViewModel app);

      /// <summary>Диалог создания контура из прямоугольного шаблона. null при отмене.</summary>
      TemplateRectResult? ShowTemplateRectDialog();

      /// <summary>Диалог создания контура из таврового шаблона. null при отмене.</summary>
      TemplateTeeResult? ShowTemplateTeeDialog();

      /// <summary>Диалог создания контура из двутаврового шаблона. null при отмене.</summary>
      TemplateIBeamResult? ShowTemplateIBeamDialog();

      /// <summary>Диалог создания контура из уголкового шаблона. null при отмене.</summary>
      TemplateAngleResult? ShowTemplateAngleDialog();

      /// <summary>Диалог создания контура из круглого шаблона. null при отмене.</summary>
      TemplateCircleResult? ShowTemplateCircleDialog();

      /// <summary>Диалог создания контура из профиля сортамента. null при отмене.</summary>
      ProfilePolyResult? ShowProfilePolyDialog();

      /// <summary>Диалог ввода круга (окружности-отверстия). null при отмене.</summary>
      (double X, double Y, double Radius)? ShowCircleDialog();

      /// <summary>Диалог однострочного ввода текста. Возвращает введённое значение или null при отмене.</summary>
      string? ShowTextInputDialog(string titleKey, string labelKey, string initialValue);

      /// <summary>Диалог комбинаций усилий СП 20.13330 (модальный, результат не возвращается).</summary>
      void ShowSp20Dialog(System.Collections.IEnumerable sets, AppViewModel app);

      /// <summary>Диалог создания/редактирования огнестойкого сечения. Возвращает результат или null при отмене.</summary>
      FireSectionDef? ShowFireSectionDialog(AppViewModel app, FireSectionDef? existing = null);

      /// <summary>Диалог создания/редактирования расчётной задачи. Возвращает результат или null при отмене.</summary>
      CalcTask? ShowCalcTaskDialog(AppViewModel app, CalcTask? existing = null, string? groupKey = null);

      /// <summary>Диалог ввода нового конструктивного элемента МКЭ (диапазоны ЛИРА). null при отмене.</summary>
      FemMemberInput? ShowFemMemberDialog();

      /// <summary>Диалог импорта усилий SCAD из XLS (фильтр по номерам элементов и толщина пластин).
      /// seed — начальные номера элементов выбранного конструктивного элемента. null при отмене.</summary>
      ScadForceImportOptions? ShowScadForceImportDialog(string? seed);

      /// <summary>Диалог создания нормативной проверки МКЭ (sls — проверка по предельным состояниям 2-й группы).
      /// Возвращает созданную проверку или null при отмене.</summary>
      FemCheck? ShowFemCheckCreateDialog(AppViewModel app, bool sls);

      /// <summary>Диалог редактирования нормативной проверки МКЭ (мутирует переданный объект).</summary>
      void ShowFemCheckEditDialog(AppViewModel app, FemCheck check, bool sls);

      /// <summary>Диалог создания/редактирования FEM-анализа. Возвращает результат или null при отмене.</summary>
      FemAnalysis? ShowFemAnalysisDialog(FemSchema schema, FemAnalysis? existing = null);

      /// <summary>Диалог выбора набора усилий для удаления. Возвращает выбранные или null при отмене.</summary>
      IReadOnlyList<CScore.ForceSet>? ShowDeleteForceSetsDialog(IReadOnlyList<CScore.ForceSet> sets);

      /// <summary>Окно свойств контура (модальное).</summary>
      void ShowContourPropsWindow(Contour contour, string tag);

      /// <summary>Диалог ввода двух чисел (например, коэффициенты масштабирования). null при отмене.</summary>
      (double X, double Y)? ShowDoubleInputDialog(string title, string labelX, string labelY,
          double initialX, double initialY);

      /// <summary>Помечает страницу редактирования материала как сохранённую (IsSaved = true).</summary>
      void MarkMaterialPageSaved(IContentPage page);

      /// <summary>Заполняет структуру дерева сечений (WPF: CompositeCollection + CollectionContainer).</summary>
      void InitializeSectionTree(AppViewModel app);

      /// <summary>Переключает язык интерфейса (замена словарей ресурсов). 0 — русский, 1 — английский.</summary>
      void ApplyLanguage(int lang);

      /// <summary>Завершает работу приложения (закрытие главного окна).</summary>
      void ShutdownApplication();

      #endregion
   }
}

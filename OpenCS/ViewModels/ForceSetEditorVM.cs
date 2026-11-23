using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// Общая часть страниц наборов усилий стержня и пластины. Правится черновик (копия набора со строками): расчёт
   /// видит набор только после «Сохранить» — проверка и запись через <see cref="ForceSetAuthoring"/>; «*» и вопрос при
   /// уходе — по отпечатку заголовка и признаку правки строк (полный отпечаток миллиона строк на каждую правку дорог).
   /// </summary>
   public abstract class ForceSetEditorVM : ViewModelBase, IUnsavedChanges
   {
      protected readonly ForceSet _model;
      protected ForceSet _draft;
      string _savedHeader;
      string _savedTag;
      bool _rowsChanged;

      protected ForceSetEditorVM(ForceSet model, AppViewModel app)
      {
         _model = model;
         App = app;
         _draft = ForceSetAuthoring.Draft(model);
         _savedHeader = Header(_draft);
         _savedTag = model.Tag;
         Sp20 = new ForceSetNameEditorVM(_draft, OnHeaderChanged);

         SaveCommand      = new RelayCommand(_ => Save(), _ => IsDirty);
         ExportCsvCommand = new RelayCommand(_ => ExportCsv());
         ImportCsvCommand = new RelayCommand(_ => ImportCsv());
         App.PlotSettingsApplied += OnPlotSettingsApplied;
      }

      void OnPlotSettingsApplied() => OnPropertyChanged(nameof(ColorizeForceValues));

      /// <summary>Страница закрыта — отписаться от событий приложения.</summary>
      public void Detach() => App.PlotSettingsApplied -= OnPlotSettingsApplied;

      public AppViewModel App { get; }

      /// <summary>Набор проекта, который показывает страница (новый — ещё не в пуле).</summary>
      public ForceSet Model => _model;

      /// <summary>Черновик правки (тесты).</summary>
      internal ForceSet Draft => _draft;

      public string Kind => _draft.Kind;

      /// <summary>Раскраска значений усилий по осям (глобальная настройка).</summary>
      public bool ColorizeForceValues => App.PlotSettings.ForceSetColorize;

      /// <summary>Параметры имени набора для комбинаторики СП 20.</summary>
      public ForceSetNameEditorVM Sp20 { get; private set; }

      public ICommand SaveCommand { get; }
      public ICommand ExportCsvCommand { get; }
      public ICommand ImportCsvCommand { get; }

      static string Header(ForceSet fs) => JsonSerializer.Serialize(new
      {
         fs.Tag, Description = fs.Description ?? "", fs.Kind, fs.SecondOrder, Imperfections = fs.Imperfections ?? ""
      });

      /// <summary>Есть правки, не записанные в проект.</summary>
      public bool IsDirty => _rowsChanged || Header(_draft) != _savedHeader;

      /// <summary>Имя для вопроса о сохранении.</summary>
      public string DisplayName => !string.IsNullOrWhiteSpace(_draft.Tag) ? _draft.Tag
         : !string.IsNullOrWhiteSpace(_savedTag) ? _savedTag : Loc.S("NewForceSetName");

      void OnHeaderChanged() => OnPropertyChanged(nameof(IsDirty));

      /// <summary>Строка или их состав изменены.</summary>
      protected void RowsChanged()
      {
         _rowsChanged = true;
         OnPropertyChanged(nameof(IsDirty));
      }

      /// <summary>Строки черновика → строки страницы (после открытия, «Не сохранять», CSV).</summary>
      protected abstract void LoadRows();

      /// <summary>Заменяет строки черновика прочитанными из CSV.</summary>
      protected abstract void ReplaceRows(List<LoadItem> bar, List<ShellLoadItem> shell);

      /// <summary>Номер для новой строки страницы.</summary>
      protected int NextRowNum(int count) => count + 1;

      void ExportCsv()
      {
         var path = App.FileDialogService.SaveFile(Loc.S("CsvFileFilter"), ".csv", Loc.S("ExportCsvBtn"));
         if (path == null) return;
         try { ForceSetAuthoring.WriteCsv(path, _draft); }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            App.Notifier.Error(ex.Message, Loc.S("ExportCsvBtn"));
         }
      }

      void ImportCsv()
      {
         var path = App.FileDialogService.OpenFile(Loc.S("CsvFileFilter"), Loc.S("ImportCsvBtn"));
         if (path == null) return;
         ImportCsvFrom(path);
      }

      /// <summary>Строки из CSV — в черновик (видна «*», откатывается «Не сохранять»); ошибки — сообщением, без замены.</summary>
      internal bool ImportCsvFrom(string path)
      {
         List<LoadItem> bar;
         List<ShellLoadItem> shell;
         List<string> errors;
         try { (bar, shell, errors) = ForceSetAuthoring.ReadCsv(path, _draft.Kind); }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            errors = [ex.Message];
            bar = [];
            shell = [];
         }
         if (errors.Count > 0)
         {
            App.Notifier.Error(string.Format(Loc.S("ForceSetCsvImportFailed"), errors.Count) + Environment.NewLine
               + string.Join(Environment.NewLine, errors.Take(10)) + (errors.Count > 10 ? Environment.NewLine + "…" : ""),
               Loc.S("ImportCsvBtn"));
            return false;
         }
         ReplaceRows(bar, shell);
         LoadRows();
         RowsChanged();
         App.LogService.Info(string.Format(Loc.S("ForceSetCsvImportedLog"), bar.Count + shell.Count, DisplayName));
         return true;
      }

      /// <summary>Проверяет черновик и записывает его в набор проекта; ошибки — в журнал.</summary>
      public bool Save()
      {
         var errors = ForceSetAuthoring.Validate(_draft).ToList();
         if (Sp20.HasErrors) errors.Add(new FieldError("gammaF", Sp20.GammaError!));
         if (errors.Count > 0)
         {
            foreach (var e in errors)
               App.LogService.Error(string.Format(Loc.S("ForceSetSaveErrorLog"), DisplayName, RowPath(e.Path), e.Message));
            return false;
         }
         bool isNew = _model.Id == 0;
         ForceSetAuthoring.CopyInto(_draft, _model);
         var saveErrors = ForceSetAuthoring.Save(App.db, _model);
         if (saveErrors.Count > 0)   // не должно случиться: черновик проверен тем же Validate
         {
            App.LogService.Error(string.Format(Loc.S("ForceSetSaveErrorLog"), DisplayName, saveErrors[0].Path, saveErrors[0].Message));
            return false;
         }
         // Id и номера строк — в черновик: следующее сохранение сохранит ссылки задач на строки.
         for (int i = 0; i < _model.Items.Count; i++) { _draft.Items[i].Id = _model.Items[i].Id; _draft.Items[i].Num = _model.Items[i].Num; }
         for (int i = 0; i < _model.ShellItems.Count; i++) { _draft.ShellItems[i].Id = _model.ShellItems[i].Id; _draft.ShellItems[i].Num = _model.ShellItems[i].Num; }
         _draft.Id = _model.Id;
         _draft.Num = _model.Num;
         _savedHeader = Header(_draft);
         _savedTag = _model.Tag;
         _rowsChanged = false;
         LoadRows();
         App.RefreshForceSetInTree(_model);
         OnPropertyChanged(nameof(IsDirty));
         App.LogService.Info(string.Format(Loc.S(isNew ? "ForceSetSavedNewLog" : "ForceSetSavedLog"), _model.Tag));
         int same = ForceSetAuthoring.OthersWithTag(App.db, _model);
         if (same > 0) App.LogService.Warning(string.Format(Loc.S("ForceSetSameTagWarning"), _model.Tag, same));
         return true;
      }

      /// <summary>Путь ошибки для журнала: <c>rows[3].Mx</c> → «строка 4, Mx».</summary>
      static string RowPath(string path)
      {
         var m = System.Text.RegularExpressions.Regex.Match(path, @"^rows\[(\d+)\]\.(\w+)$");
         return m.Success ? string.Format(Loc.S("ForceSetRowPath"), int.Parse(m.Groups[1].Value) + 1, m.Groups[2].Value) : path;
      }

      bool IUnsavedChanges.SaveChanges() => Save();

      /// <summary>Черновик — заново из записанного набора.</summary>
      public void DiscardChanges()
      {
         _draft = ForceSetAuthoring.Draft(_model);
         _savedHeader = Header(_draft);
         _rowsChanged = false;
         Sp20 = new ForceSetNameEditorVM(_draft, OnHeaderChanged);
         LoadRows();
         OnPropertyChanged(string.Empty);
      }
   }
}

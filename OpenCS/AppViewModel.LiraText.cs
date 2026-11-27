using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS
{
   /// <summary>
   /// Граничные условия и нагрузки из текстового файла схемы ЛИРЫ (<c>Data\&lt;Title&gt;.txt</c>): перенос при импорте из
   /// API и команды схемы «Дочитать граничные условия» / «Дочитать нагрузки» (срез 5 плана 4б ЛИРЫ + 4ж).
   /// </summary>
   public partial class AppViewModel
   {
      public ICommand TransferLiraLoadsCommand { get; set; } = null!;
      public ICommand RefreshLiraBoundaryCommand { get; set; } = null!;
      public ICommand ImportLiraSchemaFromTextCommand { get; set; } = null!;

      void InitLiraTextCommands()
      {
         TransferLiraLoadsCommand = new RelayCommand(async p => await RefreshLiraFromText(p as FemSchema, boundary: false, loads: true),
            _ => !IsBusy);
         RefreshLiraBoundaryCommand = new RelayCommand(async p => await RefreshLiraFromText(p as FemSchema, boundary: true, loads: false),
            _ => !IsBusy);
         ImportLiraSchemaFromTextCommand = new RelayCommand(_ => ImportLiraSchemaFromText(), _ => !IsBusy);
         InitLiraMaskSnapshotCommand();
      }

      /// <summary>
      /// Импорт схемы целиком из текстового файла ЛИРЫ — без COM API (ранние версии ЛИРЫ) и без запущенной ЛИРЫ: геометрия
      /// и жёсткости — документы 1, 3, 4, 9 и <c>{13/}</c>, имена и Ro жёсткостей, блоки и C1 пластин —
      /// <c>Work\&lt;Title&gt;.ald</c> (если есть), ГУ и нагрузки — как при импорте из API. Ошибка — схема не создаётся.
      /// </summary>
      async void ImportLiraSchemaFromText()
      {
         if (IsBusy) return;
         var import = FemImport;
         var dirs = import.LiraTextDirectories();
         string? path = FileDialogService.OpenFile(Loc.S("LiraTextFilter"), Loc.S("ImportLiraSchemaFromTextTitle"),
            import.OwnLiraTextDirectory() ?? dirs.LastOrDefault()?.Path);
         if (path == null) return;

         BeginBusy(Loc.S("StatusImportingSchema"));
         try
         {
            var txt = await import.ReadLiraTextAsync(path, Services.BackgroundImportWork.Instance);
            if (txt == null) { EndBusy(); return; }
            if (txt.Nodes.Count == 0 || txt.Elements.Count == 0)
            {
               EndBusy();
               MessageBox.Show(string.Format(Loc.S("LiraTextNoGeometry"), Path.GetFileName(path)), Loc.S("ImportLiraErrorTitle"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            var (schema, done, boundary, loads) = await import.ImportLiraText(txt, path, null, Services.BackgroundImportWork.Instance);
            RefreshFemSchemaTreeCounts(schema);
            ShowBoundarySummary(schema, boundary, show: true);
            LogService.Info(done);
            EndBusy(done);
            AskDesignLoadValues(loads);
         }
         catch (Exception ex)
         {
            EndBusy();
            LogService.Error(ex.ToString());
            MessageBox.Show(ex.Message, Loc.S("ImportLiraErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }

      /// <summary>
      /// «Дочитать граничные условия» / «Дочитать нагрузки» для схемы ЛИРЫ: текстовый файл — по имени схемы или открытой в
      /// ЛИРЕ схемы во всех версиях ЛИРЫ (подтверждение) либо выбранный пользователем; сетка схемы сверяется с файлом.
      /// Повторный перенос заменяет перенесённое раньше, ручное не трогает.
      /// </summary>
      async Task RefreshLiraFromText(FemSchema? schema, bool boundary, bool loads)
      {
         if (IsBusy) return;
         schema ??= currentFemSchema;
         string title = Loc.S(boundary ? "LiraBoundaryRefresh" : "LiraLoadsTransfer");
         if (schema == null || schema.SourceType != "lira")
         {
            MessageBox.Show(schema == null ? Loc.S("ScadForcesNoTarget") : string.Format(Loc.S("LiraTextNotLiraSchema"), schema.Tag),
               title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         bool editorOpen = ReferenceEquals(currentFemSchema, schema) && currentPage is Views.FemSchemaPage;
         if (editorOpen && !TryLeaveFemSchemaEditor()) return;

         var import = FemImport;
         var (path, lirPath) = await ChooseLiraTextPath(import, schema, title);
         if (path == null) return;
         BeginBusy(Loc.S("LiraTextReading"));
         var txt = await import.ReadLiraTextAsync(path, Services.BackgroundImportWork.Instance);
         EndBusy();
         if (txt == null) return;
         import.WarnIfLiraTextStale(path, lirPath);

         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var elements = db.GetFemMeshElements(schema.Id);
         var match = LiraTextMeshMatch.Compare(txt, meshNodes, elements);
         if (!match.Ok)
         {
            string msg = Services.FemSchemaImport.MismatchMessage(schema, path, match);
            LogService.Warning(msg);
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         // C1 пластин в текстовом файле не читается — сохраняется прочитанный импортом из API.
         if (boundary)
            ShowBoundarySummary(schema, import.TransferLiraBoundary(schema, txt, meshNodes, elements,
               Services.FemSchemaImport.ImportedFoundationC1(elements)), show: true);
         Services.FemLoadTransferOutcome? outcome = null;
         if (loads)
         {
            BeginBusy(Loc.S("LiraLoadsTransferring"));
            try { outcome = await import.TransferLiraLoads(schema, txt, meshNodes, elements, Services.BackgroundImportWork.Instance); }
            finally { EndBusy(); }
         }
         if (!string.Equals(schema.SourcePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            db.UpdateFemSchemaSourcePath(schema, Path.GetFullPath(path));
         RefreshFemSchemaTreeCounts(schema);
         if (outcome != null) AskDesignLoadValues(outcome);
      }

      /// <summary>
      /// Текстовый файл для команды: найденный по имени схемы, по пути прошлого переноса или по имени открытой в ЛИРЕ
      /// схемы — с подтверждением («Нет» — выбрать другой); не найден — выбор файла. Второй элемент — путь .lir открытой
      /// схемы (для проверки свежести файла), если файл — её.
      /// </summary>
      async Task<(string? Path, string? LirPath)> ChooseLiraTextPath(Services.FemSchemaImport import, FemSchema schema, string title)
      {
         (int? Version, string? Title, string? PathName)? open = null;
         try { open = await RunOnStaThread(Services.LiraApiSchemaReader.ReadDocumentInfo); }
         catch (Exception) { }  // ЛИРА не запущена — ищем по имени схемы

         var dirs = import.LiraTextDirectories();
         string? openFile = import.FindLiraText(open?.Title, dirs, open?.Version);
         string? found = import.FindLiraText(schema.Tag, dirs, open?.Version)
            ?? (schema.SourcePath is { } sp && sp.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) && File.Exists(sp) ? sp : null)
            ?? openFile;
         string? LirFor(string path) => openFile != null && string.Equals(Path.GetFullPath(openFile), Path.GetFullPath(path),
            StringComparison.OrdinalIgnoreCase) ? open?.PathName : null;

         if (found != null)
         {
            var answer = MessageBox.Show(string.Format(Loc.S("LiraTextConfirm"), schema.Tag, found,
                  File.GetLastWriteTime(found).ToString("dd.MM.yyyy HH:mm")),
               title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return (null, null);
            if (answer == MessageBoxResult.Yes) return (found, LirFor(found));
         }

         string? initial = found != null ? Path.GetDirectoryName(found)
            : import.OwnLiraTextDirectory() ?? dirs.FirstOrDefault(d => d.Version == open?.Version)?.Path ?? dirs.LastOrDefault()?.Path;
         string? chosen = FileDialogService.OpenFile(Loc.S("LiraTextFilter"), string.Format(Loc.S("LiraTextOpenTitle"), schema.Tag), initial);
         return chosen == null ? (null, null) : (chosen, LirFor(chosen));
      }
   }
}

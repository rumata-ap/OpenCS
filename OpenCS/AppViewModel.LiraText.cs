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
      }

      /// <summary>
      /// После импорта схемы из API: найти текстовый файл открытой схемы во всех версиях ЛИРЫ и перенести из него ГУ (C1
      /// пластин — из API) и нагрузки. Файла нет или он от другой схемы — предупреждение, закрепления и C1 — из API.
      /// </summary>
      /// <returns>Путь перенесённого файла; null — перенос не выполнен.</returns>
      async Task<string?> TransferLiraTextAfterImport(FemSchema schema, LiraSchemaData raw, string? title, int? version,
         string? lirPath)
      {
         var dirs = LiraTextFileLocator.DataDirectories(LiraTextFileLocator.PublicDocuments);
         string? path = LiraTextFileLocator.Find(title, dirs, version);
         if (path == null)
         {
            LogService.Warning(string.Format(Loc.S("LiraTextNotFound"), LiraTextFileLocator.FileName(title) ?? title ?? "?",
               dirs.Count == 0 ? LiraTextFileLocator.PublicDocuments : string.Join("; ", dirs.Select(d => d.Path)),
               Loc.S("LiraBoundaryRefresh"), Loc.S("LiraLoadsTransfer")));
            return null;
         }
         var txt = await ReadLiraText(path);
         if (txt == null) return null;
         WarnIfLiraTextStale(path, lirPath);
         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var elements = db.GetFemMeshElements(schema.Id);
         var match = LiraTextMeshMatch.Compare(txt, meshNodes, elements);
         if (!match.Ok)
         {
            LogService.Warning(MismatchMessage(schema, path, match));
            return null;
         }
         await ApplyLiraTextToNewSchema(schema, raw, txt, path, meshNodes, elements);
         return path;
      }

      /// <summary>ГУ (C1 пластин — из <paramref name="raw"/>) и нагрузки текстового файла в только что импортированную
      /// схему; путь файла — в <see cref="FemSchema.SourcePath"/>.</summary>
      async Task ApplyLiraTextToNewSchema(FemSchema schema, LiraSchemaData raw, LiraTextSchema txt, string path,
         IReadOnlyList<FemMeshNode> meshNodes, IReadOnlyList<FemElement> elements)
      {
         var c1 = CScore.Import.LiraSchemaConverter.ToFemElementBoundaryProps(raw)
            .ToDictionary(kv => kv.Key, kv => kv.Value.FoundationC1!.Value, StringComparer.Ordinal);
         TransferLiraBoundary(schema, txt, meshNodes, elements, c1.Count > 0 ? c1 : null, showSummary: true);
         await TransferLiraLoads(schema, txt, meshNodes, elements);
         db.UpdateFemSchemaSourcePath(schema, Path.GetFullPath(path));
      }

      /// <summary>
      /// Импорт схемы целиком из текстового файла ЛИРЫ — без COM API (ранние версии ЛИРЫ) и без запущенной ЛИРЫ: геометрия
      /// и жёсткости — документы 1, 3, 4, 9 и <c>{13/}</c>, имена и Ro жёсткостей, блоки и C1 пластин —
      /// <c>Work\&lt;Title&gt;.ald</c> (если есть), ГУ и нагрузки — как при импорте из API.
      /// </summary>
      async void ImportLiraSchemaFromText()
      {
         if (IsBusy) return;
         var dirs = LiraTextFileLocator.DataDirectories(LiraTextFileLocator.PublicDocuments);
         string? path = FileDialogService.OpenFile(Loc.S("LiraTextFilter"), Loc.S("ImportLiraSchemaFromTextTitle"),
            dirs.LastOrDefault()?.Path);
         if (path == null) return;

         BeginBusy(Loc.S("StatusImportingSchema"));
         try
         {
            var txt = await ReadLiraText(path);
            if (txt == null) { EndBusy(); return; }
            if (txt.Nodes.Count == 0 || txt.Elements.Count == 0)
            {
               EndBusy();
               MessageBox.Show(string.Format(Loc.S("LiraTextNoGeometry"), Path.GetFileName(path)), Loc.S("ImportLiraErrorTitle"),
                  MessageBoxButton.OK, MessageBoxImage.Warning);
               return;
            }
            string? aldPath = LiraAldFile.PathFor(path);
            LiraAldFile? ald = null;
            if (aldPath != null)
               try { ald = await Task.Run(() => LiraAldFile.Read(aldPath)); }
               catch (Exception ex) { LogService.Warning(string.Format(Loc.S("LiraAldReadError"), aldPath, ex.Message)); }
            else LogService.Warning(string.Format(Loc.S("LiraAldNotFound"), Path.GetFileNameWithoutExtension(path) + ".ald"));

            var report = new List<string>();
            double tonToKn = LiraImportSettings.TonToKnFactor;
            var raw = await Task.Run(() => LiraTextSchemaConverter.ToSchemaData(txt, ald, tonToKn, report));
            foreach (var line in report) LogService.Warning(line);

            string title = txt.Title.Trim() is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(path);
            var schema = new FemSchema { Tag = title, SourceType = "lira" };
            db.SaveFemSchema(schema);
            int groupCount = SaveLiraSchemaMesh(schema, raw);
            await ApplyLiraTextToNewSchema(schema, raw, txt, path, db.GetFemMeshNodes(schema.Id), db.GetFemMeshElements(schema.Id));
            if (raw.PlateFoundationBeyondC1 > 0)
               LogService.Warning(string.Format(Loc.S("LiraFoundationBeyondC1"), raw.PlateFoundationBeyondC1));
            SaveLiraSchemaExtras(schema, raw);
            string done = LiraImportDone(raw, groupCount);
            LogService.Info(done);
            EndBusy(done);
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

         var (path, lirPath) = await ChooseLiraTextPath(schema, title);
         if (path == null) return;
         BeginBusy(Loc.S("LiraTextReading"));
         var txt = await ReadLiraText(path);
         EndBusy();
         if (txt == null) return;
         WarnIfLiraTextStale(path, lirPath);

         var meshNodes = db.GetFemMeshNodes(schema.Id);
         var elements = db.GetFemMeshElements(schema.Id);
         var match = LiraTextMeshMatch.Compare(txt, meshNodes, elements);
         if (!match.Ok)
         {
            string msg = MismatchMessage(schema, path, match);
            LogService.Warning(msg);
            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         if (boundary)
         {
            // C1 пластин в текстовом файле не читается — сохраняется прочитанный импортом из API.
            var c1 = elements.Where(e => e.Origin == FemMember.MeshSourceImported && e.ElemType == "shell" && e.FoundationC1 is > 0)
               .GroupBy(e => e.ElemTag, StringComparer.Ordinal)
               .ToDictionary(g => g.Key, g => g.First().FoundationC1!.Value, StringComparer.Ordinal);
            TransferLiraBoundary(schema, txt, meshNodes, elements, c1.Count > 0 ? c1 : null, showSummary: true);
         }
         if (loads)
         {
            BeginBusy(Loc.S("LiraLoadsTransferring"));
            try { await TransferLiraLoads(schema, txt, meshNodes, elements); }
            finally { EndBusy(); }
         }
         if (!string.Equals(schema.SourcePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            db.UpdateFemSchemaSourcePath(schema, Path.GetFullPath(path));
         RefreshFemSchemaTreeCounts(schema);
      }

      /// <summary>
      /// Текстовый файл для команды: найденный по имени схемы, по пути прошлого переноса или по имени открытой в ЛИРЕ
      /// схемы — с подтверждением («Нет» — выбрать другой); не найден — выбор файла. Второй элемент — путь .lir открытой
      /// схемы (для проверки свежести файла), если файл — её.
      /// </summary>
      async Task<(string? Path, string? LirPath)> ChooseLiraTextPath(FemSchema schema, string title)
      {
         (int? Version, string? Title, string? PathName)? open = null;
         try { open = await RunOnStaThread(Services.LiraApiSchemaReader.ReadDocumentInfo); }
         catch (Exception) { }  // ЛИРА не запущена — ищем по имени схемы

         var dirs = LiraTextFileLocator.DataDirectories(LiraTextFileLocator.PublicDocuments);
         string? openFile = LiraTextFileLocator.Find(open?.Title, dirs, open?.Version);
         string? found = LiraTextFileLocator.Find(schema.Tag, dirs, open?.Version)
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
            : dirs.FirstOrDefault(d => d.Version == open?.Version)?.Path ?? dirs.LastOrDefault()?.Path;
         string? chosen = FileDialogService.OpenFile(Loc.S("LiraTextFilter"), string.Format(Loc.S("LiraTextOpenTitle"), schema.Tag), initial);
         return chosen == null ? (null, null) : (chosen, LirFor(chosen));
      }

      /// <summary>Читает текстовый файл ЛИРЫ в фоне; замечания разбора — в журнал; ошибка чтения — в журнал, null.</summary>
      async Task<LiraTextSchema?> ReadLiraText(string path)
      {
         try
         {
            var txt = await Task.Run(() => LiraTextReader.Read(path));
            foreach (var w in txt.Warnings.Take(10)) LogService.Warning($"{Path.GetFileName(path)}: {w}");
            int more = txt.Warnings.Count - 10 + txt.SuppressedWarnings;
            if (more > 0) LogService.Warning(string.Format(Loc.S("LiraTextMoreWarnings"), Path.GetFileName(path), more));
            LogService.Info(string.Format(Loc.S("LiraTextRead"), path, txt.Nodes.Count, txt.Elements.Count, txt.Loads.Count));
            return txt;
         }
         catch (Exception ex)  // повреждённый файл не должен ронять импорт: ГУ и нагрузки дочитываются командой
         {
            LogService.Warning(string.Format(Loc.S("LiraTextReadError"), path, ex.Message));
            return null;
         }
      }

      /// <summary>Текстовый файл старше файла схемы .lir — ГУ и нагрузки могут быть устаревшими (предупреждение).</summary>
      void WarnIfLiraTextStale(string path, string? lirPath)
      {
         if (lirPath == null || !File.Exists(lirPath)) return;
         DateTime txtTime = File.GetLastWriteTime(path), lirTime = File.GetLastWriteTime(lirPath);
         if (txtTime < lirTime.AddMinutes(-1))
            LogService.Warning(string.Format(Loc.S("LiraTextStale"), Path.GetFileName(path), txtTime.ToString("dd.MM.yyyy HH:mm"),
               Path.GetFileName(lirPath), lirTime.ToString("dd.MM.yyyy HH:mm")));
      }

      static string MismatchMessage(FemSchema schema, string path, LiraTextMeshMatch match) =>
         string.Format(Loc.S("LiraTextMismatch"), Path.GetFileName(path), schema.Tag, match.Missing, match.Different,
            match.Example ?? (match.Nodes == 0 ? Loc.S("LiraTextNoImportedNodes") : ""));

      /// <summary>Переносит ГУ текстового файла в сеточный уровень схемы (заменяет перенесённые раньше).</summary>
      void TransferLiraBoundary(FemSchema schema, LiraTextSchema txt, IReadOnlyList<FemMeshNode> meshNodes,
         IReadOnlyList<FemElement> elements, IReadOnlyDictionary<string, double>? foundationC1, bool showSummary)
      {
         var result = LiraBoundaryTransfer.Transfer(txt, meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal),
            ElementTypes(elements), LiraImportSettings.TonToKnFactor, foundationC1);
         SaveBoundaryTransfer(schema, result, LiraBoundaryTransfer.Origin, meshNodes, elements, showSummary);
      }

      /// <summary>
      /// Переносит нагрузки текстового файла в сеточный уровень схемы (заменяет перенесённые раньше). Перенос и ΣF — в
      /// фоне (ФР19: 0,6 млн нагрузок, по 4–7 с), запись в БД — в потоке интерфейса.
      /// </summary>
      async Task TransferLiraLoads(FemSchema schema, LiraTextSchema txt, IReadOnlyList<FemMeshNode> meshNodes,
         IReadOnlyList<FemElement> elements)
      {
         var tags = meshNodes.Select(n => n.NodeTag).ToHashSet(StringComparer.Ordinal);
         var types = ElementTypes(elements);
         var cases = schema.LoadCases.ToList();
         var elementLoads = db.GetFemElementLoads(schema.Id);
         var nodeLoads = db.GetFemMeshNodeLoads(schema.Id);
         double tonToKn = LiraImportSettings.TonToKnFactor;
         var result = await Task.Run(() =>
         {
            int next = 0;
            return LiraLoadTransfer.Transfer(txt, tags, types, tonToKn, cases, elementLoads, nodeLoads, () => --next);
         });
         SaveLoadTransferResult(schema, result);
         var resolve = LoadTotalsResolver(schema, result, LiraLoadTransfer.Origin, meshNodes, elements);
         FinishLoadTransfer(schema, "LiraLoadsTotal", await Task.Run(resolve));
      }

      static Dictionary<string, string> ElementTypes(IEnumerable<FemElement> elements) =>
         elements.GroupBy(e => e.ElemTag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ElemType, StringComparer.Ordinal);
   }
}

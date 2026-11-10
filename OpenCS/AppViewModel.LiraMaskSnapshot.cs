using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CScore.Fem;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS
{
   /// <summary>
   /// Снимок результатов ЛИРЫ для масок фрагментов (как у SCAD, <c>TakeScadMaskSnapshot</c>): перемещения узлов и усилия
   /// загружений КЭ сетки схемы из открытой ЛИРЫ → вложение <see cref="FemSchemaSourceFileKind.LiraMaskResults"/>.
   /// </summary>
   public partial class AppViewModel
   {
      public ICommand TakeLiraMaskSnapshotCommand { get; set; } = null!;

      void InitLiraMaskSnapshotCommand() =>
         TakeLiraMaskSnapshotCommand = new RelayCommand(p => TakeLiraMaskSnapshot(p as FemSchema), _ => !IsBusy);

      async void TakeLiraMaskSnapshot(FemSchema? schema)
      {
         if (IsBusy || schema == null) return;
         if (schema.SourceType != "lira")
         {
            MessageBox.Show(string.Format(Loc.S("LiraMaskSnapshotNotLiraSchema"), schema.Tag), Loc.S("LiraMaskSnapshotTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }
         var nodes = db.GetFemMeshNodes(schema.Id)
            .Select(n => int.TryParse(n.NodeTag, out int id) ? id : 0).Where(id => id > 0).Distinct().Order().ToList();
         var bars = new List<int>();
         var shells = new List<int>();
         var thickness = new Dictionary<int, double>();
         foreach (var e in db.GetFemMeshElements(schema.Id))
         {
            if (!int.TryParse(e.ElemTag, out int id)) continue;
            if (e.ElemType == "shell")
            {
               shells.Add(id);
               if (e.ThicknessM is double h) thickness[id] = h;
            }
            else if (e.ElemType == "beam") bars.Add(id);
         }
         if (bars.Count + shells.Count == 0)
         {
            MessageBox.Show(Loc.S("LiraMaskSnapshotNoElements"), Loc.S("LiraMaskSnapshotTitle"),
               MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
         }

         var cts = BeginBusyWithCancellation(string.Format(Loc.S("LiraMaskSnapshotReading"), nodes.Count, bars.Count + shells.Count),
            indeterminate: false);
         var progress = new Progress<double>(f => ReportBusyProgress(f));
         try
         {
            var (build, data, docName) = await Task.Run(() =>
            {
               var read = LiraApiMaskSnapshotReader.Read(nodes, bars, shells, progress, cts.Token);
               if (!string.Equals(read.DocumentName, schema.Tag, StringComparison.OrdinalIgnoreCase))
                  throw new InvalidOperationException(string.Format(Loc.S("LiraMaskSnapshotWrongDocument"), read.DocumentName, schema.Tag));
               // Перемещения API — в метрах и радианах независимо от единиц документа.
               var b = LiraResultSnapshotBuilder.Build(read.Loads, read.Units, 1.0, thickness, read.DocumentName,
                  DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
               return (b, CScore.Fem.Fragment.Masks.ImportedResultSnapshotCodec.Encode(b.Snapshot), read.DocumentName);
            }, cts.Token);

            db.SaveFemSchemaSourceFile(schema.Id, FemSchemaSourceFileKind.LiraMaskResults, docName, data);

            if (build.ShellsWithoutThickness > 0)
               LogService.Warning(string.Format(Loc.S("MaskSnapshotShellsNoThickness"), build.ShellsWithoutThickness));
            var loads = build.Snapshot.Loads;
            string done = string.Format(Loc.S("LiraMaskSnapshotSuccess"), schema.Tag, loads.Count,
               loads.Count > 0 ? loads.Max(l => l.NodeNums.Length) : 0,
               loads.Count > 0 ? loads.Max(l => l.BarNums.Length) : 0,
               loads.Count > 0 ? loads.Max(l => l.ShellNums.Length) : 0);
            LogService.Info(done);
            EndBusy(done);
         }
         catch (OperationCanceledException)
         {
            EndBusy(Loc.S("LiraMaskSnapshotCancelled"));
         }
         catch (Exception ex)
         {
            EndBusy();
            string msg = string.Format(Loc.S("LiraMaskSnapshotError"), ex.Message);
            LogService.Error(msg + Environment.NewLine + ex);
            MessageBox.Show(msg, Loc.S("LiraMaskSnapshotTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
         }
      }
   }
}

using System.Windows;
using System.Windows.Input;
using CScore.Fem;
using CScore.Fem.Fields;
using CScore.Import;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS
{
   /// <summary>
   /// Поля из таблиц точек (спека § 5): новое поле на цели из дерева и 3D, правка, повтор с новой таблицей, включение,
   /// порядок наложения, удаление. Любое изменение поля сбрасывает результаты постановок схемы.
   /// </summary>
   public partial class AppViewModel
   {
      ICommand? assignFemFieldToGroupCommand;

      /// <summary>«Назначить из таблицы точек…» у группы КонЭ или КЭ дерева (параметр — группа).</summary>
      public ICommand AssignFemFieldToGroupCommand => assignFemFieldToGroupCommand ??= new RelayCommand(p =>
      {
         if (p is FemMemberGroup group && db.FemSchemas.FirstOrDefault(s => s.Id == group.SchemaId) is { } schema)
            CreateFemField(schema, new FemFieldTargets { GroupIds = [group.Id] });
      });

      /// <summary>Новое поле на цели; по «ОК» — сохранение, пересчёт, сброс постановок, мозаика величины в 3D.</summary>
      public void CreateFemField(FemSchema schema, FemFieldTargets targets) =>
         OpenFemFieldDialog(schema, targets, null, newTable: false);

      public void EditFemField(FemFieldItem item) =>
         OpenFemFieldDialog(item.Schema, item.Field.Definition.Targets, item.Field, newTable: false);

      /// <summary>Те же цели и совмещение, новые точки (ГТО пересчитали): окно открывается с выбором файла.</summary>
      public void RepeatFemFieldWithNewTable(FemFieldItem item) =>
         OpenFemFieldDialog(item.Schema, item.Field.Definition.Targets, item.Field, newTable: true);

      void OpenFemFieldDialog(FemSchema schema, FemFieldTargets targets, FemField? existing, bool newTable)
      {
         var ctx = FemFieldService.LoadContext(db, schema.Id);
         var vm = new FemFieldDialogVM(ctx, targets, LiraImportSettings.TonToKnFactor, existing);
         var dialog = new Views.FemFieldDialog(vm) { Owner = Application.Current.MainWindow };
         if (newTable) dialog.Loaded += (_, _) => dialog.OpenCsvOnLoad();
         if (dialog.ShowDialog() != true) return;
         var field = vm.BuildField(schema.Id);
         if (existing != null) (field.Enabled, field.SortOrder) = (existing.Enabled, existing.SortOrder);
         db.SaveFemField(field);
         var computed = FemFieldService.Recompute(db, field, ctx);
         foreach (var d in computed.Evaluation.Diagnostics)
            LogService.Warning(d.Tags.Count > 0 ? $"{d.Message}: {d.Tags.Count}" : d.Message);
         AfterFemFieldsChanged(schema);
         ShowFemBoundaryMosaic(schema, FemFieldQuantities.Get(field.Quantity).Target == FemFieldTargetKind.MeshNode
            ? PlateRebarMosaicSourceKind.Springs : PlateRebarMosaicSourceKind.Foundation);
      }

      /// <summary>«Показать на схеме»: мозаика C1 или пружин в 3D.</summary>
      public void ShowFemField(FemFieldItem item) => ShowFemBoundaryMosaic(item.Schema,
         item.IsSpring ? PlateRebarMosaicSourceKind.Springs : PlateRebarMosaicSourceKind.Foundation);

      public void ToggleFemField(FemFieldItem item)
      {
         db.SetFemFieldEnabled(item.Field.Id, !item.Field.Enabled);
         AfterFemFieldsChanged(item.Schema);
      }

      /// <summary>Сдвигает поле в порядке наложения на <paramref name="delta"/> (−1 — выше, раньше; +1 — ниже, позже).</summary>
      public void MoveFemField(FemFieldItem item, int delta)
      {
         var ids = db.GetFemFields(item.Schema.Id).Select(f => f.Id).ToList();
         int i = ids.IndexOf(item.Field.Id), j = i + delta;
         if (i < 0 || j < 0 || j >= ids.Count) return;
         (ids[i], ids[j]) = (ids[j], ids[i]);
         db.SetFemFieldOrder(ids);
         AfterFemFieldsChanged(item.Schema);
      }

      public void DeleteFemField(FemFieldItem item)
      {
         if (MessageBox.Show(string.Format(Loc.S("FemFieldDeleteConfirm"), item.Field.Name), Loc.S("Confirmation"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
         db.DeleteFemField(item.Field.Id);
         AfterFemFieldsChanged(item.Schema);
      }

      /// <summary>
      /// Поля изменились: результаты постановок схемы сбрасываются, ветки полей и ГУ дерева перечитываются, у открытой
      /// страницы схемы — глифы ГУ и мозаика 3D.
      /// </summary>
      void AfterFemFieldsChanged(FemSchema schema)
      {
         int invalidated = InvalidateFemSchemaAnalyses(schema);
         if (invalidated > 0) LogService.Info(string.Format(Loc.S("ScadBoundaryAnalysesInvalidated"), invalidated));
         ReloadFemMeshSnapshotTree(schema.Id);
         if (currentFemSchema == schema && CurrentPage is Views.FemSchemaPage page) page.ReloadBoundary();
      }
   }
}

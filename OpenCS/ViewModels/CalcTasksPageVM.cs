using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.Views;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// ViewModel страницы расчётных задач.
   /// </summary>
   public class CalcTasksPageVM : ViewModelBase
   {
      readonly AppViewModel _app;
      readonly CalcTasksPage _page;

      CalcTaskVM?  selectedTask   = null;
      CalcResult?  selectedResult = null;

      public ObservableCollection<CalcTaskVM> Tasks { get; } = [];

      public ObservableCollection<CalcResult> SelectedTaskResults { get; } = [];

      public CalcTaskVM? SelectedTask
      {
         get => selectedTask;
         set
         {
            selectedTask = value;
            RefreshResults();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPrestressLossSelected));
         }
      }

      public bool IsPrestressLossSelected => SelectedTask?.Model.Kind == "prestress_loss";

      public CalcResult? SelectedResult
      {
         get => selectedResult;
         set { selectedResult = value; OnPropertyChanged(); }
      }

      public ICommand NewTaskCommand             { get; }
      public ICommand RunTaskCommand             { get; }
      public ICommand EditTaskCommand            { get; }
      public ICommand DeleteTaskCommand          { get; }
      public ICommand ViewResultCommand          { get; }
      public ICommand DeleteResultCommand        { get; }
      public ICommand DeleteAllResultsCommand    { get; }
      public ICommand EditPrestressParamsCommand { get; }

      public CalcTasksPageVM(AppViewModel app, CalcTasksPage page)
      {
         _app  = app;
         _page = page;

         RebuildTaskVMs();
         _app.CalcTasks.CollectionChanged   += (_, _) => RebuildTaskVMs();
         _app.CalcResults.CollectionChanged += (_, _) => RefreshResults();

         NewTaskCommand    = new RelayCommand(_ => NewTask());
         RunTaskCommand    = new RelayCommand(_ => _ = RunTaskAsync(),
            _ => SelectedTask != null && !_app.IsBusy);
         EditTaskCommand   = new RelayCommand(_ => EditTask(),  _ => SelectedTask != null);
         DeleteTaskCommand = new RelayCommand(_ => DeleteTask(), _ => SelectedTask != null);
         ViewResultCommand   = new RelayCommand(_ => ViewResult(),   _ => SelectedResult != null);
         DeleteResultCommand = new RelayCommand(_ => DeleteResult(), _ => SelectedResult != null);
         DeleteAllResultsCommand = new RelayCommand(_ => DeleteAllResults(), _ => SelectedTask != null);
         EditPrestressParamsCommand = new RelayCommand(
             _ => EditPrestressParams(), _ => IsPrestressLossSelected);
      }

      void RebuildTaskVMs()
      {
         Tasks.Clear();
         foreach (var ct in _app.CalcTasks)
         {
            var vm = new CalcTaskVM(ct);
            var sec = _app.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
            var fs  = _app.BarForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
            var fi  = fs?.Items.FirstOrDefault(i => i.Id == ct.ForceItemId);
            vm.SectionTag    = sec?.Tag  ?? "?";
            vm.ForceSetTag   = fs?.Tag   ?? "?";
            vm.ForceItemLabel = fi?.Label ?? "?";
            Tasks.Add(vm);
         }
         RefreshResults();
      }

      void RefreshResults()
      {
         SelectedTaskResults.Clear();
         if (SelectedTask == null) return;
         var tid = SelectedTask.Model.Id;
         foreach (var r in _app.CalcResults.Where(r => r.TaskId == tid))
            SelectedTaskResults.Add(r);
      }

      void NewTask()
      {
         var dlg = new CalcTaskPropsDialog(_app) { Owner = Window.GetWindow(_page) };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;

         var ct = dlg.Result;
         ct.Num = _app.CalcTasks.Count > 0 ? _app.CalcTasks.Max(t => t.Num) + 1 : 1;
         _app.db.SaveCalcTask(ct);
         _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), ct.Tag));

         if (ct.Kind == "prestress_loss")
         {
            var pdlg = new PrestressLossDialog(_app, ct) { Owner = Window.GetWindow(_page) };
            pdlg.ShowDialog();
         }
      }

      void EditTask()
      {
         if (SelectedTask == null) return;
         var dlg = new CalcTaskPropsDialog(_app, SelectedTask.Model) { Owner = Window.GetWindow(_page) };
         if (dlg.ShowDialog() != true || dlg.Result == null) return;

         var src = dlg.Result;
         var ct  = SelectedTask.Model;
         ct.Tag         = src.Tag;
         ct.Kind        = src.Kind;
         ct.SectionId   = src.SectionId;
         ct.ForceSetId  = src.ForceSetId;
         ct.ForceItemId = src.ForceItemId;
         ct.CalcType    = src.CalcType;
         ct.ParamsJson  = src.ParamsJson;
         _app.db.SaveCalcTask(ct);
         RebuildTaskVMs();

         if (ct.Kind == "prestress_loss")
         {
            var pdlg = new PrestressLossDialog(_app, ct) { Owner = Window.GetWindow(_page) };
            pdlg.ShowDialog();
         }
      }

      void EditPrestressParams()
      {
         if (SelectedTask == null) return;
         var dlg = new PrestressLossDialog(_app, SelectedTask.Model) { Owner = Window.GetWindow(_page) };
         dlg.ShowDialog();
      }

      async Task RunTaskAsync()
      {
         if (SelectedTask == null) return;
         var taskRef = SelectedTask;
         await CalcTaskExecutor.RunAsync(_app, taskRef.Model, onResultsChanged: () =>
         {
            RefreshResults();
            SelectedResult = SelectedTaskResults.LastOrDefault();
         }, navigateToResult: true);
      }

      public void SelectTask(int taskId)
      {
         SelectedTask = Tasks.FirstOrDefault(t => t.Model.Id == taskId);
      }

      public void ViewResult()
      {
         if (SelectedResult == null) return;
         _app.CurrentPage = new CalcResultView(SelectedResult, _app);
      }

      void DeleteTask()
      {
         if (SelectedTask == null) return;
         var res = MessageBox.Show(Loc.S("ConfirmDeleteCalcTask"), Loc.S("Warning"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         _app.db.DeleteCalcTask(SelectedTask.Model);
      }

      void DeleteResult()
      {
         if (SelectedResult == null) return;
         _app.db.DeleteCalcResult(SelectedResult);
         RefreshResults();
      }

      void DeleteAllResults()
      {
         if (SelectedTask == null) return;
         var tag = SelectedTask.Model.Tag;
         var res = MessageBox.Show(string.Format(Loc.S("ConfirmDeleteCalcResults"), tag), Loc.S("Warning"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
         if (res != MessageBoxResult.Yes) return;
         _app.db.DeleteCalcResultsByTaskId(SelectedTask.Model.Id);
         RefreshResults();
      }
   }
}

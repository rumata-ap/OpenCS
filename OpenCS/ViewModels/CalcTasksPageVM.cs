using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.Views;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OpenCS.ViewModels
{
   /// <summary>
   /// ViewModel страницы расчётных задач. Создание, правка, копия и удаление — те же команды, что в дереве
   /// (<see cref="AppViewModel"/>, служба <c>CalcTaskAuthoring</c>).
   /// </summary>
   public class CalcTasksPageVM : ViewModelBase, IDisposable
   {
      readonly AppViewModel _app;
      readonly CalcTasksPage? _page;

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
            OnPropertyChanged(nameof(IsPunchingSelected));
         }
      }

      public bool IsPrestressLossSelected => SelectedTask?.Model.Kind == "prestress_loss";

      /// <summary>Выбрана задача продавливания — показать кнопку окна параметров.</summary>
      public bool IsPunchingSelected => PunchingBatchRunner.IsPunchingKind(SelectedTask?.Model.Kind);

      public CalcResult? SelectedResult
      {
         get => selectedResult;
         set { selectedResult = value; OnPropertyChanged(); }
      }

      public ICommand NewTaskCommand             { get; }
      public ICommand RunTaskCommand             { get; }
      public ICommand EditTaskCommand            { get; }
      public ICommand DuplicateTaskCommand       { get; }
      public ICommand DeleteTaskCommand          { get; }
      public ICommand ViewResultCommand          { get; }
      public ICommand DeleteResultCommand        { get; }
      public ICommand DeleteAllResultsCommand    { get; }
      public ICommand EditPrestressParamsCommand { get; }
      public ICommand EditPunchingParamsCommand  { get; }

      public CalcTasksPageVM(AppViewModel app, CalcTasksPage? page)
      {
         _app  = app;
         _page = page;

         RebuildTaskVMs();
         _app.CalcTasks.CollectionChanged   += OnTasksChanged;
         _app.CalcResults.CollectionChanged += OnResultsChanged;
         _app.CalcTaskModified              += OnTaskModified;

         NewTaskCommand    = new RelayCommand(_ => Select(_app.NewCalcTask()));
         RunTaskCommand    = new RelayCommand(_ => _ = RunTaskAsync(),
            _ => SelectedTask != null && !_app.IsBusy);
         EditTaskCommand   = new RelayCommand(_ => _app.EditCalcTask(SelectedTask?.Model), _ => SelectedTask != null);
         DuplicateTaskCommand = new RelayCommand(_ => Select(_app.DuplicateCalcTask(SelectedTask?.Model)), _ => SelectedTask != null);
         DeleteTaskCommand = new RelayCommand(_ => _app.DeleteCalcTask(SelectedTask?.Model), _ => SelectedTask != null);
         ViewResultCommand   = new RelayCommand(_ => ViewResult(),   _ => SelectedResult != null);
         DeleteResultCommand = new RelayCommand(_ => DeleteResult(), _ => SelectedResult != null);
         DeleteAllResultsCommand = new RelayCommand(_ => _app.DeleteCalcResults(SelectedTask?.Model), _ => SelectedTask != null);
         EditPrestressParamsCommand = new RelayCommand(
             _ => _app.ShowCalcTaskParamsWindow(SelectedTask!.Model), _ => IsPrestressLossSelected);
         EditPunchingParamsCommand = new RelayCommand(
             _ => _app.ShowCalcTaskParamsWindow(SelectedTask!.Model), _ => IsPunchingSelected);
      }

      void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTaskVMs();
      void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshResults();
      void OnTaskModified() => RebuildTaskVMs();

      /// <summary>Отписка от коллекций проекта (страница закрыта): иначе каждое открытие держит VM до закрытия проекта.</summary>
      public void Dispose()
      {
         _app.CalcTasks.CollectionChanged   -= OnTasksChanged;
         _app.CalcResults.CollectionChanged -= OnResultsChanged;
         _app.CalcTaskModified              -= OnTaskModified;
      }

      void Select(CalcTask? ct)
      {
         if (ct != null) SelectTask(ct.Id);
      }

      void RebuildTaskVMs()
      {
         int? keep = SelectedTask?.Model.Id;
         Tasks.Clear();
         foreach (var ct in _app.CalcTasks)
            Tasks.Add(CalcTaskVM.Build(ct, _app.db));
         selectedTask = keep is int id ? Tasks.FirstOrDefault(t => t.Model.Id == id) : null;
         OnPropertyChanged(nameof(SelectedTask));
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

      async Task RunTaskAsync()
      {
         if (SelectedTask == null) return;
         var taskRef = SelectedTask;
         await CalcTaskExecutor.RunAsync(_app, taskRef.Model, onResultsChanged: () =>
         {
            RefreshResults();
            SelectedResult = SelectedTaskResults.LastOrDefault();
         }, showResult: r => _app.CurrentPage = new CalcResultView(r, _app));
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

      void DeleteResult()
      {
         if (SelectedResult == null) return;
         _app.db.DeleteCalcResult(SelectedResult);
         RefreshResults();
      }
   }
}

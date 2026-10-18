using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Результат пакетного подбора сеток Капри-Мори (<c>shell_simpl_capri_design_uls_batch/sls_batch</c>).</summary>
public partial class CapraMauryDesignBatchResultView : System.Windows.Controls.UserControl
{
    readonly AppViewModel _app;
    readonly CalcTask _task;

    public CapraMauryDesignBatchResultView(CalcResult result, CalcTask task, AppViewModel app)
    {
        _app = app;
        _task = task;
        InitializeComponent();
        DataContext = new CapraMauryDesignBatchResultVM(result, task);
        RowsGrid.SelectionChanged += (_, _) => CreateTaskBtn.IsEnabled = RowsGrid.SelectedItem != null;
    }

    void RowsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => CreateTask();

    void CreateTask_Click(object sender, RoutedEventArgs e) => CreateTask();

    /// <summary>Одиночная задача подбора по выбранной строке: параметры пакета и усилия строки (уже приведённые к кН/м).</summary>
    void CreateTask()
    {
        if (RowsGrid.SelectedItem is not CapraMauryDesignBatchRowVM row) return;
        var section = _app.PlateSections.FirstOrDefault(s => s.Id == _task.SectionId);
        if (section == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var p = ShellSimplParams.Parse(_task.ParamsJson);
        p.Nx = row.Source.Nx; p.Ny = row.Source.Ny; p.Nxy = row.Source.Nxy;
        p.Mx = row.Source.Mx; p.My = row.Source.My; p.Mxy = row.Source.Mxy;
        p.AutoStressToForce = false;
        p.StripModel = "ndm";

        var tag = $"{section.Tag} — {row.Label}";
        var newTask = new CalcTask
        {
            Kind = _task.Kind.Replace("_batch", ""),
            SectionId = _task.SectionId,
            CalcType = _task.CalcType,
            Tag = tag,
            ForceSetId = _task.ForceSetId,
            ParamsJson = JsonSerializer.Serialize(p),
        };
        newTask.Num = _app.CalcTasks.Count > 0 ? _app.CalcTasks.Max(t => t.Num) + 1 : 1;
        _app.db.SaveCalcTask(newTask);
        _app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), tag));
    }
}

public sealed class CapraMauryDesignBatchRowVM
{
    public CapraMauryDesignBatchRow Source { get; init; } = new();
    public int Num => Source.Num;
    public string Label => Source.Label;
    public bool Feasible => Source.Feasible;
    public string Diagnostics => Source.Diagnostics;
    public string TopX => Cm(Source.TopAsx);
    public string TopY => Cm(Source.TopAsy);
    public string BotX => Cm(Source.BotAsx);
    public string BotY => Cm(Source.BotAsy);

    internal static string Cm(double m2) => (m2 * 1e4).ToString("F2", CultureInfo.CurrentCulture);
}

public sealed class CapraMauryDesignEnvelopeRowVM
{
    public string Component { get; init; } = "";
    public string As { get; init; } = "";
    public string Row { get; init; } = "";
}

public sealed class CapraMauryDesignBatchResultVM : ViewModelBase
{
    public string Title { get; }
    public string Summary { get; } = "";
    public string ModelNote { get; } = Loc.S("CapriDesignModelNote");
    public Brush StatusBrush { get; }
    public ObservableCollection<CapraMauryDesignBatchRowVM> Rows { get; } = [];
    public ObservableCollection<CapraMauryDesignEnvelopeRowVM> Envelope { get; } = [];

    public CapraMauryDesignBatchResultVM(CalcResult result, CalcTask task)
    {
        Title = task.Tag;
        var node = JsonNode.Parse(result.DataJson);
        if (node?["error"] is JsonNode err)
        {
            Summary = err.GetValue<string>();
            StatusBrush = CapraMauryDesignResultVM.FailBrush;
            return;
        }
        var data = JsonSerializer.Deserialize<CapraMauryDesignBatchData>(result.DataJson) ?? new CapraMauryDesignBatchData();
        if (data.Limit == "sls")
            ModelNote = string.Format(CultureInfo.CurrentCulture, Loc.S("CapriDesignModelNoteSls"), data.AcrcLimMm);
        foreach (var r in data.Rows) Rows.Add(new CapraMauryDesignBatchRowVM { Source = r });
        foreach (var e in data.Envelope)
        {
            Envelope.Add(new CapraMauryDesignEnvelopeRowVM
            {
                Component = Loc.S(e.Component switch
                {
                    "top_x" => "CapriDesignTopX",
                    "top_y" => "CapriDesignTopY",
                    "bot_x" => "CapriDesignBotX",
                    _ => "CapriDesignBotY",
                }),
                As = CapraMauryDesignBatchRowVM.Cm(e.As),
                Row = e.Num > 0 || !string.IsNullOrEmpty(e.Label) ? $"{e.Num} — {e.Label}" : "—",
            });
        }
        Summary = string.Format(Loc.S("CapriDesignBatchSummary"), data.Total, data.Feasible, data.Total - data.Feasible);
        StatusBrush = data.Feasible == data.Total ? CapraMauryDesignResultVM.OkBrush : CapraMauryDesignResultVM.FailBrush;
    }
}

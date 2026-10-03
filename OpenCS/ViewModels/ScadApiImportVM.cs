using System.IO;
using System.Windows.Input;
using OpenCS.Services;
using OpenCS.Services.Scad;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Диалог импорта схемы из проекта SCAD (.SPR): файл, каталог SCAD, рабочий каталог, что читать.
/// После выбора файла проект открывается в фоне и в строке состояния показывается его сводка.
/// </summary>
public sealed class ScadApiImportVM : ViewModelBase
{
    readonly IFileDialogService _files;
    int _probeGeneration;

    public ScadApiImportVM(ScadApiSettings settings, IFileDialogService files)
    {
        _files = files;
        _dllDirectory = ScadInstallLocator.ContainsDll(settings.DllDirectory)
            ? settings.DllDirectory!
            : ScadInstallLocator.FindDllDirectory() ?? settings.DllDirectory ?? "";
        _workDirectory = !string.IsNullOrWhiteSpace(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory)
            ? settings.WorkDirectory
            : ScadInstallLocator.FindWorkDirectory() ?? settings.WorkDirectory ?? "";
        _sprPath = settings.LastProjectPath is { } last && File.Exists(last) ? last : "";
        _readOutputAxes = settings.ReadOutputAxes;
        _concreteGroups = settings.ConcreteGroupsAsMemberGroups;

        BrowseSprCommand = new RelayCommand(_ =>
        {
            if (_files.OpenFile(Loc.S("ScadApiSprFilter"), Loc.S("ScadApiSprBrowseTitle")) is { } f) SprPath = f;
        });
        BrowseDllCommand = new RelayCommand(_ =>
        {
            if (_files.SelectFolder(Loc.S("ScadApiDllBrowseTitle"), DllDirectory) is { } d) DllDirectory = d;
        });
        BrowseWorkCommand = new RelayCommand(_ =>
        {
            if (_files.SelectFolder(Loc.S("ScadApiWorkBrowseTitle"), WorkDirectory) is { } d) WorkDirectory = d;
        });

        _status = Loc.S(_sprPath.Length == 0 ? "ScadApiStatusChooseFile" : "ScadApiStatusOpening");
        if (_sprPath.Length > 0) _ = ProbeAsync();
    }

    public ICommand BrowseSprCommand { get; }
    public ICommand BrowseDllCommand { get; }
    public ICommand BrowseWorkCommand { get; }

    string _sprPath;
    /// <summary>Файл проекта .SPR.</summary>
    public string SprPath
    {
        get => _sprPath;
        set { if (_sprPath == value) return; _sprPath = value; OnPropertyChanged(); _ = ProbeAsync(); }
    }

    string _dllDirectory;
    /// <summary>Каталог с SCADAPIX.dll.</summary>
    public string DllDirectory
    {
        get => _dllDirectory;
        set { if (_dllDirectory == value) return; _dllDirectory = value; OnPropertyChanged(); _ = ProbeAsync(); }
    }

    string _workDirectory;
    /// <summary>Рабочий каталог SCAD (результаты расчёта).</summary>
    public string WorkDirectory
    {
        get => _workDirectory;
        set { if (_workDirectory == value) return; _workDirectory = value; OnPropertyChanged(); _ = ProbeAsync(); }
    }

    bool _readOutputAxes;
    /// <summary>Читать оси выдачи усилий пластин.</summary>
    public bool ReadOutputAxes { get => _readOutputAxes; set { _readOutputAxes = value; OnPropertyChanged(); } }

    bool _concreteGroups;
    /// <summary>Создать группы КЭ по ЖБ-группам SCAD.</summary>
    public bool ConcreteGroupsAsMemberGroups { get => _concreteGroups; set { _concreteGroups = value; OnPropertyChanged(); } }

    string _status;
    /// <summary>Сводка открытого проекта или сообщение об ошибке.</summary>
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    bool _isProbing;
    /// <summary>Проект открывается в фоне.</summary>
    public bool IsProbing { get => _isProbing; private set { _isProbing = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanImport)); } }

    bool _probeOk;
    /// <summary>Проект открыт успешно — можно импортировать.</summary>
    public bool CanImport => _probeOk && !IsProbing;

    /// <summary>Записать выбор в настройки.</summary>
    public void ApplyTo(ScadApiSettings s)
    {
        s.DllDirectory = DllDirectory;
        s.WorkDirectory = WorkDirectory;
        s.LastProjectPath = SprPath;
        s.ReadOutputAxes = ReadOutputAxes;
        s.ConcreteGroupsAsMemberGroups = ConcreteGroupsAsMemberGroups;
    }

    async Task ProbeAsync()
    {
        int generation = ++_probeGeneration;
        _probeOk = false;
        if (string.IsNullOrWhiteSpace(SprPath))
        {
            IsProbing = false;
            Status = Loc.S("ScadApiStatusChooseFile");
            return;
        }
        IsProbing = true;
        Status = Loc.S("ScadApiStatusOpening");
        string spr = SprPath, dll = DllDirectory, work = WorkDirectory;

        string text;
        bool ok = false;
        try
        {
            ScadApiTrace.Write($"Проверка проекта #{generation}: SPR «{spr}», DLL «{dll}», работа «{work}»");
            var sum = await Task.Run(() =>
            {
                ScadApiTrace.Write($"Проверка #{generation}: ожидание Gate");
                ScadApiNative.Gate.Wait();
                ScadApiTrace.Write($"Проверка #{generation}: Gate получен");
                try
                {
                    var native = ScadApiNative.Load(dll);
                    using var s = new ScadApiSession(native);
                    s.Open(spr);
                    return ScadApiReader.ReadSummary(s, string.IsNullOrWhiteSpace(work) ? null : work);
                }
                finally { ScadApiNative.Gate.Release(); }
            });
            text = FormatSummary(sum);
            ok = true;
        }
        catch (ScadApiException ex) { text = ex.Format(Loc.S); ScadApiTrace.Write($"Проверка #{generation}: {text}"); }
        catch (Exception ex)
        {
            text = string.Format(Loc.S("ScadApiUnexpectedError"), ex.Message);
            ScadApiTrace.Write($"Проверка #{generation}: {ex}");
        }
        if (ok) ScadApiTrace.Write($"Проверка #{generation}: готово");

        if (generation != _probeGeneration) return; // за это время выбор изменился
        _probeOk = ok;
        Status = text;
        IsProbing = false;
        CommandManager.InvalidateRequerySuggested();
    }

    static string FormatSummary(ScadProjectSummary s)
    {
        string results = s.ResultLoads is int loads
            ? string.Format(Loc.S("ScadApiStatusResults"), loads,
                Loc.S(s.HasForces ? "ScadApiYes" : "ScadApiNo"), Loc.S(s.HasRsu ? "ScadApiYes" : "ScadApiNo"))
            : Loc.S("ScadApiStatusNoResults");
        return string.Format(Loc.S("ScadApiStatusSummary"),
            s.Nodes, s.Bars + s.Shells, s.Bars, s.Shells, s.SkippedElements, s.Stiffnesses, s.Groups, s.Blocks,
            s.ConcreteGroups, s.AxisSystems, s.LengthUnit, s.SectionUnit, s.ForceUnit)
            + Environment.NewLine + results;
    }
}

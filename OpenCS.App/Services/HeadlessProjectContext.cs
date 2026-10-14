using CScore;
using CScore.Fem;
using CScore.Fire.Entities;
using OpenCS.Reporting;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Открытый проект без UI (консоль, тесты, будущий MCP): БД с загруженными коллекциями и настройками расчёта.
/// Журнал, уведомления и занятость задаёт потребитель. Открывает файл как есть — защиту файла пользователя
/// (работа на копии) обеспечивает вызывающий код.
/// </summary>
public sealed class HeadlessProjectContext : IProjectContext, IDisposable
{
    public DatabaseService Db { get; }
    public CalcSettings CalcSettings { get; }
    public GmshSettings GmshSettings { get; }
    public ILogService LogService { get; }
    public IUserNotifier Notifier { get; }
    public IBusyHost Busy { get; }
    public ReportProviderRegistry ReportProviders { get; } = ReportProviderRegistry.CreateDefault();

    HeadlessProjectContext(DatabaseService db, ILogService log, IUserNotifier notifier, IBusyHost busy)
    {
        Db = db;
        LogService = log;
        Notifier = notifier;
        Busy = busy;
        // Как AppViewModel.LoadSettingsFromDb: настройки хранятся в файле проекта.
        CalcSettings = db.LoadCalcSettings();
        GmshSettings = db.LoadGmshSettings();
    }

    /// <summary>Открывает проект по пути (миграции схемы применяются к этому файлу) и загружает его данные.</summary>
    public static HeadlessProjectContext Open(string path, ILogService log, IUserNotifier notifier, IBusyHost busy)
    {
        var db = new DatabaseService(path);
        try
        {
            db.LoadAll();
            return new HeadlessProjectContext(db, log, notifier, busy);
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    public IReadOnlyList<Material> Materials => Db.Materials;
    public IReadOnlyList<Diagramm> Diagrams => Db.Diagrams;
    public IReadOnlyList<CrossSection> CrossSections => Db.CrossSections;
    public IReadOnlyList<PlateSection> PlateSections => Db.PlateSections;
    public IReadOnlyList<FireSectionDef> FireSections => Db.FireSections;
    public IReadOnlyList<ForceSet> ForceSets => Db.ForceSets;
    public IReadOnlyList<ForceSet> BarForceSets => Db.ForceSets.Where(f => f.Kind == "bar").ToList();
    public IReadOnlyList<FemSchema> FemSchemas => Db.FemSchemas;
    public IReadOnlyList<CalcTask> CalcTasks => Db.CalcTasks;

    public void Dispose() => Db.Dispose();
}

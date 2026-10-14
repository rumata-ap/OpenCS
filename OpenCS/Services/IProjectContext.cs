using CScore;
using CScore.Fem;
using CScore.Fire.Entities;
using OpenCS.Reporting;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>
/// Открытый проект для исполнителей расчётов: БД, коллекции, настройки, журнал, занятость, уведомления.
/// Реализуют <c>AppViewModel</c> (WPF) и консольный контекст. Коллекции исполнители только читают.
/// </summary>
public interface IProjectContext
{
    DatabaseService Db { get; }

    /// <summary>Текущие настройки расчёта (объект подменяется при применении настроек).</summary>
    CalcSettings CalcSettings { get; }
    GmshSettings GmshSettings { get; }
    ILogService LogService { get; }
    IUserNotifier Notifier { get; }
    IBusyHost Busy { get; }
    ReportProviderRegistry ReportProviders { get; }

    IReadOnlyList<Material> Materials { get; }
    IReadOnlyList<Diagramm> Diagrams { get; }
    IReadOnlyList<CrossSection> CrossSections { get; }
    IReadOnlyList<PlateSection> PlateSections { get; }
    IReadOnlyList<FireSectionDef> FireSections { get; }
    IReadOnlyList<ForceSet> ForceSets { get; }

    /// <summary>Наборы усилий стержней (Kind = "bar").</summary>
    IReadOnlyList<ForceSet> BarForceSets { get; }
    IReadOnlyList<FemSchema> FemSchemas { get; }
    IReadOnlyList<CalcTask> CalcTasks { get; }
}

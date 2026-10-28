using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Итог команды «Фрагмент по отметкам…»: новая схема (null — отказ), строки отчёта, строка журнала.</summary>
public sealed record StoreyFragmentFlowResult(FemSchema? Schema, IReadOnlyList<StoreyFragmentReportRow> Rows, string LogMessage)
{
    public bool IsError => Schema is null;
}

/// <summary>Сборка фрагмента из меню схемы без окон: сервис сборки → строки отчёта и сообщение журнала.</summary>
public static class StoreyFragmentMenuFlow
{
    /// <summary>Снимок полосы и построитель — в фоне; запись схемы — в вызывающем потоке (коллекция схем дерева).</summary>
    public static async Task<StoreyFragmentFlowResult> RunAsync(DatabaseService db, FemSchema parent, StoreyFragmentRequest request)
    {
        var prepared = await Task.Run(() => StoreyFragmentService.Prepare(db, parent.Id, request));
        var (schema, draft) = StoreyFragmentService.Commit(db, parent.Id, request, prepared);
        var rows = StoreyFragmentReportRows.From(draft.Diagnostics);
        string F(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);
        string log = schema is null
            ? string.Format(Loc.S("StoreyFragmentFailed"), parent.Tag, F(request.Z1), F(request.Z2), rows.Count(r => r.IsError))
            : string.Format(Loc.S("StoreyFragmentBuilt"), schema.Tag, parent.Tag, F(request.Z1), F(request.Z2),
                draft.Members.Count, draft.LoadCases.Count, rows.Count);
        return new(schema, rows, log);
    }
}

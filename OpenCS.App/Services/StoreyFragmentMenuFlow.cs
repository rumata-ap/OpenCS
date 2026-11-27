using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Итог команды «Фрагмент по отметкам…»: новая схема (null — отказ), строки отчёта, строка журнала.</summary>
public sealed record StoreyFragmentFlowResult(FemSchema? Schema, IReadOnlyList<StoreyFragmentReportRow> Rows, string LogMessage)
{
    public bool IsError => Schema is null;
}

/// <summary>Сборка фрагмента из меню схемы без окон: сервис сборки → строки отчёта и сообщение журнала.</summary>
public static class StoreyFragmentMenuFlow
{
    /// <summary>
    /// Снимок полосы и построитель — в фоне; запись схемы — в вызывающем потоке (коллекция схем дерева). Имя занято или
    /// пусто — отказ без сборки.
    /// </summary>
    public static async Task<StoreyFragmentFlowResult> RunAsync(DatabaseService db, FemSchema parent, StoreyFragmentRequest request)
    {
        if (FragmentAuthoring.NameRefusal(db, request) is not null) return Finish(db, parent, request, null);
        var prepared = await Task.Run(() => StoreyFragmentService.Prepare(db, parent.Id, request));
        return Finish(db, parent, request, prepared);
    }

    /// <summary>Запись подготовленного черновика и отчёт; <paramref name="prepared"/> = null — отказ по имени.</summary>
    internal static StoreyFragmentFlowResult Finish(DatabaseService db, FemSchema parent, StoreyFragmentRequest request,
        StoreyFragmentPrepared? prepared)
    {
        string F(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);
        if (prepared is null)
        {
            string error = FragmentAuthoring.NameRefusal(db, request) ?? Loc.S("StoreyFragmentNameEmpty");
            var row = new StoreyFragmentReportRow(Loc.S("StoreyFragmentReportError"), error, true);
            return new(null, [row], string.Format(Loc.S("StoreyFragmentFailed"), parent.Tag, F(request.Z1), F(request.Z2), 1));
        }
        var (schema, draft) = StoreyFragmentService.Commit(db, parent.Id, request, prepared);
        var rows = StoreyFragmentReportRows.From(draft.Diagnostics).ToList();
        // Полоса или призма захватили всю схему: фрагмент без разрезов — копия родителя, маски с него не снимаются.
        if (schema is not null && draft.Cuts.Count == 0)
            rows.Insert(0, new StoreyFragmentReportRow(Loc.S("StoreyFragmentReportWarning"), Loc.S("StoreyFragmentNoCuts"), false));
        string log = schema is null
            ? string.Format(Loc.S("StoreyFragmentFailed"), parent.Tag, F(request.Z1), F(request.Z2), rows.Count(r => r.IsError))
            : string.Format(Loc.S("StoreyFragmentBuilt"), schema.Tag, parent.Tag, F(request.Z1), F(request.Z2),
                draft.Members.Count, draft.LoadCases.Count, rows.Count);
        return new(schema, rows, log);
    }
}

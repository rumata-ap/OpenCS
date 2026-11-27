using System.Globalization;
using CScore.Fem;
using CScore.Fem.Fragment;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Ошибка поля окна фрагмента: имя поля (<see cref="FragmentAuthoring.FieldZ1"/> …) и текст для пользователя.</summary>
public sealed record FragmentFieldError(string Field, string Message);

/// <summary>
/// Фрагменты МКЭ-схемы — общая служба окон и CLI (аудит GUI + CLI, раздел «Фрагменты»): проверка полей запроса с привязкой
/// к полю, уникальное имя новой схемы, сборка без окон (<see cref="StoreyFragmentService"/>) и запись снятых масок.
/// Окна показывают ошибки у полей, CLI печатает их с путём в документе.
/// </summary>
public static partial class FragmentAuthoring
{
    public const string FieldZ1 = "z1", FieldZ2 = "z2", FieldOffset = "offset", FieldFloors = "floors", FieldName = "name";

    /// <summary>Ошибка имени новой схемы: пусто или уже есть у другой схемы проекта; null — годится.</summary>
    public static string? NameError(DatabaseService db, string? name, int selfId = 0)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(name)) return Loc.S("StoreyFragmentNameEmpty");
        return FemSchemaAuthoring.TagError(db, name, selfId);
    }

    /// <summary>Свободное имя: <paramref name="name"/>, если не занято, иначе «имя (2)», «имя (3)»…</summary>
    public static string FreeName(DatabaseService db, string name)
    {
        ArgumentNullException.ThrowIfNull(db);
        string t = (name ?? "").Trim();
        if (t.Length == 0 || FemSchemaAuthoring.TagError(db, t) == null) return t;
        for (int n = 2; ; n++)
        {
            string candidate = string.Format(Loc.S("StoreyFragmentNameNumbered"), t, n);
            // Строка ресурса без подстановок (нет перевода) — номер всё равно меняет имя, цикл конечен.
            if (!candidate.Contains(n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) candidate = $"{t} ({n})";
            if (FemSchemaAuthoring.TagError(db, candidate) == null) return candidate;
        }
    }

    /// <summary>
    /// Отметки z₁/z₂ (м, запятая или точка) по полям: не число — ошибка у своего поля; z₁ ≥ z₂ — у z₂. Без ошибок —
    /// <paramref name="z1"/>, <paramref name="z2"/>.
    /// </summary>
    public static List<FragmentFieldError> ValidateLevels(string? z1Text, string? z2Text, out double z1, out double z2)
    {
        var errors = new List<FragmentFieldError>();
        bool ok1 = TryNumber(z1Text, out z1), ok2 = TryNumber(z2Text, out z2);
        if (!ok1) errors.Add(new(FieldZ1, Loc.S("StoreyFragmentLevelInvalid")));
        if (!ok2) errors.Add(new(FieldZ2, Loc.S("StoreyFragmentLevelInvalid")));
        if (ok1 && ok2 && z1 >= z2) errors.Add(new(FieldZ2, Loc.S("StoreyFragmentLevelsOrder")));
        return errors;
    }

    /// <summary>Отступ d призмы, м: положительное число; null — годится.</summary>
    public static string? OffsetError(string? text, out double d) =>
        TryNumber(text, out d) && d > 0 ? null : Loc.S("ElementFragmentOffsetInvalid");

    /// <summary>Конечное число в текущей или инвариантной записи (запятая и точка).</summary>
    public static bool TryNumber(string? text, out double value) =>
        ParseAny((text ?? "").Trim(), out value) && double.IsFinite(value);

    /// <summary>Число с точкой или запятой (как <c>Pars.ParseAny</c> окон).</summary>
    static bool ParseAny(string text, out double result) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
        double.TryParse(text.Replace('.', ','), NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out result);

    /// <summary>
    /// Сборка фрагмента без окон (CLI): проверка имени, снимок полосы и построитель, запись одной транзакцией. Имя занято
    /// или построитель отказал — схема не создаётся (<see cref="StoreyFragmentFlowResult.IsError"/>).
    /// </summary>
    public static StoreyFragmentFlowResult Build(DatabaseService db, FemSchema parent, StoreyFragmentRequest request) =>
        StoreyFragmentMenuFlow.Finish(db, parent, request, NameRefusal(db, request) is not null
            ? null : StoreyFragmentService.Prepare(db, parent.Id, request));

    /// <summary>
    /// Запись черновика, подготовленного <see cref="StoreyFragmentService.Prepare"/> заранее (CLI: сборка — на этапе
    /// проверки документа, запись — потом), с проверкой имени; отчёт и строка журнала — как у окна.
    /// </summary>
    public static StoreyFragmentFlowResult Commit(DatabaseService db, FemSchema parent, StoreyFragmentRequest request,
        StoreyFragmentPrepared prepared) =>
        StoreyFragmentMenuFlow.Finish(db, parent, request, NameRefusal(db, request) is not null ? null : prepared);

    /// <summary>Отказ по имени запроса (пусто или занято) — строка отчёта; null — имя годится.</summary>
    internal static string? NameRefusal(DatabaseService db, StoreyFragmentRequest request) => NameError(db, request.Name);

    /// <summary>
    /// Записать набор масок, снятый <see cref="StoreyFragmentMaskFlow.Extract"/> без записи (в потоке соединения БД);
    /// отказ снятия — прежние маски не трогаются.
    /// </summary>
    public static void SaveMasks(DatabaseService db, FemSchema fragment, StoreyFragmentMaskFlowResult result)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsError && result.Set is { } set) db.ReplaceFragmentMasks(fragment.Id, set);
    }
}

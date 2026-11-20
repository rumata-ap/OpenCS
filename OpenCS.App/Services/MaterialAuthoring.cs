using CScore;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Ошибка ввода: путь поля (как в документе opencs-model) и текст для пользователя.</summary>
public sealed record FieldError(string Path, string Message);

/// <summary>
/// Создание и правка материала проекта: одна проверка и одно сохранение для страницы материала и консоли
/// (<c>opencs apply</c>). Пишет в базу только проверенный материал.
/// </summary>
public static class MaterialAuthoring
{
    static readonly CalcType[] Sets = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    public static IReadOnlyList<FieldError> Validate(Material m)
    {
        var errors = new List<FieldError>();
        if (m.Type == MatType.None) errors.Add(new("type", Loc.S("MaterialErrTypeNotSet")));
        if (string.IsNullOrWhiteSpace(m.Tag)) errors.Add(new("tag", Loc.S("MaterialErrTagEmpty")));
        if (m.Type is MatType.None or MatType.Custom) return errors;
        foreach (var ct in Sets)
        {
            string path = $"chars.{ct}";
            var ch = m.GetChars(ct);
            if (ch == null)
            {
                errors.Add(new(path, string.Format(Loc.S("MaterialErrCharsMissing"), ct)));
                continue;
            }
            if (!(double.IsFinite(ch.E) && ch.E > 0)) errors.Add(new($"{path}.e", Loc.S("MaterialErrEPositive")));
            if (m.Type == MatType.Concrete && !(ch.Fc < 0)) errors.Add(new($"{path}.fc", Loc.S("MaterialErrConcreteFc")));
        }
        return errors;
    }

    /// <summary>
    /// Согласует производные поля перед записью: у арматуры Rsc = |Fc| — на странице материала правится прочность
    /// на сжатие (Fc), а НДМ и формульные проверки читают Rsc (<see cref="MaterialChars.GetRscOrLegacyFc"/>).
    /// В справочнике арматуры Rsc = −Fc во всех строках, так что материал из справочника не меняется.
    /// </summary>
    public static void Normalize(Material m)
    {
        if (m.Type is not (MatType.ReSteelF or MatType.ReSteelU)) return;
        foreach (var ct in Sets)
            if (m.GetChars(ct) is { Fc: < 0 } ch) ch.Rsc = -ch.Fc;
    }

    /// <summary>Проверяет и сохраняет (новый — добавляет в проект). Ошибки — ничего не записано.</summary>
    public static IReadOnlyList<FieldError> Save(DatabaseService db, Material m)
    {
        var errors = Validate(m);
        if (errors.Count > 0) return errors;
        Normalize(m);
        m.SetJson();
        if (m.Id == 0) db.AddMaterial(m);
        else db.SaveMaterial(m);
        return [];
    }
}

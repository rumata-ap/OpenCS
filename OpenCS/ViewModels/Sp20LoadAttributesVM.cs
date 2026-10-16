using System.Globalization;
using CScore.Combinations;
using CScore.Fem;
using CScore.Fem.Combinations;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Атрибуты нагрузки СП 20 загружения (спека 2026-10-14, п. 5.2.1): вид, γf, γf при уменьшении веса (п. 7.3, только у
/// постоянных), группа взаимоисключения и доля длительной части (только у кратковременных, с выбором вида нагрузки из
/// <see cref="Sp20LongTermShares"/>). Пустое γf — значение по умолчанию из настроек (подсказка в поле).
/// </summary>
public sealed class Sp20LoadAttributesVM : ViewModelBase
{
    readonly Sp20GammaDefaults _defaults;

    public Sp20LoadAttributesVM(FemLoadCase loadCase, Sp20GammaDefaults defaults)
    {
        _defaults = defaults;
        _kind = FemLoadsTabVM.Sp20Types.Any(t => t.Key == loadCase.Sp20Type) ? loadCase.Sp20Type : "short_term";
        _group = loadCase.Sp20Group ?? "";
        _gammaText = Format(loadCase.GammaFUnfav);
        _reducedGammaText = Format(loadCase.GammaFFav);
        _shareText = Format(loadCase.LongTermShare);
        _shareKind = ShareKinds.FirstOrDefault(k => k.Share is { } v && loadCase.LongTermShare is { } s && Math.Abs(v - s) < 1e-9)?.Key
                     ?? (loadCase.LongTermShare == null ? null : "other");
    }

    /// <summary>Виды нагрузок для доли длительной части: подпись с основанием и типовая доля.</summary>
    public static IReadOnlyList<ShareKind> ShareKinds { get; } = Sp20LongTermShares.All
        .Select(e => new ShareKind(e.Key, e.Share is { } v
            ? $"{e.Title} — {v.ToString("0.##", CultureInfo.CurrentCulture)} ({e.Clause})"
            : $"{e.Title} ({e.Clause})", e.Share)).ToList();

    public sealed record ShareKind(string Key, string Label, double? Share);

    static string Format(double? value) => value?.ToString("0.####", CultureInfo.CurrentCulture) ?? "";

    string _kind;
    /// <summary>Вид нагрузки: permanent | long_term | short_term | accidental.</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (value == null || value == _kind) return;
            _kind = value;
            OnPropertyChanged();
            foreach (var name in new[] { nameof(IsPermanent), nameof(IsShortTerm), nameof(GammaHint), nameof(ReducedGammaHint) })
                OnPropertyChanged(name);
        }
    }

    public bool IsPermanent => _kind == "permanent";
    public bool IsShortTerm => _kind == "short_term";

    string _group;
    public string Group { get => _group; set { _group = value ?? ""; OnPropertyChanged(); } }

    string _gammaText;
    /// <summary>γf; пусто — по умолчанию для вида (см. <see cref="GammaHint"/>).</summary>
    public string GammaText { get => _gammaText; set { _gammaText = value ?? ""; OnPropertyChanged(); } }

    string _reducedGammaText;
    /// <summary>γf при уменьшении веса (п. 7.3); только у постоянных, пусто — по умолчанию.</summary>
    public string ReducedGammaText { get => _reducedGammaText; set { _reducedGammaText = value ?? ""; OnPropertyChanged(); } }

    /// <summary>γf по умолчанию для вида — подсказка в пустом поле.</summary>
    public string GammaHint => string.Format(Loc.S("Sp20AttrDefaultHint"),
        FemCombinationCoefficients.GammaF(new FemLoadCase { Sp20Type = _kind }, _defaults).ToString("0.##", CultureInfo.CurrentCulture));

    public string ReducedGammaHint => string.Format(Loc.S("Sp20AttrDefaultHint"),
        _defaults.PermanentFav.ToString("0.##", CultureInfo.CurrentCulture));

    string? _shareKind;
    /// <summary>Вид нагрузки для доли длительной части: выбор подставляет типовую долю (поле остаётся редактируемым).</summary>
    public string? ShareKindKey
    {
        get => _shareKind;
        set
        {
            if (value == _shareKind) return;
            _shareKind = value;
            OnPropertyChanged();
            if (ShareKinds.FirstOrDefault(k => k.Key == value)?.Share is { } share) ShareText = Format(share);
        }
    }

    string _shareText;
    /// <summary>Доля длительной части 0…1; пусто — не задана (в CL/NL — 0).</summary>
    public string ShareText { get => _shareText; set { _shareText = value ?? ""; OnPropertyChanged(); } }

    /// <summary>
    /// Переносит атрибуты в значения загружения (имя, собственный вес, ψ — не трогаются); текст ошибки или null.
    /// γf п. 7.3 у непостоянных и доля у непостоянных-некратковременных сохраняются как были (в расчёт не идут).
    /// </summary>
    public string? ApplyTo(FemLoadCase values)
    {
        if (!TryOptional(GammaText, false, out var gamma) || gamma == 0) return Loc.S("FemLoadCaseDialogBadNumber");
        if (!TryOptional(ReducedGammaText, false, out var reduced)) return Loc.S("FemLoadCaseDialogBadNumber");
        if (!TryOptional(ShareText, true, out var share)) return Loc.S("Sp20AttrBadShare");
        values.Sp20Type = _kind;
        values.Sp20Group = string.IsNullOrWhiteSpace(_group) ? null : _group.Trim();
        values.GammaFUnfav = gamma;
        if (IsPermanent) values.GammaFFav = reduced;
        if (IsShortTerm) values.LongTermShare = share;
        return null;
    }

    /// <summary>Пусто — null; иначе неотрицательное число (доля — не больше 1).</summary>
    static bool TryOptional(string text, bool share, out double? value)
    {
        value = null;
        text = text.Trim();
        if (text.Length == 0) return true;
        if (!Pars.ParseAny(text, out double v) || !double.IsFinite(v) || v < 0 || share && v > 1) return false;
        value = v;
        return true;
    }
}

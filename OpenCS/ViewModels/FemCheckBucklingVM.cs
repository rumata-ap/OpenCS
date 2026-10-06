using System.Globalization;
using System.Windows;
using CScore.Fem;
using CScore.Sp16;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>
/// Продольный изгиб в проверке по КЭ — общий блок диалога «Нормативная проверка» и страницы группы:
/// у ЖБ (стержни, стены) — коэффициент η (<see cref="FemEtaParams"/>), у стали — расчётные длины
/// lef = μ·l по сетке (<see cref="SteelFemCheckParams"/>). Хранятся в параметрах проверки
/// (<see cref="FemCheck.ParamsJson"/>); <see cref="Apply"/> меняет только их, прочие параметры проверки сохраняет.
/// </summary>
public sealed class FemCheckBucklingVM : ViewModelBase
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    string? _normCode;
    bool _isPlate;

    /// <summary>Вид проверки, для которого показывается блок: <c>steel_check</c>, <c>rc_check</c>, <c>rc_plate_check</c>.</summary>
    public string? NormCode
    {
        get => _normCode;
        set
        {
            _normCode = value;
            _isPlate = value == "rc_plate_check";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Visibility));
            OnPropertyChanged(nameof(EtaVisibility));
            OnPropertyChanged(nameof(EtaYVisibility));
            OnPropertyChanged(nameof(SteelVisibility));
            OnPropertyChanged(nameof(Header));
        }
    }

    bool IsSteel => _normCode == "steel_check";
    bool IsBarRc => _normCode == "rc_check";

    /// <summary>Блок применим к виду проверки.</summary>
    public bool Applies => IsSteel || IsBarRc || _isPlate;

    public Visibility Visibility => Applies ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EtaVisibility => IsBarRc || _isPlate ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>μy, ψy — только у стержней: у стен одна плоскость (вертикальная полоса).</summary>
    public Visibility EtaYVisibility => IsBarRc ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SteelVisibility => IsSteel ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Подпись блока.</summary>
    public string Header => Loc.S(IsSteel ? "FemCheckDlgSteelLef" : "FemCheckDlgEta");

    // ── η (п. 8.1.15 СП 63) ──

    bool _etaEnabled, _etaIterative, _etaPsiAuto = true;
    string _etaLength = "", _etaMuX = "1", _etaMuY = "1", _etaPsiX = "1", _etaPsiY = "1";

    public bool EtaEnabled { get => _etaEnabled; set { _etaEnabled = value; OnPropertyChanged(); } }
    public bool EtaIterative { get => _etaIterative; set { _etaIterative = value; OnPropertyChanged(); } }
    /// <summary>Длина l, м; пусто — по сетке схемы для каждого КЭ.</summary>
    public string EtaLength { get => _etaLength; set { _etaLength = value; OnPropertyChanged(); } }
    public string EtaMuX { get => _etaMuX; set { _etaMuX = value; OnPropertyChanged(); } }
    public string EtaMuY { get => _etaMuY; set { _etaMuY = value; OnPropertyChanged(); } }
    public bool EtaPsiAuto { get => _etaPsiAuto; set { _etaPsiAuto = value; OnPropertyChanged(); } }
    public string EtaPsiX { get => _etaPsiX; set { _etaPsiX = value; OnPropertyChanged(); } }
    public string EtaPsiY { get => _etaPsiY; set { _etaPsiY = value; OnPropertyChanged(); } }

    // ── Расчётные длины стали по сетке ──

    bool _steelMeshLef;
    string _steelMuX = "1", _steelMuY = "1", _steelMuB = "0";

    public bool SteelMeshLef { get => _steelMeshLef; set { _steelMeshLef = value; OnPropertyChanged(); } }
    public string SteelMuX { get => _steelMuX; set { _steelMuX = value; OnPropertyChanged(); } }
    public string SteelMuY { get => _steelMuY; set { _steelMuY = value; OnPropertyChanged(); } }
    public string SteelMuB { get => _steelMuB; set { _steelMuB = value; OnPropertyChanged(); } }

    // ── Загрузка / сборка ──

    static string F(double v) => v.ToString("G", Inv);

    static double? Num(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, Inv, out var v) && double.IsFinite(v) ? v : null;

    /// <summary>Заполняет блок из параметров проверки (вид — <see cref="FemCheck.NormCode"/>).</summary>
    public void Load(FemCheck check)
    {
        NormCode = check.NormCode;
        LoadEta(check.NormCode switch
        {
            "rc_check" => BarCheckParams.Parse(check.ParamsJson).Eta,
            "rc_plate_check" => PlateCheckParams.Parse(check.ParamsJson).Eta,
            _ => null,
        });
        LoadSteel(check.NormCode == "steel_check" ? SteelFemCheckParams.TryParse(check.ParamsJson)?.MeshLef : null);
    }

    void LoadEta(FemEtaParams? eta)
    {
        var e = eta ?? new FemEtaParams();
        EtaEnabled   = e.Enabled;
        EtaIterative = e.Iterative;
        EtaLength    = e.HasManualLength ? F(e.LengthM!.Value) : "";
        EtaMuX       = F(e.MuX);
        EtaMuY       = F(e.MuY);
        EtaPsiAuto   = e.IsPsiAuto;
        EtaPsiX      = F(e.PsiX);
        EtaPsiY      = F(e.PsiY);
    }

    void LoadSteel(SteelMeshLef? mesh)
    {
        var m = mesh ?? new SteelMeshLef();
        SteelMeshLef = mesh != null;
        SteelMuX = F(m.MuX);
        SteelMuY = F(m.MuY);
        SteelMuB = F(m.MuB);
    }

    /// <summary>Параметры η из полей; null — η не учитывается.</summary>
    public FemEtaParams? BuildEta() => !EtaEnabled ? null : new FemEtaParams
    {
        Enabled   = true,
        Iterative = EtaIterative,
        LengthM   = Num(EtaLength) is > 0 and double l ? l : null,
        MuX       = Num(EtaMuX) is > 0 and double mx ? mx : 1.0,
        MuY       = Num(EtaMuY) is > 0 and double my ? my : 1.0,
        PsiMode   = EtaPsiAuto ? FemEtaParams.PsiAuto : FemEtaParams.PsiManual,
        PsiX      = Num(EtaPsiX) is >= 0 and double px ? px : 1.0,
        PsiY      = Num(EtaPsiY) is >= 0 and double py ? py : 1.0,
    };

    /// <summary>Параметры стальной проверки из полей.</summary>
    public SteelFemCheckParams BuildSteel() => new()
    {
        MeshLef = !SteelMeshLef ? null : new SteelMeshLef
        {
            MuX = Num(SteelMuX) is > 0 and double mx ? mx : 1.0,
            MuY = Num(SteelMuY) is > 0 and double my ? my : 1.0,
            MuB = Num(SteelMuB) is >= 0 and double mb ? mb : 0.0,
        },
    };

    /// <summary>Записывает блок в параметры проверки, сохраняя прочие её параметры.</summary>
    public void Apply(FemCheck check)
    {
        switch (check.NormCode)
        {
            case "rc_check":
                check.ParamsJson = (BarCheckParams.Parse(check.ParamsJson) with { Eta = BuildEta() }).ToJson();
                break;
            case "rc_plate_check":
                check.ParamsJson = (PlateCheckParams.Parse(check.ParamsJson) with { Eta = BuildEta() }).ToJson();
                break;
            case "steel_check":
                check.ParamsJson = BuildSteel().ToJson();
                break;
        }
    }
}

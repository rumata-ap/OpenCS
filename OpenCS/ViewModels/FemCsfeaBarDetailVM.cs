using System.Globalization;
using CScore.Fem;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Величина детальной эпюры стержня CSfea.</summary>
public enum FemCsfeaBarDetailQuantity { EpsB, SigmaB, EpsS, SigmaS, Height, Psi }

/// <summary>Пункт списка величин детальной эпюры.</summary>
public sealed record FemCsfeaBarDetailQuantityOption(FemCsfeaBarDetailQuantity Quantity, string Label);

/// <summary>Строка таблицы детальной эпюры: точка КЭ, её величина и источник (запись или досчёт).</summary>
public sealed record FemCsfeaBarDetailRow(int ElemNum, string Point, string S, string Mx, string My, string Value, string Source, string Crack);

/// <summary>Маркер точки Лобатто (щелчок — окно «НДС сечения») или точки, где досчёт не сошёлся.</summary>
public sealed record FemCsfeaBarDetailMarker(double S, int ElemNum, int? LobattoPoint, string Label, bool Failed);

/// <summary>
/// Детальные эпюры вдоль выбранных стержней результата CSfea (спека НДС сечений, п. 2.4): наибольшие εb, σb, εs, σs,
/// высота сжатой зоны и ψs в n точках каждого КЭ цепочки. Точки КЭ считает <paramref name="compute"/> (кешируются по
/// числу точек).
/// </summary>
public sealed class FemCsfeaBarDetailVM : ViewModelBase
{
    readonly Func<int, int, IReadOnlyList<FemCsfeaBarDetailPoint>?> _compute;
    readonly Dictionary<(int Elem, int Points), IReadOnlyList<FemCsfeaBarDetailPoint>?> _cache = [];

    /// <summary>Эпюра или её настройки изменились.</summary>
    public event EventHandler? Changed;

    /// <param name="chains">Цепочки выбранных стержней.</param>
    /// <param name="compute">Точки КЭ (номер, число точек); null — КЭ не считается (нет сечения CScore).</param>
    /// <param name="psi">Постановка с ψs — есть величина ψs.</param>
    public FemCsfeaBarDetailVM(IReadOnlyList<BarChain> chains, Func<int, int, IReadOnlyList<FemCsfeaBarDetailPoint>?> compute, bool psi)
    {
        _compute = compute;
        Chains = chains.Select(c => new FemBarDiagramChainOption(c, ChainLabel(c))).ToList();
        Quantities = Enum.GetValues<FemCsfeaBarDetailQuantity>()
            .Where(q => psi || q != FemCsfeaBarDetailQuantity.Psi)
            .Select(q => new FemCsfeaBarDetailQuantityOption(q, Loc.S("FemCsfeaDetail" + q)))
            .ToList();
        _selectedChain = Chains.FirstOrDefault();
        _selectedQuantity = Quantities.First(q => q.Quantity == FemCsfeaBarDetailQuantity.SigmaS);
        Rebuild();
    }

    public IReadOnlyList<FemBarDiagramChainOption> Chains { get; }
    public IReadOnlyList<FemCsfeaBarDetailQuantityOption> Quantities { get; }
    /// <summary>Число точек на КЭ.</summary>
    public IReadOnlyList<int> PointCounts { get; } = [5, 11, 21, 41];
    public bool HasSeveralChains => Chains.Count > 1;

    FemBarDiagramChainOption? _selectedChain;
    public FemBarDiagramChainOption? SelectedChain
    {
        get => _selectedChain;
        set { if (Equals(_selectedChain, value)) return; _selectedChain = value; OnPropertyChanged(); Rebuild(); }
    }

    FemCsfeaBarDetailQuantityOption _selectedQuantity;
    public FemCsfeaBarDetailQuantityOption SelectedQuantity
    {
        get => _selectedQuantity;
        set { if (value == null || Equals(_selectedQuantity, value)) return; _selectedQuantity = value; OnPropertyChanged(); Rebuild(); }
    }

    int _points = FemCsfeaBarDetail.DefaultPoints;
    public int Points
    {
        get => _points;
        set { if (_points == value) return; _points = value; OnPropertyChanged(); Rebuild(); }
    }

    /// <summary>Участки эпюры (между соседними точками КЭ; на стыке КЭ — разрыв).</summary>
    public IReadOnlyList<BarDiagramSegment> Segments { get; private set; } = [];
    public IReadOnlyList<FemCsfeaBarDetailMarker> Markers { get; private set; } = [];
    public IReadOnlyList<FemCsfeaBarDetailRow> Rows { get; private set; } = [];
    public string Title { get; private set; } = "";

    /// <summary>Значение величины в точке (единицы подписи: ‰, МПа, мм); null — нет состояния.</summary>
    public static double? Value(FemCsfeaBarDetailPoint p, FemCsfeaBarDetailQuantity q) => p.State is not { } s ? null : q switch
    {
        FemCsfeaBarDetailQuantity.EpsB => s.EpsB * 1e3,
        FemCsfeaBarDetailQuantity.SigmaB => s.SigmaB,
        FemCsfeaBarDetailQuantity.EpsS => s.EpsS * 1e3,
        FemCsfeaBarDetailQuantity.SigmaS => s.SigmaS,
        FemCsfeaBarDetailQuantity.Height => s.CompressedHeight * 1e3,
        _ => s.PsiMin,
    };

    IReadOnlyList<FemCsfeaBarDetailPoint>? PointsOf(int elem)
    {
        if (!_cache.TryGetValue((elem, _points), out var points))
            _cache[(elem, _points)] = points = _compute(elem, _points);
        return points;
    }

    void Rebuild()
    {
        var q = _selectedQuantity.Quantity;
        var segments = new List<BarDiagramSegment>();
        var markers = new List<FemCsfeaBarDetailMarker>();
        var rows = new List<(double S, FemCsfeaBarDetailRow Row)>();
        foreach (var e in SelectedChain?.Chain.Elements ?? [])
        {
            if (PointsOf(e.ElemNum) is not { } points) continue;
            for (int k = 0; k < points.Count; k++)
            {
                var p = points[k];
                double s = e.At(p.T);
                if (k + 1 < points.Count && Value(p, q) is double v0 && Value(points[k + 1], q) is double v1)
                {
                    double s1 = e.At(points[k + 1].T);
                    segments.Add(s <= s1 ? new BarDiagramSegment(s, s1, v0, v1) : new BarDiagramSegment(s1, s, v1, v0));
                }
                string point = p.LobattoPoint switch { 0 => "i", 1 => "½", 2 => "j", _ => p.T.ToString("0.###", CultureInfo.CurrentCulture) };
                if (p.Recorded)
                    markers.Add(new FemCsfeaBarDetailMarker(s, e.ElemNum, p.LobattoPoint,
                        string.Format(Loc.S("FemCsfeaDetailMarkerRecorded"), e.ElemNum, point), Failed: false));
                else if (p.State == null)
                    markers.Add(new FemCsfeaBarDetailMarker(s, e.ElemNum, null,
                        string.Format(Loc.S("FemCsfeaDetailMarkerFailed"), e.ElemNum, point), Failed: true));
                rows.Add((s, new FemCsfeaBarDetailRow(e.ElemNum, point, s.ToString("0.###", CultureInfo.CurrentCulture),
                    p.Mx.ToString("0.###", CultureInfo.CurrentCulture), p.My.ToString("0.###", CultureInfo.CurrentCulture),
                    Value(p, q)?.ToString(Format(q), CultureInfo.CurrentCulture) ?? "",
                    Loc.S(p.Recorded ? "FemCsfeaDetailSourceRecorded" : p.State == null ? "FemCsfeaDetailSourceFailed" : "FemCsfeaDetailSourceSolved"),
                    p.State is not { } st ? "" : Loc.S(st.CrackFromFlag ? "FemCsfeaDetailCrackFlag" : st.Cracked ? "FemCsfeaDetailCrackYes" : "FemCsfeaDetailCrackNo"))));
            }
        }
        Segments = segments.OrderBy(x => x.S0).ToList();
        Markers = markers;
        Rows = rows.OrderBy(r => r.S).Select(r => r.Row).ToList();
        Title = _selectedQuantity.Label;
        OnPropertyChanged(nameof(Segments));
        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Title));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    static string Format(FemCsfeaBarDetailQuantity q) => q switch
    {
        FemCsfeaBarDetailQuantity.EpsB or FemCsfeaBarDetailQuantity.EpsS => "0.000",
        FemCsfeaBarDetailQuantity.Psi => "0.###",
        FemCsfeaBarDetailQuantity.Height => "0",
        _ => "0.0",
    };

    static string ChainLabel(BarChain chain)
    {
        string length = chain.Length.ToString("0.##", CultureInfo.CurrentCulture);
        return chain.Elements.Count == 1
            ? string.Format(Loc.S("FemBarDiagramChainOne"), chain.Elements[0].ElemNum, length)
            : string.Format(Loc.S("FemBarDiagramChainMany"), chain.Elements[0].ElemNum, chain.Elements[^1].ElemNum,
                chain.Elements.Count, length);
    }
}

namespace CScore.PlateStrip;

/// <summary>
/// Постоянный линейный источник плитного отклика. Используется доменными тестами и
/// будущими программными источниками, но не предлагается пользователю в UI.
/// </summary>
public sealed class ConstantLinearPlateSectionResponse : IPlateSectionResponse
{
    readonly double[,] _a;
    readonly double[,] _b;
    readonly double[,] _d;
    readonly double[,] _as;
    readonly double[,] _h;

    public ConstantLinearPlateSectionResponse(
        double[,] a, double[,] b, double[,] d, double[,] ass, string fingerprint)
    {
        PlateSectionResponseMath.ValidateBlock(a, 3, 3, nameof(a));
        PlateSectionResponseMath.ValidateBlock(b, 3, 3, nameof(b));
        PlateSectionResponseMath.ValidateBlock(d, 3, 3, nameof(d));
        PlateSectionResponseMath.ValidateBlock(ass, 2, 2, nameof(ass));
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Отпечаток источника не должен быть пустым.", nameof(fingerprint));

        _a = PlateSectionResponseMath.Clone(a);
        _b = PlateSectionResponseMath.Clone(b);
        _d = PlateSectionResponseMath.Clone(d);
        _as = PlateSectionResponseMath.Clone(ass);
        _h = PlateSectionResponseMath.BuildH(_a, _b, _d);
        Fingerprint = fingerprint;
    }

    /// <summary>Вид источника.</summary>
    public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.ConstantLinear;

    /// <summary>Отпечаток подготовленных блоков.</summary>
    public string Fingerprint { get; }

    public PlateResultants Forces(ShellStrainState state) =>
        PlateSectionResponseMath.ToResultants(PlateSectionResponseMath.MatVec(_h, state.ToArray()));

    public PlateShellTangentResult Tangent(ShellStrainState state) =>
        PlateSectionResponseMath.BuildTangent(_a, _b, _d, _as, Forces(state));

    internal double[,] Matrix => PlateSectionResponseMath.Clone(_h);
}

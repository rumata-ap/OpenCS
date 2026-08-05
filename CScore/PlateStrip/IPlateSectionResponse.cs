namespace CScore.PlateStrip;

/// <summary>
/// Доменный контракт отклика плитного сечения. Намеренно параллелен
/// CSfea.Core.IShellSectionResponse, но не создаёт зависимости CScore от CSfea.
/// </summary>
public interface IPlateSectionResponse
{
    /// <summary>Вид источника отклика.</summary>
    EquivalentSectionSourceKind SourceKind { get; }

    /// <summary>Шесть результирующих усилий для заданного состояния.</summary>
    PlateResultants Forces(ShellStrainState state);

    /// <summary>Полная касательная матрица мембранно-изгибного отклика.</summary>
    PlateShellTangentResult Tangent(ShellStrainState state);

    /// <summary>Детерминированный отпечаток источника.</summary>
    string Fingerprint { get; }
}

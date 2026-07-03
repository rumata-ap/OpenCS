namespace CSfea.Core;

/// <summary>Результат одного шага нелинейного CR-анализа рамы.
/// Порт <c>beam_corotational.py: NonlinearStepRecord</c>.</summary>
public sealed record NonlinearStepRecord(
    double Lam,
    double[] U,
    int NIter,
    bool Converged,
    IReadOnlyList<double> Residuals);

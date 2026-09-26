namespace CScore.Sp16;

public static partial class Sp16Tables
{
    // ── Табл. 12: ccr для σcr по (81) при σloc = 0 ──

    static readonly double[] T12Delta = [0.8, 1.0, 2.0, 4.0, 6.0, 10.0, 30.0];
    static readonly double[] T12Ccr = [30.0, 31.5, 33.3, 34.6, 34.8, 35.1, 35.5];

    /// <summary>ccr по табл. 12: сварные поясные соединения — по δ (84), фрикционные — 35,2.</summary>
    public static double Table12Ccr(double delta, bool friction) => friction ? 35.2 : Interp(delta, T12Delta, T12Ccr);

    // ── Табл. 17: ccr для (85) и табл. 22 (127) ──

    static readonly double[] T17Alpha = [1.0, 1.2, 1.4, 1.6, 1.8, 2.0];
    static readonly double[] T17Ccr = [10.2, 12.7, 15.5, 20.0, 25.0, 30.0];

    /// <summary>ccr по табл. 17 в зависимости от α = (σ1 − σ2)/σ1 (1 ≤ α ≤ 2, вне — по краю).</summary>
    public static double Table17Ccr(double alpha) => Interp(alpha, T17Alpha, T17Ccr);

    // ── Табл. 14: c1 для σloc,cr по (82) ──

    static readonly double[] T14Rho = [0.10, 0.15, 0.20, 0.25, 0.30, 0.35, 0.40];
    static readonly double[] T14A = [0.50, 0.60, 0.67, 0.80, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0];
    static readonly double[,] T14 =
    {
        { 56.7, 46.6, 41.8, 34.9, 28.5, 24.5, 21.7, 19.5, 17.7, 16.2 },
        { 38.9, 31.3, 27.9, 23.0, 18.6, 16.2, 14.6, 13.6, 12.7, 12.0 },
        { 33.9, 26.7, 23.5, 19.2, 15.4, 13.3, 12.1, 11.3, 10.7, 10.2 },
        { 30.6, 24.9, 20.3, 16.2, 12.9, 11.1, 10.0,  9.4,  9.0,  8.7 },
        { 28.9, 21.6, 18.5, 14.5, 11.3,  9.6,  8.7,  8.1,  7.8,  7.6 },
        { 28.0, 20.6, 17.4, 13.4, 10.2,  8.6,  7.7,  7.2,  6.9,  6.7 },
        { 27.4, 20.0, 16.8, 12.7,  9.5,  7.9,  7.0,  6.6,  6.3,  6.1 },
    };

    /// <summary>c1 по табл. 14 в зависимости от ρ = 1,04·lef/hef и a/hef (вне диапазона — крайние значения).</summary>
    public static double Table14C1(double rho, double aToHef) => Interp2(rho, aToHef, T14Rho, T14A, T14);

    // ── Табл. 15: c2 для σloc,cr по (82) ──

    static readonly double[] T15Delta = [1, 2, 4, 6, 10, 30];
    static readonly double[] T15A = [0.50, 0.60, 0.67, 0.80, 1.00, 1.20, 1.40, 1.60];
    static readonly double[,] T15 =
    {
        { 1.56, 1.56, 1.56, 1.56, 1.56, 1.56, 1.56, 1.56 },
        { 1.64, 1.64, 1.64, 1.67, 1.76, 1.82, 1.84, 1.85 },
        { 1.66, 1.67, 1.69, 1.75, 1.88, 2.01, 2.09, 2.12 },
        { 1.67, 1.68, 1.70, 1.77, 1.92, 2.08, 2.19, 2.26 },
        { 1.68, 1.69, 1.71, 1.78, 1.96, 2.14, 2.28, 2.38 },
        { 1.68, 1.70, 1.72, 1.80, 1.99, 2.20, 2.38, 2.52 },
    };

    /// <summary>c2 по табл. 15 в зависимости от δ (84) и a/hef (вне диапазона — крайние значения).</summary>
    public static double Table15C2(double delta, double aToHef) => Interp2(delta, aToHef, T15Delta, T15A, T15);

    // ── Табл. 16: ccr при σloc ≠ 0 и a/hef > 0,8 ──

    static readonly double[] T16A = [0.8, 0.9, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0];

    /// <summary>
    /// ccr по табл. 16; при a/hef ≤ 0,8 — по табл. 12 (значение табл. 12 служит и левым узлом
    /// интерполяции на участке 0,8…0,9).
    /// </summary>
    public static double Table16Ccr(double aToHef, double table12Ccr) =>
        Interp(aToHef, T16A, [table12Ccr, 37.0, 39.2, 45.2, 52.8, 62.0, 72.6, 84.7]);

    // ── Табл. 18: α для (86) ──

    static readonly double[] T18Tau = [0, 0.5, 0.6, 0.7, 0.8, 0.9];
    static readonly double[] T18Lw = [2.2, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5];
    static readonly double[,] T18 =
    {
        { 0.240, 0.239, 0.235, 0.226, 0.213, 0.195, 0.173, 0.153 },
        { 0.203, 0.202, 0.197, 0.189, 0.176, 0.158, 0.136, 0.116 },
        { 0.186, 0.185, 0.181, 0.172, 0.159, 0.141, 0.119, 0.099 },
        { 0.167, 0.166, 0.162, 0.152, 0.140, 0.122, 0.100, 0.080 },
        { 0.144, 0.143, 0.139, 0.130, 0.117, 0.099, 0.077, 0.057 },
        { 0.119, 0.118, 0.114, 0.105, 0.092, 0.074, 0.052, 0.032 },
    };

    /// <summary>α по табл. 18 в зависимости от τ/Rs и λ̄w (вне диапазона — крайние значения).</summary>
    public static double Table18Alpha(double tauToRs, double lambdaW) => Interp2(tauToRs, lambdaW, T18Tau, T18Lw, T18);
}

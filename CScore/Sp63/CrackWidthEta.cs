using System;

namespace CScore.Sp63;

/// <summary>
/// Обвязка η (п. 8.1.15) для задач ширины раскрытия трещин:
/// авто-ψ = |M_long/M_total| и одинаковый масштаб long/total после RodEtaWiring.
/// </summary>
public static class CrackWidthEta
{
    public const double MomentEpsilon = 1e-9;

    public static double AutoPsi(double mLong, double mTotal)
    {
        if (Math.Abs(mTotal) < MomentEpsilon)
            return 1.0;
        double r = Math.Abs(mLong / mTotal);
        return Math.Clamp(r, 0.0, 1.0);
    }

    public readonly record struct ScaledMoments(
        double MxLongEff, double MxTotalEff, double MyLongEff, double MyTotalEff);

    public static ScaledMoments ScaleLongTotal(
        double mxLong, double mxTotal, double myLong, double myTotal,
        double mxTotalEff, double myTotalEff)
    {
        double sx = Math.Abs(mxTotal) < MomentEpsilon ? 1.0 : mxTotalEff / mxTotal;
        double sy = Math.Abs(myTotal) < MomentEpsilon ? 1.0 : myTotalEff / myTotal;
        return new ScaledMoments(
            MxLongEff: mxLong * sx,
            MxTotalEff: mxTotalEff,
            MyLongEff: myLong * sy,
            MyTotalEff: myTotalEff);
    }
}

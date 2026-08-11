using CScore;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public class RvePatchKinematicsTests
{
    [Fact]
    public void SquareContourUV_ReturnsCcwSquareAroundCenter()
    {
        var contour = RvePatchKinematics.SquareContourUV(centerU: 2.0, centerV: 5.0, sizeM: 1.0);

        Assert.Equal(4, contour.Length);
        Assert.All(contour, p => Assert.True(System.Math.Abs(p.U - 2.0) <= 0.5001 && System.Math.Abs(p.V - 5.0) <= 0.5001));
        // CCW: площадь по шнурку положительна.
        double area2 = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % 4];
            area2 += a.U * b.V - b.U * a.V;
        }
        Assert.True(area2 > 0, "Контур должен быть ориентирован против часовой стрелки.");
    }

    [Theory]
    [InlineData(0.7, 0.0, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.7, 0.0, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.7, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.0, 0.7, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.7, 0.0)]
    [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, 0.7)]
    public void NodeField_ReproducesInputCurvaturesAndZeroTransverseShear(
        double eps0x, double eps0y, double gamma0xy, double kx, double ky, double kxy)
    {
        var state = new ShellStrainState(eps0x, eps0y, gamma0xy, kx, ky, kxy);
        const double h = 1e-6;

        RvePatchNodeState At(double x, double y) =>
            RvePatchKinematics.NodeField(state, centerU: 0.0, centerV: 0.0, nodeU: x, nodeV: y);

        // Численные производные в произвольной точке (0.3, -0.2), не только в центре.
        double x0 = 0.3, y0 = -0.2;
        double dThetaYdX = (At(x0 + h, y0).ThetaY - At(x0 - h, y0).ThetaY) / (2 * h);
        double dThetaXdY = (At(x0, y0 + h).ThetaX - At(x0, y0 - h).ThetaX) / (2 * h);
        double dThetaXdX = (At(x0 + h, y0).ThetaX - At(x0 - h, y0).ThetaX) / (2 * h);
        double dThetaYdY = (At(x0, y0 + h).ThetaY - At(x0, y0 - h).ThetaY) / (2 * h);
        double dWdX = (At(x0 + h, y0).W - At(x0 - h, y0).W) / (2 * h);
        double dWdY = (At(x0, y0 + h).W - At(x0, y0 - h).W) / (2 * h);

        double kappaX = dThetaYdX;
        double kappaY = -dThetaXdY;
        double kappaXY = -dThetaXdX + dThetaYdY;
        double gammaXZ = dWdX + At(x0, y0).ThetaY;
        double gammaYZ = dWdY - At(x0, y0).ThetaX;

        const double tol = 1e-4;
        Assert.True(System.Math.Abs(kappaX - kx) < tol);
        Assert.True(System.Math.Abs(kappaY - ky) < tol);
        Assert.True(System.Math.Abs(kappaXY - kxy) < tol);
        Assert.True(System.Math.Abs(gammaXZ) < tol);
        Assert.True(System.Math.Abs(gammaYZ) < tol);
    }
}

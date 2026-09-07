using System;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests
{
    /// <summary>Срез 7, Task 9: поворот shell resultants (преобразование Мора) и конверсия единиц.</summary>
    public class ShellResultantRotationTests
    {
        [Fact]
        public void ZeroAngle_IsIdentity()
        {
            var source = Resultants(nx: 100, ny: 40, nxy: 15, mx: 8, my: 3, mxy: 2, qx: 5, qy: 7);

            var rotated = ShellResultantRotation.Rotate(source, 0.0);

            AssertClose(source, rotated);
        }

        [Fact]
        public void NinetyDegrees_SwapsNormalComponentsAndFlipsShear()
        {
            var source = Resultants(nx: 100, ny: 40, nxy: 15, mx: 8, my: 3, mxy: 2, qx: 5, qy: 7);

            var rotated = ShellResultantRotation.Rotate(source, Math.PI / 2.0);

            Assert.Equal(40, rotated.Nx, 9);
            Assert.Equal(100, rotated.Ny, 9);
            Assert.Equal(-15, rotated.Nxy, 9);
            Assert.Equal(3, rotated.Mx, 9);
            Assert.Equal(8, rotated.My, 9);
            Assert.Equal(-2, rotated.Mxy, 9);
            Assert.Equal(7, rotated.Qx, 9);
            Assert.Equal(-5, rotated.Qy, 9);
        }

        [Fact]
        public void FortyFiveDegrees_OnPureTension_GivesPureShear()
        {
            // Классическая проверка круга Мора: Nx = -Ny, Nxy = 0 -> под 45° чистый сдвиг.
            // Знак отрицательный, потому что поворачиваются ОСИ (пассивное преобразование):
            // Nxy' = (Ny - Nx)·cos·sin = -100. Та же конвенция даёт -50 в тесте
            // ShearIsNotRotatedAsVector_TensorRuleDiffers и -15 при повороте на 90°.
            var source = Resultants(nx: 100, ny: -100, nxy: 0);

            var rotated = ShellResultantRotation.Rotate(source, Math.PI / 4.0);

            Assert.Equal(0.0, rotated.Nx, 9);
            Assert.Equal(0.0, rotated.Ny, 9);
            Assert.Equal(-100.0, rotated.Nxy, 9);
        }

        [Fact]
        public void TwoRotations_EqualSingleCombinedRotation()
        {
            var source = Resultants(nx: 120, ny: -30, nxy: 45, mx: 9, my: -4, mxy: 6, qx: 3, qy: -8);

            var twice = ShellResultantRotation.Rotate(
                ShellResultantRotation.Rotate(source, 0.3), 0.45);
            var once = ShellResultantRotation.Rotate(source, 0.75);

            AssertClose(once, twice);
        }

        [Fact]
        public void Invariants_ArePreserved()
        {
            var source = Resultants(nx: 120, ny: -30, nxy: 45, mx: 9, my: -4, mxy: 6);

            var rotated = ShellResultantRotation.Rotate(source, 0.61);

            Assert.Equal(source.Nx + source.Ny, rotated.Nx + rotated.Ny, 9);
            Assert.Equal(source.Mx + source.My, rotated.Mx + rotated.My, 9);
            Assert.Equal(source.Nx * source.Ny - source.Nxy * source.Nxy,
                         rotated.Nx * rotated.Ny - rotated.Nxy * rotated.Nxy, 6);
            Assert.Equal(Math.Sqrt(source.Qx * source.Qx + source.Qy * source.Qy),
                         Math.Sqrt(rotated.Qx * rotated.Qx + rotated.Qy * rotated.Qy), 9);
        }

        [Fact]
        public void ShearIsNotRotatedAsVector_TensorRuleDiffers()
        {
            // Защита от подмены тензорного поворота векторным: под 45° при Nx=100, Ny=0, Nxy=0
            // тензорное правило даёт Nx' = 50 и Nxy' = -50, векторное дало бы 70.7.
            var source = Resultants(nx: 100, ny: 0, nxy: 0);

            var rotated = ShellResultantRotation.Rotate(source, Math.PI / 4.0);

            Assert.Equal(50.0, rotated.Nx, 9);
            Assert.Equal(50.0, rotated.Ny, 9);
            Assert.Equal(-50.0, rotated.Nxy, 9);
        }

        [Fact]
        public void ToKilonewton_DividesBySiFactor()
        {
            var source = Resultants(nx: 100_000, ny: -40_000, nxy: 15_000,
                                    mx: 8_000, my: 3_000, mxy: 2_000, qx: 5_000, qy: 7_000);

            var converted = ShellResultantRotation.ToKilonewton(source);

            Assert.Equal(100.0, converted.Nx, 9);
            Assert.Equal(-40.0, converted.Ny, 9);
            Assert.Equal(15.0, converted.Nxy, 9);
            Assert.Equal(8.0, converted.Mx, 9);
            Assert.Equal(3.0, converted.My, 9);
            Assert.Equal(2.0, converted.Mxy, 9);
            Assert.Equal(5.0, converted.Qx, 9);
            Assert.Equal(7.0, converted.Qy, 9);
        }

        [Fact]
        public void AngleBetween_MeasuresSignedAngleAboutNormal()
        {
            // Ось полосы повёрнута на +90° вокруг Z относительно оси оболочки.
            double angle = ShellResultantRotation.AngleBetween(
                1, 0, 0,
                0, 1, 0,
                0, 0, 1);

            Assert.Equal(Math.PI / 2.0, angle, 9);

            double reverse = ShellResultantRotation.AngleBetween(
                0, 1, 0,
                1, 0, 0,
                0, 0, 1);

            Assert.Equal(-Math.PI / 2.0, reverse, 9);
        }

        [Fact]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ShellResultantRotation.Rotate(null!, 0.0));
            Assert.Throws<ArgumentNullException>(() => ShellResultantRotation.ToKilonewton(null!));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ShellResultantRotation.Rotate(Resultants(), double.NaN));
        }

        static ShellSectionResultants Resultants(
            double nx = 0, double ny = 0, double nxy = 0,
            double mx = 0, double my = 0, double mxy = 0,
            double qx = 0, double qy = 0) =>
            new(ElementTag: 1, IntegrationPoint: 0, nx, ny, nxy, mx, my, mxy, qx, qy);

        static void AssertClose(ShellSectionResultants expected, ShellSectionResultants actual)
        {
            Assert.Equal(expected.Nx, actual.Nx, 9);
            Assert.Equal(expected.Ny, actual.Ny, 9);
            Assert.Equal(expected.Nxy, actual.Nxy, 9);
            Assert.Equal(expected.Mx, actual.Mx, 9);
            Assert.Equal(expected.My, actual.My, 9);
            Assert.Equal(expected.Mxy, actual.Mxy, 9);
            Assert.Equal(expected.Qx, actual.Qx, 9);
            Assert.Equal(expected.Qy, actual.Qy, 9);
        }
    }
}

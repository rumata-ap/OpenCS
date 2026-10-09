using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace CSfea.Sparse;

/// <summary>
/// Плотные ядра суперузлового Холецкого. Матрицы — по столбцам с ведущей размерностью (элемент (i, j) блока с
/// началом off — x[off + j·ld + i]); смещения long, так что блоки L могут лежать в общем массиве значений.
/// </summary>
internal static class DenseKernels
{
    private const int Mr = 8, Nr = 4;  // микроблок: 8 строк (два Vector256) × 4 столбца
    private const int Kc = 256;        // глубина блока по суммированию (панель A — в L1/L2)
    private const int Nc = 128;        // ширина блока столбцов C (панель B — в L2)

    /// <summary>
    /// C −= A·Bᵀ: C — p×q, A — p×kk, B — q×kk. При <paramref name="lowerOnly"/> C считается диагональным блоком:
    /// микроблоки целиком над диагональю (строка &lt; столбца) пропускаются, остальные считаются полностью.
    /// </summary>
    public static void GemmNtSub(int p, int q, int kk,
                                 double[] a, long aOff, int lda,
                                 double[] b, long bOff, int ldb,
                                 double[] c, long cOff, int ldc, bool lowerOnly)
    {
        if (p <= 0 || q <= 0 || kk <= 0) return;
        if (!Vector256.IsHardwareAccelerated)
        {
            GemmNtSubScalar(0, p, 0, q, 0, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly);
            return;
        }
        int pFull = p - p % Mr, qFull = q - q % Nr;
        for (int l0 = 0; l0 < kk; l0 += Kc)
        {
            int kc = Math.Min(Kc, kk - l0);
            for (int t0 = 0; t0 < qFull; t0 += Nc)
            {
                int t1 = Math.Min(t0 + Nc, qFull);
                for (int i = 0; i < pFull; i += Mr)
                    for (int t = t0; t < t1; t += Nr)
                    {
                        if (lowerOnly && i + Mr <= t) break;
                        Micro8x4(i, t, l0, kc, a, aOff, lda, b, bOff, ldb, c, cOff, ldc);
                    }
            }
            // Края: нижние строки (p mod 8) и правые столбцы (q mod 4).
            if (pFull < p)
                GemmNtSubScalar(pFull, p, 0, qFull, l0, l0 + kc, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly);
            if (qFull < q)
                GemmNtSubScalar(0, p, qFull, q, l0, l0 + kc, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Micro8x4(int i, int t, int l0, int kc,
                                 double[] a, long aOff, int lda, double[] b, long bOff, int ldb,
                                 double[] c, long cOff, int ldc)
    {
        ref double ra = ref MemoryMarshal.GetArrayDataReference(a);
        ref double rb = ref MemoryMarshal.GetArrayDataReference(b);
        ref double rc = ref MemoryMarshal.GetArrayDataReference(c);
        var c00 = Vector256<double>.Zero; var c10 = Vector256<double>.Zero;
        var c01 = Vector256<double>.Zero; var c11 = Vector256<double>.Zero;
        var c02 = Vector256<double>.Zero; var c12 = Vector256<double>.Zero;
        var c03 = Vector256<double>.Zero; var c13 = Vector256<double>.Zero;
        nuint ai = (nuint)(aOff + (long)l0 * lda + i);
        nuint bi = (nuint)(bOff + (long)l0 * ldb + t);
        nuint sa = (nuint)lda, sb = (nuint)ldb;
        for (int l = 0; l < kc; l++)
        {
            var a0 = Vector256.LoadUnsafe(ref ra, ai);
            var a1 = Vector256.LoadUnsafe(ref ra, ai + 4);
            var b0 = Vector256.Create(Unsafe.Add(ref rb, bi));
            var b1 = Vector256.Create(Unsafe.Add(ref rb, bi + 1));
            var b2 = Vector256.Create(Unsafe.Add(ref rb, bi + 2));
            var b3 = Vector256.Create(Unsafe.Add(ref rb, bi + 3));
            c00 = Vector256.FusedMultiplyAdd(a0, b0, c00); c10 = Vector256.FusedMultiplyAdd(a1, b0, c10);
            c01 = Vector256.FusedMultiplyAdd(a0, b1, c01); c11 = Vector256.FusedMultiplyAdd(a1, b1, c11);
            c02 = Vector256.FusedMultiplyAdd(a0, b2, c02); c12 = Vector256.FusedMultiplyAdd(a1, b2, c12);
            c03 = Vector256.FusedMultiplyAdd(a0, b3, c03); c13 = Vector256.FusedMultiplyAdd(a1, b3, c13);
            ai += sa;
            bi += sb;
        }
        nuint ci = (nuint)(cOff + (long)t * ldc + i), sc = (nuint)ldc;
        Sub(ref rc, ci, c00, c10); ci += sc;
        Sub(ref rc, ci, c01, c11); ci += sc;
        Sub(ref rc, ci, c02, c12); ci += sc;
        Sub(ref rc, ci, c03, c13);

        static void Sub(ref double rc, nuint ci, Vector256<double> lo, Vector256<double> hi)
        {
            (Vector256.LoadUnsafe(ref rc, ci) - lo).StoreUnsafe(ref rc, ci);
            (Vector256.LoadUnsafe(ref rc, ci + 4) - hi).StoreUnsafe(ref rc, ci + 4);
        }
    }

    private static void GemmNtSubScalar(int i0, int i1, int t0, int t1, int l0, int l1,
                                        double[] a, long aOff, int lda, double[] b, long bOff, int ldb,
                                        double[] c, long cOff, int ldc, bool lowerOnly)
    {
        for (int t = t0; t < t1; t++)
        {
            long ct = cOff + (long)t * ldc;
            int iStart = lowerOnly ? Math.Max(i0, t - t % Mr) : i0;
            for (int l = l0; l < l1; l++)
            {
                double bt = b[bOff + (long)l * ldb + t];
                if (bt == 0.0) continue;
                long al = aOff + (long)l * lda;
                for (int i = iStart; i < i1; i++)
                    c[ct + i] -= a[al + i] * bt;
            }
        }
    }

    /// <summary>y[0..len) −= s·x[0..len) (участки массивов с началами yOff, xOff).</summary>
    public static void AxpySub(int len, double s, double[] x, long xOff, double[] y, long yOff)
    {
        ref double rx = ref MemoryMarshal.GetArrayDataReference(x);
        ref double ry = ref MemoryMarshal.GetArrayDataReference(y);
        int i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            var vs = Vector256.Create(s);
            for (; i + 4 <= len; i += 4)
            {
                nuint xi = (nuint)(xOff + i), yi = (nuint)(yOff + i);
                (Vector256.LoadUnsafe(ref ry, yi) - vs * Vector256.LoadUnsafe(ref rx, xi)).StoreUnsafe(ref ry, yi);
            }
        }
        for (; i < len; i++)
            Unsafe.Add(ref ry, (nuint)(yOff + i)) -= s * Unsafe.Add(ref rx, (nuint)(xOff + i));
    }

    /// <summary>
    /// Холецкий панели m×k (по столбцам, ld = m): нижняя трапеция L·Lᵀ = A для первых k строк и L21 = A21·L11⁻ᵀ
    /// ниже. Блочный правосторонний алгоритм: панель шириной nb — левосторонне по столбцам, хвост — через
    /// <see cref="GemmNtSub"/>. Возвращает false, если встретился неположительный ведущий элемент (он заменяется
    /// на |d| или 1e−300, как в up-looking решателе, и счёт продолжается).
    /// </summary>
    public static bool FactorPanel(double[] x, long off, int m, int k, int nb = 64)
    {
        bool spd = true;
        for (int j = 0; j < k; j += nb)
        {
            int jb = Math.Min(nb, k - j);
            // Панель: столбцы j..j+jb, строки от диагонали вниз — левосторонне внутри панели.
            for (int cc = j; cc < j + jb; cc++)
            {
                long colC = off + (long)cc * m;
                for (int l = j; l < cc; l++)
                {
                    long colL = off + (long)l * m;
                    double s = x[colL + cc];
                    if (s != 0.0) AxpySub(m - cc, s, x, colL + cc, x, colC + cc);
                }
                double d = x[colC + cc];
                if (d <= 0.0)
                {
                    spd = false;
                    d = Math.Abs(d) < 1e-300 ? 1e-300 : Math.Abs(d);
                }
                d = Math.Sqrt(d);
                x[colC + cc] = d;
                double inv = 1.0 / d;
                for (long r = colC + cc + 1; r < colC + m; r++) x[r] *= inv;
            }
            // Хвост: столбцы j+jb..k, строки j+jb..m: A −= L(:, панель)·L(столбцы хвоста, панель)ᵀ.
            int j2 = j + jb;
            if (j2 < k)
                GemmNtSub(m - j2, k - j2, jb,
                          x, off + (long)j * m + j2, m,
                          x, off + (long)j * m + j2, m,
                          x, off + (long)j2 * m + j2, m, lowerOnly: true);
        }
        return spd;
    }
}

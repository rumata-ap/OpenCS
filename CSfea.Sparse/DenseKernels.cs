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
    private const int Kc = 256;        // глубина блока по суммированию
    private const int Mc = 96;         // строк в упакованном блоке A (96×256 — в L2)
    private const int Nc = 1024;       // столбцов в упакованной панели B (в L3)
    private const long SmallWork = 1L << 13;   // меньше — без упаковки, простым циклом

    // Упакованные блоки потока: A — микропанели Mr×Kc подряд, B — микропанели Nr×Kc подряд.
    [ThreadStatic] private static double[]? _packA;
    [ThreadStatic] private static double[]? _packB;
    [ThreadStatic] private static double[]? _tile;

    /// <summary>
    /// C −= A·Bᵀ: C — p×q, A — p×kk, B — q×kk. При <paramref name="lowerOnly"/> C считается диагональным блоком
    /// (строка i + <paramref name="rowShift"/> против столбца t): микроблоки целиком над диагональю пропускаются,
    /// остальные считаются полностью. Схема BLIS: панель B и блок A упаковываются в непрерывные буферы потока,
    /// микроядро 8×4 читает их последовательно (без промахов TLB на больших ведущих размерностях).
    /// </summary>
    public static void GemmNtSub(int p, int q, int kk,
                                 double[] a, long aOff, int lda,
                                 double[] b, long bOff, int ldb,
                                 double[] c, long cOff, int ldc, bool lowerOnly)
        => GemmNtSubCore(p, q, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly, 0, UsePacked(p, q, kk));

    // Путь выбирается по размеру всего произведения (а не полосы потока) — от числа потоков результат не зависит.
    private static bool UsePacked(int p, int q, int kk) =>
        Vector256.IsHardwareAccelerated && (long)p * q * kk >= SmallWork;

    private static void GemmNtSubCore(int p, int q, int kk,
                                      double[] a, long aOff, int lda,
                                      double[] b, long bOff, int ldb,
                                      double[] c, long cOff, int ldc, bool lowerOnly, int rowShift, bool packed)
    {
        if (p <= 0 || q <= 0 || kk <= 0) return;
        if (!packed)
        {
            GemmNtSubScalar(0, p, 0, q, 0, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly, rowShift);
            return;
        }
        PackedLoop(p, q, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly, rowShift, null);
    }

    // Упакованный цикл BLIS: панель B (Nc×Kc) пакуется один раз и общая; блоки строк Mc (со своей упаковкой A)
    // идут подряд или делятся между потоками. Элемент C считается одинаково при любом делении.
    private static void PackedLoop(int p, int q, int kk,
                                   double[] a, long aOff, int lda,
                                   double[] b, long bOff, int ldb,
                                   double[] c, long cOff, int ldc, bool lowerOnly, int rowShift, ParallelOptions? po)
    {
        var pb = _packB ??= new double[Nc * Kc];
        int blocks = (p + Mc - 1) / Mc;
        for (int t0 = 0; t0 < q; t0 += Nc)
        {
            if (lowerOnly && rowShift + p <= t0) break;   // все строки C выше этих столбцов
            int nc = Math.Min(Nc, q - t0);
            // Блоки строк целиком над столбцами панели пропускаются.
            int first = lowerOnly ? Math.Max(0, (t0 - rowShift) / Mc - 1) : 0;
            for (int l0 = 0; l0 < kk; l0 += Kc)
            {
                int kc = Math.Min(Kc, kk - l0);
                Pack(b, bOff + (long)l0 * ldb + t0, ldb, nc, kc, Nr, pb);
                if (po != null && blocks - first > 1)
                    Parallel.For(first, blocks, po, blk =>
                        RowBlock(blk * Mc, Math.Min(Mc, p - blk * Mc), t0, nc, l0, kc, a, aOff, lda, pb, c, cOff, ldc, lowerOnly, rowShift));
                else
                    for (int blk = first; blk < blocks; blk++)
                        RowBlock(blk * Mc, Math.Min(Mc, p - blk * Mc), t0, nc, l0, kc, a, aOff, lda, pb, c, cOff, ldc, lowerOnly, rowShift);
            }
        }
    }

    // Блок строк [i0, i0+mc) против упакованной панели B: упаковка A в буфер потока и микроядра.
    private static void RowBlock(int i0, int mc, int t0, int nc, int l0, int kc,
                                 double[] a, long aOff, int lda, double[] pb,
                                 double[] c, long cOff, int ldc, bool lowerOnly, int rowShift)
    {
        if (lowerOnly && rowShift + i0 + mc <= t0) return;
        var pa = _packA ??= new double[Mc * Kc];
        var tmp = _tile ??= new double[Mr * Nr];
        Pack(a, aOff + (long)l0 * lda + i0, lda, mc, kc, Mr, pa);
        for (int jr = 0; jr < nc; jr += Nr)
        {
            int t = t0 + jr;
            for (int ir = 0; ir < mc; ir += Mr)
            {
                int i = i0 + ir;
                if (lowerOnly && rowShift + i + Mr <= t) continue;
                long ci = cOff + (long)t * ldc + i;
                if (ir + Mr <= mc && jr + Nr <= nc)
                    Micro8x4(pa, ir * kc, pb, jr * kc, kc, c, ci, ldc);
                else
                {
                    // Край: полный микроблок во временный, вычесть только существующие элементы.
                    Array.Clear(tmp);
                    Micro8x4(pa, ir * kc, pb, jr * kc, kc, tmp, 0, Mr);
                    int rows = Math.Min(Mr, mc - ir), cols = Math.Min(Nr, nc - jr);
                    for (int x = 0; x < cols; x++)
                        for (int y = 0; y < rows; y++)
                            c[ci + (long)x * ldc + y] += tmp[x * Mr + y];
                }
            }
        }
    }

    // Упаковка блока rows×kc (по столбцам, ld) в микропанели по width строк: dst[(r0·kc + l)·… ] — для микропанели,
    // начинающейся со строки r0, элементы (r0..r0+width) столбца l подряд; недостающие строки — нули.
    private static void Pack(double[] src, long off, int ld, int rows, int kc, int width, double[] dst)
    {
        for (int r0 = 0; r0 < rows; r0 += width)
        {
            int w = Math.Min(width, rows - r0);
            int d = r0 * kc;   // микропанель: width × kc, начало r0·kc (width·(r0/width)·kc)
            long s = off + r0;
            if (w == width)
                for (int l = 0; l < kc; l++, d += width, s += ld)
                    Array.Copy(src, s, dst, d, width);
            else
                for (int l = 0; l < kc; l++, d += width, s += ld)
                {
                    Array.Copy(src, s, dst, d, w);
                    Array.Clear(dst, d + w, width - w);
                }
        }
    }

    // C(8×4, начало ci, ldc) −= Aᵖ·Bᵖᵀ по упакованным микропанелям (A — 8 подряд на l, B — 4 подряд на l).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Micro8x4(double[] pa, int aStart, double[] pb, int bStart, int kc,
                                 double[] c, long ci, int ldc)
    {
        ref double ra = ref MemoryMarshal.GetArrayDataReference(pa);
        ref double rb = ref MemoryMarshal.GetArrayDataReference(pb);
        ref double rc = ref MemoryMarshal.GetArrayDataReference(c);
        var c00 = Vector256<double>.Zero; var c10 = Vector256<double>.Zero;
        var c01 = Vector256<double>.Zero; var c11 = Vector256<double>.Zero;
        var c02 = Vector256<double>.Zero; var c12 = Vector256<double>.Zero;
        var c03 = Vector256<double>.Zero; var c13 = Vector256<double>.Zero;
        nuint ai = (nuint)aStart, bi = (nuint)bStart;
        for (int l = 0; l < kc; l++, ai += Mr, bi += Nr)
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
        }
        nuint p0 = (nuint)ci, sc = (nuint)ldc;
        Sub(ref rc, p0, c00, c10); p0 += sc;
        Sub(ref rc, p0, c01, c11); p0 += sc;
        Sub(ref rc, p0, c02, c12); p0 += sc;
        Sub(ref rc, p0, c03, c13);

        static void Sub(ref double rc, nuint ci, Vector256<double> lo, Vector256<double> hi)
        {
            (Vector256.LoadUnsafe(ref rc, ci) - lo).StoreUnsafe(ref rc, ci);
            (Vector256.LoadUnsafe(ref rc, ci + 4) - hi).StoreUnsafe(ref rc, ci + 4);
        }
    }

    private static void GemmNtSubScalar(int i0, int i1, int t0, int t1, int l0, int l1,
                                        double[] a, long aOff, int lda, double[] b, long bOff, int ldb,
                                        double[] c, long cOff, int ldc, bool lowerOnly, int rowShift)
    {
        for (int t = t0; t < t1; t++)
        {
            long ct = cOff + (long)t * ldc;
            // Нижний треугольник: нужны строки с i + rowShift ≥ t (с запасом до микроблока — лишнее не вредит).
            int iStart = lowerOnly ? Math.Max(i0, t - rowShift - (Mr - 1)) : i0;
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

    /// <summary>Работа (умножений), начиная с которой произведение делится между потоками.</summary>
    public const long ParallelWork = 1L << 21;
    private const int RowChunk = 192;   // полоса строк панели для параллельного TRSM

    /// <summary>
    /// <see cref="GemmNtSub"/> с делением блоков строк C между потоками (при работе от <see cref="ParallelWork"/>).
    /// Каждый элемент C считает один поток в том же порядке суммирования — результат не зависит от числа потоков.
    /// </summary>
    public static void GemmNtSub(int p, int q, int kk,
                                 double[] a, long aOff, int lda,
                                 double[] b, long bOff, int ldb,
                                 double[] c, long cOff, int ldc, bool lowerOnly, ParallelOptions? po)
    {
        bool packed = UsePacked(p, q, kk);
        if (po == null || po.MaxDegreeOfParallelism == 1 || (long)p * q * kk < ParallelWork || p <= Mc || !packed)
            GemmNtSubCore(p, q, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly, 0, packed);
        else
            PackedLoop(p, q, kk, a, aOff, lda, b, bOff, ldb, c, cOff, ldc, lowerOnly, 0, po);
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
    public static bool FactorPanel(double[] x, long off, int m, int k, int nb = 64, ParallelOptions? po = null)
    {
        bool spd = true;
        for (int j = 0; j < k; j += nb)
        {
            int jb = Math.Min(nb, k - j);
            // Диагональный блок jb×jb панели — левосторонне по столбцам.
            int j2 = j + jb;
            for (int cc = j; cc < j2; cc++)
            {
                long colC = off + (long)cc * m;
                for (int l = j; l < cc; l++)
                {
                    long colL = off + (long)l * m;
                    double s = x[colL + cc];
                    if (s != 0.0) AxpySub(j2 - cc, s, x, colL + cc, x, colC + cc);
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
                for (long r = colC + cc + 1; r < colC + j2; r++) x[r] *= inv;
            }
            // Строки ниже блока: X·L11ᵀ = A21 полосами строк (полосы независимы; порядок операций в элементе тот же).
            int below = m - j2;
            if (below > 0)
            {
                int chunks = (below + RowChunk - 1) / RowChunk;
                if (po != null && po.MaxDegreeOfParallelism > 1 && chunks > 1 && (long)below * jb * jb >= ParallelWork)
                    Parallel.For(0, chunks, po, ch => TrsmRows(x, off, m, j, jb, j2 + ch * RowChunk, Math.Min(m, j2 + (ch + 1) * RowChunk)));
                else
                    TrsmRows(x, off, m, j, jb, j2, m);
            }
            // Хвост: столбцы j+jb..k, строки j+jb..m: A −= L(:, панель)·L(столбцы хвоста, панель)ᵀ.
            if (j2 < k)
                GemmNtSub(m - j2, k - j2, jb,
                          x, off + (long)j * m + j2, m,
                          x, off + (long)j * m + j2, m,
                          x, off + (long)j2 * m + j2, m, lowerOnly: true, po);
        }
        return spd;
    }

    // Строки [r0, r1) столбцов панели [j, j+jb): x(:, c) = (x(:, c) − Σ_{l<c} x(:, l)·L(c, l)) / L(c, c).
    private static void TrsmRows(double[] x, long off, int m, int j, int jb, int r0, int r1)
    {
        int len = r1 - r0;
        for (int cc = j; cc < j + jb; cc++)
        {
            long colC = off + (long)cc * m;
            for (int l = j; l < cc; l++)
            {
                long colL = off + (long)l * m;
                double s = x[colL + cc];
                if (s != 0.0) AxpySub(len, s, x, colL + r0, x, colC + r0);
            }
            double inv = 1.0 / x[colC + cc];
            for (long r = colC + r0; r < colC + r1; r++) x[r] *= inv;
        }
    }
}

namespace CScore.Sp16;

/// <summary>Коэффициент η по табл. Д.2 с номером типа сечения; <see cref="Reason"/> — почему η не определён.</summary>
public sealed record EtaValue(double? Eta, int? Type, double? AfAw, string? Note, string? Reason);

public static partial class Sp16Tables
{
    // ── Табл. Д.3: φe при внецентренном сжатии в плоскости симметрии ──

    static readonly double[] D3Lambda = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5, 7.0, 8.0, 9.0];
    static readonly double[] D3Mef =
        [0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5, 7.0, 8.0, 9.0, 10, 12, 14, 17, 20];

    // Значения ×1000 (прим. 1); 0 — ячейка в таблице отсутствует.
    static readonly int[,] D3 =
    {
        { 967, 922, 850, 782, 722, 669, 620, 577, 538, 469, 417, 370, 337, 307, 280, 260, 237, 222, 210, 183, 164, 150, 125, 106, 90, 77 },
        { 925, 854, 778, 711, 653, 600, 563, 520, 484, 427, 382, 341, 307, 283, 259, 240, 225, 209, 196, 175, 157, 142, 121, 103, 86, 74 },
        { 875, 804, 716, 647, 593, 548, 507, 470, 439, 388, 347, 312, 283, 262, 240, 223, 207, 195, 182, 163, 148, 134, 114, 99, 82, 70 },
        { 813, 742, 653, 587, 536, 496, 457, 425, 397, 352, 315, 286, 260, 240, 222, 206, 193, 182, 170, 153, 138, 125, 107, 94, 79, 67 },
        { 742, 672, 587, 526, 480, 442, 410, 383, 357, 317, 287, 262, 238, 220, 204, 190, 178, 168, 158, 144, 130, 118, 101, 90, 76, 65 },
        { 667, 597, 520, 465, 425, 395, 365, 342, 320, 287, 260, 238, 217, 202, 187, 175, 166, 156, 147, 135, 123, 112, 97, 86, 73, 63 },
        { 587, 522, 455, 408, 375, 350, 325, 303, 287, 258, 233, 216, 198, 183, 172, 162, 153, 145, 137, 125, 115, 106, 92, 82, 69, 60 },
        { 505, 447, 394, 356, 330, 309, 289, 270, 256, 232, 212, 197, 181, 168, 158, 149, 140, 135, 127, 118, 108, 98, 88, 78, 66, 57 },
        { 418, 382, 342, 310, 288, 272, 257, 242, 229, 208, 192, 178, 165, 155, 146, 137, 130, 125, 118, 110, 101, 93, 83, 75, 64, 55 },
        { 354, 326, 295, 273, 253, 239, 225, 215, 205, 188, 175, 162, 150, 143, 135, 126, 120, 117, 111, 103, 95, 88, 79, 72, 62, 53 },
        { 302, 280, 256, 240, 224, 212, 200, 192, 184, 170, 158, 148, 138, 132, 124, 117, 112, 108, 104, 95, 89, 84, 75, 69, 60, 51 },
        { 258, 244, 223, 210, 198, 190, 178, 172, 166, 153, 145, 137, 128, 120, 115, 109, 104, 100, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 223, 213, 196, 185, 176, 170, 160, 155, 149, 140, 132, 125, 117, 112, 106, 101, 97, 94, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 194, 186, 173, 163, 157, 152, 145, 141, 136, 127, 121, 115, 108, 102, 98, 94, 91, 87, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 152, 146, 138, 133, 128, 121, 117, 115, 113, 106, 100, 95, 91, 87, 83, 81, 78, 76, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 122, 117, 112, 107, 103, 100, 98, 96, 93, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
    };

    /// <summary>
    /// φe по табл. Д.3 (билинейная интерполяция по λ̄ и mef). При λ̄ &lt; 0,5 и mef &lt; 0,1 принимаются
    /// значения первой строки/столбца (φe убывает с ростом λ̄ и mef — в запас). null — значение вне
    /// таблицы (mef &gt; 20, либо λ̄ больше последней заполненной строки для данного mef).
    /// </summary>
    public static double? PhiE(double lambdaBar, double mef)
    {
        if (mef > D3Mef[^1] + 1e-12) return null;
        double lb = Math.Max(lambdaBar, D3Lambda[0]), me = Math.Max(mef, D3Mef[0]);
        if (lb > D3Lambda[^1] + 1e-12) return null;
        var (i, ti) = Bracket(D3Lambda, lb);
        var (j, tj) = Bracket(D3Mef, me);
        double sum = 0;
        foreach (var (ii, wi) in new[] { (i, 1 - ti), (i + 1, ti) })
            foreach (var (jj, wj) in new[] { (j, 1 - tj), (j + 1, tj) })
            {
                double w = wi * wj;
                if (w <= 1e-12) continue;
                int v = D3[ii, jj];
                if (v == 0) return null;
                sum += w * v / 1000.0;
            }
        return sum;
    }

    /// <summary>Индекс левого узла сетки и доля шага (0…1) для значения внутри диапазона.</summary>
    static (int Index, double T) Bracket(IReadOnlyList<double> xs, double x)
    {
        for (int i = 0; i < xs.Count - 1; i++)
            if (x <= xs[i + 1] + 1e-12)
                return (i, Math.Clamp((x - xs[i]) / (xs[i + 1] - xs[i]), 0, 1));
        return (xs.Count - 2, 1);
    }

    // ── Табл. Д.5: mef для шарнирно опёртых стержней двоякосимметричного сечения ──

    static readonly double[] D5Delta = [-1.0, -0.5, 0.0, 0.5];
    static readonly double[] D5Lambda = [1, 2, 3, 4, 5, 6, 7];
    static readonly double[] D5Mef1 = [0.1, 0.5, 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 7.0, 10.0, 20.0];

    static readonly double[][,] D5 =
    [
        new double[,] // δ = −1
        {
            { 0.10, 0.30, 0.68, 1.12, 1.60, 2.62, 3.55, 4.55, 6.50, 9.40, 19.40 },
            { 0.10, 0.17, 0.39, 0.68, 1.03, 1.80, 2.75, 3.72, 5.65, 8.60, 18.50 },
            { 0.10, 0.10, 0.22, 0.36, 0.55, 1.17, 1.95, 2.77, 4.60, 7.40, 17.20 },
            { 0.10, 0.10, 0.10, 0.18, 0.30, 0.57, 1.03, 1.78, 3.35, 5.90, 15.40 },
            { 0.10, 0.10, 0.10, 0.10, 0.15, 0.23, 0.48, 0.95, 2.18, 4.40, 13.40 },
            { 0.10, 0.10, 0.10, 0.10, 0.10, 0.15, 0.18, 0.40, 1.25, 3.00, 11.40 },
            { 0.10, 0.10, 0.10, 0.10, 0.10, 0.10, 0.10, 0.10, 0.50, 1.70, 9.50 },
        },
        new double[,] // δ = −0,5
        {
            { 0.10, 0.31, 0.68, 1.12, 1.60, 2.62, 3.55, 4.55, 6.50, 9.40, 19.40 },
            { 0.10, 0.22, 0.46, 0.73, 1.05, 1.88, 2.75, 3.72, 5.65, 8.60, 18.50 },
            { 0.10, 0.17, 0.38, 0.58, 0.80, 1.33, 2.00, 2.77, 4.60, 7.40, 17.20 },
            { 0.10, 0.14, 0.32, 0.49, 0.66, 1.05, 1.52, 2.22, 3.50, 5.90, 15.40 },
            { 0.10, 0.10, 0.26, 0.41, 0.57, 0.95, 1.38, 1.80, 2.95, 4.70, 13.40 },
            { 0.10, 0.16, 0.28, 0.40, 0.52, 0.95, 1.25, 1.60, 2.50, 4.00, 11.50 },
            { 0.10, 0.22, 0.32, 0.42, 0.55, 0.95, 1.10, 1.35, 2.20, 3.50, 10.80 },
        },
        new double[,] // δ = 0 (λ̄ = 1, mef,1 = 4: в норме «2,55» — опечатка, принято 3,55)
        {
            { 0.10, 0.32, 0.70, 1.12, 1.60, 2.62, 3.55, 4.55, 6.50, 9.40, 19.40 },
            { 0.10, 0.28, 0.60, 0.90, 1.28, 1.96, 2.75, 3.72, 5.65, 8.40, 18.50 },
            { 0.10, 0.27, 0.55, 0.84, 1.15, 1.75, 2.43, 3.17, 4.80, 7.40, 17.20 },
            { 0.10, 0.26, 0.52, 0.78, 1.10, 1.60, 2.20, 2.83, 4.00, 6.30, 15.40 },
            { 0.10, 0.25, 0.52, 0.78, 1.10, 1.55, 2.10, 2.78, 3.85, 5.90, 14.50 },
            { 0.10, 0.28, 0.52, 0.78, 1.10, 1.55, 2.00, 2.70, 3.80, 5.60, 13.80 },
            { 0.10, 0.32, 0.52, 0.78, 1.10, 1.55, 1.90, 2.60, 3.75, 5.50, 13.00 },
        },
        new double[,] // δ = 0,5
        {
            { 0.10, 0.40, 0.80, 1.23, 1.68, 2.62, 3.55, 4.55, 6.50, 9.10, 19.40 },
            { 0.10, 0.40, 0.78, 1.20, 1.60, 2.30, 3.15, 4.10, 5.85, 8.60, 18.50 },
            { 0.10, 0.40, 0.77, 1.17, 1.55, 2.30, 3.10, 3.90, 5.55, 8.13, 18.00 },
            { 0.10, 0.40, 0.75, 1.13, 1.55, 2.30, 3.05, 3.80, 5.30, 7.60, 17.50 },
            { 0.10, 0.40, 0.75, 1.10, 1.55, 2.30, 3.00, 3.80, 5.30, 7.60, 17.00 },
            { 0.10, 0.40, 0.75, 1.10, 1.50, 2.30, 3.00, 3.80, 5.30, 7.60, 16.50 },
            { 0.10, 0.40, 0.75, 1.10, 1.40, 2.30, 3.00, 3.80, 5.30, 7.60, 16.00 },
        },
    ];

    /// <summary>
    /// mef по табл. Д.5 (линейная интерполяция по δ, λ̄ и mef,1). δ = M2/M1 (M1 — больший по модулю
    /// концевой момент). При λ̄ &lt; 1 и λ̄ &gt; 7 принимаются крайние строки, при δ &gt; 0,5 таблица не
    /// распространяется — mef = mef,1 (в запас). null — mef,1 &gt; 20 (расчёт как изгибаемого элемента).
    /// </summary>
    /// <remarks>
    /// В строке δ = 0, λ̄ = 1 при mef,1 = 4,0 норма (docx и html) содержит «2,55» между 2,62 и 4,55 —
    /// нарушение монотонности; принято 3,55, как во всех остальных строках λ̄ = 1.
    /// </remarks>
    public static double? MefD5(double delta, double lambdaBar, double mef1, out string? note)
    {
        note = null;
        if (mef1 > D5Mef1[^1] + 1e-12) return null;
        if (delta > 0.5 + 1e-12)
        {
            note = "δ > 0,5 — табл. Д.5 не распространяется, принято mef = mef,1 (в запас)";
            return mef1;
        }
        if (lambdaBar < 1 || lambdaBar > 7)
            note = $"λ̄ = {lambdaBar:0.###} вне диапазона табл. Д.5 (1…7) — принята крайняя строка";
        double d = Math.Max(delta, -1.0);
        double lb = Math.Clamp(lambdaBar, 1, 7);
        double me = Math.Max(mef1, D5Mef1[0]);
        var byDelta = new double[D5Delta.Length];
        for (int k = 0; k < D5Delta.Length; k++) byDelta[k] = Interp2(lb, me, D5Lambda, D5Mef1, D5[k]);
        return Interp(d, D5Delta, byDelta);
    }

    // ── Табл. 20: расчётный момент для шарнирно опёртого стержня с одной осью симметрии ──

    /// <summary>
    /// M по табл. 20 (9.2.3). mMax = Mmax·A/(N·Wc); m1 — наибольший момент в средней трети длины
    /// (принимается не менее 0,5Mmax). При mMax &gt; 20 принимается mMax = 20 (M = Mmax). Результат —
    /// не менее 0,5Mmax.
    /// </summary>
    public static double MomentTable20(double mMaxAbs, double m1Abs, double mRelMax, double lambdaBar)
    {
        double m1 = Math.Max(m1Abs, 0.5 * mMaxAbs);
        double mm = Math.Min(mRelMax, 20);
        double m2 = Math.Max(mMaxAbs - 0.25 * lambdaBar * (mMaxAbs - m1), 0.5 * mMaxAbs);
        double m = mm <= 3
            ? (lambdaBar < 4 ? m2 : m1)
            : (lambdaBar < 4 ? m2 + (mm - 3) * (mMaxAbs - m2) / 17 : m1 + (mm - 3) * (mMaxAbs - m1) / 17);
        return Math.Max(m, 0.5 * mMaxAbs);
    }

    // ── Табл. Д.2: коэффициент влияния формы сечения η ──

    /// <summary>
    /// η по табл. Д.2 для изгиба относительно канонической оси x (aboutX) или y; compressedPositive —
    /// наиболее сжатое волокно на стороне y &gt; 0 (x &gt; 0). Отнесение профилей к типам:
    /// прямоугольник — 1; труба — 4; двутавр с равными поясами относительно x и короб — 5 (Af — пояс,
    /// Aw — стенки); двутавр относительно y — 8 (Af — стенка, Aw — пояса); асимметричный двутавр
    /// относительно x, больший пояс сжат, меньший ≈ 0,5 большего — 10; тавр относительно x и швеллер
    /// относительно y — 9 (сжата кромка стенки/полок) или 11 (сжата полка/стенка швеллера).
    /// Промежуточные Af/Aw — линейная интерполяция.
    /// </summary>
    /// <remarks>
    /// Тип 5, Af/Aw ≥ 1, λ̄ ≤ 5, m ≤ 5: в тексте Кодекса (docx) «0,012(6 − m)λ̄», в html и по условию
    /// непрерывности с соседними столбцами (1,4 − 0,02λ̄ при m = 5; 1,3 при λ̄ = 5, m = 0,1) — 0,02(6 − m)λ̄;
    /// изменение № 6 в табл. Д.2 заменило только рисунки типов 1, 9, 11. Рисунки типов 9 и 11 в
    /// изменении № 6 и docx одинаковы (ошибка); различие — по html: у типа 9 сжата кромка стенки
    /// (полки швеллера), у типа 11 — полка тавра (стенка швеллера).
    /// </remarks>
    public static EtaValue Eta(Sp16Section s, bool aboutX, bool compressedPositive, double m, double lambdaBar)
    {
        double mm = Math.Clamp(m, 0.1, 20);
        bool lowL = lambdaBar <= 5, lowM = mm <= 5;
        switch (s.Kind)
        {
            case SteelProfileKind.Rect:
                return new(1.0, 1, null, null, null);
            case SteelProfileKind.Pipe:
                return new(lowL && lowM ? 1.35 - 0.05 * mm - 0.01 * (5 - mm) * lambdaBar : 1.1, 4, null, null, null);
            case SteelProfileKind.Box:
            {
                double af = aboutX ? s.Profile.Bf1 * s.Profile.Tf1 : s.Profile.H * s.Tw;
                double aw = aboutX ? 2 * s.Hw * s.Tw : 2 * (s.Profile.Bf1 - 2 * s.Tw) * s.Profile.Tf1;
                return Type5(af / aw, mm, lambdaBar);
            }
            case SteelProfileKind.IBeam when !aboutX:
                return Type8(s.Hw * s.Tw / (s.AfTop + s.AfBottom), mm, lambdaBar);
            case SteelProfileKind.IBeam when s.Profile.IsDoublySymmetricIBeam:
                return Type5(s.AfTop / (s.Hw * s.Tw), mm, lambdaBar);
            case SteelProfileKind.IBeam:
            {
                double afC = compressedPositive ? s.AfTop : s.AfBottom, afT = compressedPositive ? s.AfBottom : s.AfTop;
                if (afC < afT)
                    return new(null, null, null, null, "асимметричный двутавр со сжатым меньшим поясом в табл. Д.2 не приведён — задайте η вручную");
                if (Math.Abs(afT / afC - 0.5) > 0.1)
                    return new(null, null, null, null, $"тип 10 табл. Д.2 — меньший пояс 0,5 большего; у сечения {afT / afC:0.##} — задайте η вручную");
                return Type10(afC / (s.Hw * s.Tw), mm, lambdaBar);
            }
            case SteelProfileKind.Tee when aboutX:
            {
                bool flangeTop = !s.Profile.Flipped;
                double ratio = s.Profile.Bf1 * s.Profile.Tf1 / (s.Hw * s.Tw);
                return flangeTop == compressedPositive ? Type11(ratio, mm, lambdaBar) : Type9(ratio, mm, lambdaBar);
            }
            case SteelProfileKind.Channel when !aboutX:
            {
                bool webRight = s.Profile.Flipped;
                double ratio = s.Profile.H * s.Tw / (2 * (s.Profile.Bf1 - s.Tw) * s.Profile.Tf1);
                return webRight == compressedPositive ? Type11(ratio, mm, lambdaBar) : Type9(ratio, mm, lambdaBar);
            }
            case SteelProfileKind.Generic:
                return new(null, null, null, null, "профиль не распознан — задайте η вручную");
            default:
                return new(null, null, null, null, "сечение в табл. Д.2 не приведено — задайте η вручную");
        }
    }

    static EtaValue Type5(double r, double m, double lb)
    {
        double[] rows = [0.25, 0.5, 1.0];
        double[] v = lb <= 5 && m <= 5
            ? [1.45 - 0.05 * m - 0.01 * (5 - m) * lb, 1.75 - 0.1 * m - 0.02 * (5 - m) * lb, 1.90 - 0.1 * m - 0.02 * (6 - m) * lb]
            : lb <= 5 ? [1.2, 1.25, 1.4 - 0.02 * lb] : [1.2, 1.25, 1.3];
        return new(Interp(r, rows, v), 5, r, r < 0.25 ? "Af/Aw < 0,25 — принято η при Af/Aw = 0,25 (в запас)" : null, null);
    }

    static EtaValue Type8(double r, double m, double lb)
    {
        if (r < 0.25)
            return new(1.0, 8, r, "Af/Aw < 0,25 — принято наибольшее для типа 8 значение η = 1,0 (в запас)", null);
        double[] rows = [0.25, 0.5, 1.0];
        double[] v = lb <= 5 && m <= 5
            ? [0.75 + 0.05 * m + 0.01 * (5 - m) * lb, 0.5 + 0.1 * m + 0.02 * (5 - m) * lb, 0.25 + 0.15 * m + 0.03 * (5 - m) * lb]
            : [1.0, 1.0, 1.0];
        return new(Interp(r, rows, v), 8, r, null, null);
    }

    static EtaValue Type9(double r, double m, double lb)
    {
        double[] rows = [0.5, 1.0];
        double[] v = lb <= 5 && m <= 5
            ? [1.25 - 0.05 * m - 0.01 * (5 - m) * lb, 1.5 - 0.1 * m - 0.02 * (5 - m) * lb]
            : [1.0, 1.0];
        return new(Interp(r, rows, v), 9, r, r < 0.5 ? "Af/Aw < 0,5 — принято η при Af/Aw = 0,5 (в запас)" : null, null);
    }

    static EtaValue Type10(double r, double m, double lb)
    {
        if (r > 2.0 + 1e-9)
            return new(null, 10, r, null, $"Af/Aw = {r:0.##} > 2 — вне табл. Д.2 (тип 10), задайте η вручную");
        double[] rows = [0.5, 1.0, 2.0];
        double[] v = (lb <= 5, m <= 5) switch
        {
            (true, true) => [1.4, 1.6 - 0.01 * (5 - m) * lb, 1.8 - 0.02 * (5 - m) * lb],
            (false, true) => [1.4, 1.35 + 0.05 * m, 1.3 + 0.1 * m],
            _ => [1.4, 1.6, 1.8],
        };
        return new(Interp(r, rows, v), 10, r, r < 0.5 ? "Af/Aw < 0,5 — принято η при Af/Aw = 0,5 (в запас)" : null, null);
    }

    static EtaValue Type11(double r, double m, double lb)
    {
        if (r > 2.0 + 1e-9)
            return new(null, 11, r, null, $"Af/Aw = {r:0.##} > 2 — вне табл. Д.2 (тип 11), задайте η вручную");
        bool low = lb <= 5 && m <= 5;
        if (r > 1.0 + 1e-9 && !low)
            return new(null, 11, r, null, $"тип 11 при Af/Aw = {r:0.##} > 1 и λ̄ > 5 или m > 5 — в табл. Д.2 значения нет («–»), задайте η вручную");
        double[] rows = low ? [0.5, 1.0, 1.5, 2.0] : [0.5, 1.0];
        double[] v = low
            ? [1.45 + 0.04 * m, 1.8 + 0.12 * m, 2.0 + 0.25 * m + 0.1 * lb, 3.0 + 0.25 * m + 0.1 * lb]
            : lb <= 5 ? [1.65, 2.4] : m <= 5 ? [1.45 + 0.04 * m, 1.8 + 0.12 * m] : [1.65, 2.4];
        return new(Interp(r, rows, v), 11, r, r < 0.5 ? "Af/Aw < 0,5 — принято η при Af/Aw = 0,5 (в запас)" : null, null);
    }
}

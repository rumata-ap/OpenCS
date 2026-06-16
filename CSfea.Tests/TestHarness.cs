using System.Diagnostics;

namespace CSfea.Tests;

/// <summary>Простой раннер проверок с выводом PASS/FAIL и сводкой.</summary>
public static class TestHarness
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static Stopwatch? _sectionSw;

    /// <summary>
    /// Полный прогон: R60 parity, длительные FEM. Включить: <c>CSFEA_SLOW=1</c>.
    /// По умолчанию — быстрый smoke (термальные unit-тесты + короткие огневые).
    /// </summary>
    public static bool IncludeSlowTests
    {
        get
        {
            string? v = Environment.GetEnvironmentVariable("CSFEA_SLOW");
            return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Section(string title)
    {
        _sectionSw?.Stop();
        if (_sectionSw is not null)
            Console.WriteLine($"  ({_sectionSw.ElapsedMilliseconds} ms)");

        _sectionSw = Stopwatch.StartNew();
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");
    }

    public static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _passed++; else _failed++;
        string tag = ok ? "PASS" : "FAIL";
        Console.WriteLine($"  [{tag}] {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    /// <summary>Проверка относительной погрешности значения относительно эталона.</summary>
    public static void CheckRel(string name, double value, double reference, double relTol)
    {
        double err = reference != 0.0 ? (value - reference) / Math.Abs(reference)
                                      : value;
        bool ok = Math.Abs(err) <= relTol;
        Check(name, ok, $"value={value:e4}, ref={reference:e4}, err={err * 100:f3}% (tol={relTol * 100:f2}%)");
    }

    public static void CheckLess(string name, double value, double bound)
        => Check(name, value < bound, $"value={value:e4} < {bound:e4}");

    /// <summary>Пропустить тяжёлый тест, если не задан <c>CSFEA_SLOW=1</c>.</summary>
    public static void RunSlow(string name, Action body)
    {
        if (!IncludeSlowTests)
        {
            _skipped++;
            Console.WriteLine($"  [SKIP] {name}  — CSFEA_SLOW=1 для полного FEM-прогона");
            return;
        }

        body();
    }

    public static int Summary()
    {
        _sectionSw?.Stop();
        if (_sectionSw is not null)
            Console.WriteLine($"  ({_sectionSw.ElapsedMilliseconds} ms)");

        Console.WriteLine();
        string skipNote = _skipped > 0 ? $", {_skipped} SKIP" : "";
        Console.WriteLine($"ИТОГО: {_passed} PASS, {_failed} FAIL{skipNote}");
        return _failed == 0 ? 0 : 1;
    }
}

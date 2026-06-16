namespace CSfea.Tests;

/// <summary>Простой раннер проверок с выводом PASS/FAIL и сводкой.</summary>
public static class TestHarness
{
    private static int _passed;
    private static int _failed;

    public static void Section(string title)
    {
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

    public static int Summary()
    {
        Console.WriteLine();
        Console.WriteLine($"ИТОГО: {_passed} PASS, {_failed} FAIL");
        return _failed == 0 ? 0 : 1;
    }
}

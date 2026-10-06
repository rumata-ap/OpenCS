using System.Reflection;
using System.Text;
using Xunit.Sdk;

// Тесты исторически шли последовательно одним прогоном; часть из них меряет время
// (параллельный/последовательный Richardson, таймауты T6) и пишет в общий Console —
// параллельный запуск классов исказил бы и то, и другое.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CSfea.Tests;

/// <summary>
/// Мягкие проверки поверх xUnit: <see cref="Check"/> не прерывает тест, а копит результат;
/// по завершении [Fact] атрибут <see cref="HarnessChecksAttribute"/> валит тест, если была
/// хоть одна FAIL, и выводит журнал всех проверок теста. Так один [Fact] по-прежнему
/// показывает все расхождения сразу, а не только первое.
/// </summary>
public static class TestHarness
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Log = new();
    private static int _failed;
    private static int _skipped;

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

    public static void Section(string title) => Write($"=== {title} ===");

    public static void Check(string name, bool ok, string detail = "")
    {
        string tag = ok ? "PASS" : "FAIL";
        lock (Gate)
        {
            if (!ok) _failed++;
            WriteLocked($"  [{tag}] {name}{(detail.Length > 0 ? "  — " + detail : "")}");
        }
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
            lock (Gate)
            {
                _skipped++;
                WriteLocked($"  [SKIP] {name}  — CSFEA_SLOW=1 для полного FEM-прогона");
            }
            return;
        }

        body();
    }

    internal static void Begin()
    {
        lock (Gate)
        {
            Log.Clear();
            _failed = 0;
            _skipped = 0;
        }
    }

    internal static void End()
    {
        string log;
        int failed;
        lock (Gate)
        {
            log = Log.ToString();
            failed = _failed;
        }

        if (failed > 0)
            throw new XunitException($"{failed} FAIL из проверок TestHarness:{Environment.NewLine}{log}");
    }

    private static void Write(string line)
    {
        lock (Gate) WriteLocked(line);
    }

    private static void WriteLocked(string line)
    {
        Log.AppendLine(line);
        Console.WriteLine(line);
    }
}

/// <summary>Обнуляет журнал <see cref="TestHarness"/> перед [Fact] и проверяет его после.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HarnessChecksAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) => TestHarness.Begin();

    public override void After(MethodInfo methodUnderTest) => TestHarness.End();
}

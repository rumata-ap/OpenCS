using CScore;

namespace CSfea.Tests;

public static class SteelClassifierTests
{
    public static void RunAll()
    {
        TestHarness.Section("SteelClassifier: Классификация элементов сечения (таблица 2 СП 16)");

        ClassifyWeb_ClassA();
        ClassifyWeb_ClassB();
        ClassifyWeb_ClassC();
        ClassifyFlange_ClassA();
        ClassifyFlange_ClassB();
        ClassifyFlange_ClassC();
        ClassifyCircularHollow_ClassA();
        ClassifyCircularHollow_ClassB();
        ClassifyRectangularHollow_ClassA();
    }

    static void ClassifyWeb_ClassA()
    {
        // d/tw = 30, limit A = 33·ε̄ ≈ 33 для fy=235
        var result = SteelClassifier.ClassifyWeb(0.30, 0.01, 235);
        TestHarness.Check("ClassifyWeb ClassA",
            result.Class == SteelClassifier.ElementClass.A,
            $"d/tw={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyWeb_ClassB()
    {
        // d/tw = 36, limit B = 38·ε̄ ≈ 38 для fy=235
        var result = SteelClassifier.ClassifyWeb(0.36, 0.01, 235);
        TestHarness.Check("ClassifyWeb ClassB",
            result.Class == SteelClassifier.ElementClass.B,
            $"d/tw={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyWeb_ClassC()
    {
        // d/tw = 41, limit C = 42·ε̄ ≈ 42 для fy=235
        var result = SteelClassifier.ClassifyWeb(0.41, 0.01, 235);
        TestHarness.Check("ClassifyWeb ClassC",
            result.Class == SteelClassifier.ElementClass.C,
            $"d/tw={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyFlange_ClassA()
    {
        // bf/tf = 8, limit A = 9·ε̄ ≈ 9 для fy=235
        var result = SteelClassifier.ClassifyFlange(0.08, 0.01, 235);
        TestHarness.Check("ClassifyFlange ClassA",
            result.Class == SteelClassifier.ElementClass.A,
            $"bf/tf={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyFlange_ClassB()
    {
        // bf/tf = 9.5, limit B = 10·ε̄ ≈ 10 для fy=235
        var result = SteelClassifier.ClassifyFlange(0.095, 0.01, 235);
        TestHarness.Check("ClassifyFlange ClassB",
            result.Class == SteelClassifier.ElementClass.B,
            $"bf/tf={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyFlange_ClassC()
    {
        // bf/tf = 13, limit C = 14·ε̄ ≈ 14 для fy=235
        var result = SteelClassifier.ClassifyFlange(0.13, 0.01, 235);
        TestHarness.Check("ClassifyFlange ClassC",
            result.Class == SteelClassifier.ElementClass.C,
            $"bf/tf={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyCircularHollow_ClassA()
    {
        // D/t = 40, limit A = 50·ε̄² = 50 для fy=235
        var result = SteelClassifier.ClassifyCircularHollow(0.2, 0.005, 235);
        TestHarness.Check("ClassifyCircularHollow ClassA",
            result.Class == SteelClassifier.ElementClass.A,
            $"D/t={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyCircularHollow_ClassB()
    {
        // D/t = 60, limit B = 70·ε̄² = 70 для fy=235
        var result = SteelClassifier.ClassifyCircularHollow(0.2, 0.00333, 235);
        TestHarness.Check("ClassifyCircularHollow ClassB",
            result.Class == SteelClassifier.ElementClass.B,
            $"D/t={result.WidthToThickness:F1}, class={result.Class}");
    }

    static void ClassifyRectangularHollow_ClassA()
    {
        // b/t = 30, limit A = 33·ε̄ ≈ 33 для fy=235
        var result = SteelClassifier.ClassifyRectangularHollow(0.15, 0.005, 235);
        TestHarness.Check("ClassifyRectangularHollow ClassA",
            result.Class == SteelClassifier.ElementClass.A,
            $"b/t={result.WidthToThickness:F1}, class={result.Class}");
    }
}

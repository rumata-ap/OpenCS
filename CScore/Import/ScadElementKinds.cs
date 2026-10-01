namespace CScore.Import;

/// <summary>Категория КЭ SCAD для импорта топологии.</summary>
public enum ScadElementKind { Beam, Shell, Skip }

/// <summary>
/// Общие правила для типов КЭ SCAD (txt-экспорт и SCADAPIX.dll): классификация типа и
/// перевод порядка узлов четырёхугольника в контурный.
/// </summary>
public static class ScadElementKinds
{
    /// <summary>
    /// Классифицирует КЭ по коду типа SCAD и числу узлов: стержни — типы 1–10 с 2 узлами;
    /// пластины/оболочки — 11–20 и 41–50 с 3–4 узлами; остальное (балки-стенки 21–30, объёмные,
    /// связи 51, жёсткие вставки 100, 200 …) — пропуск.
    /// </summary>
    public static ScadElementKind Classify(int type, int nodeCount)
    {
        if (type is >= 1 and <= 10 && nodeCount == 2)
            return ScadElementKind.Beam;
        if ((type is >= 11 and <= 20 || type is >= 41 and <= 50) && nodeCount is 3 or 4)
            return ScadElementKind.Shell;
        return ScadElementKind.Skip;
    }

    /// <summary>
    /// SCAD хранит узлы четырёхугольника «зигзагом» (1-2 по нижней стороне, 3-4 по верхней,
    /// обход по контуру — 1-2-4-3), а сетка OpenCS — по контуру. Переставляет
    /// [a,b,c,d] → [a,b,d,c]; для другого числа узлов возвращает массив как есть.
    /// Первые два узла и нормаль (p2−p1)×(p3−p1) от перестановки не меняются.
    /// </summary>
    public static int[] ToPerimeterOrder(int[] nodes) =>
        nodes.Length == 4 ? [nodes[0], nodes[1], nodes[3], nodes[2]] : nodes;
}

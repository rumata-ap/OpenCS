namespace CScore.Import;

/// <summary>Категория КЭ SCAD для импорта топологии.</summary>
public enum ScadElementKind { Beam, Shell, Skip }

/// <summary>
/// Общие правила для типов КЭ SCAD (txt-экспорт и SCADAPIX.dll): классификация типа.
/// Узлы четырёхугольника SCAD (как и ЛИРЫ) идут «1 2 4 3» по обходу контура — в этом же порядке их
/// хранит сетка OpenCS (обход n1→n2→n4→n3 делают потребители, см. Fem3DVM.BuildShellEdges),
/// поэтому при импорте порядок не меняется.
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
}

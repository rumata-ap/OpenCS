namespace OpenCS.Views.Helpers;

/// <summary>Разбиение T3/T6-элементов сетки на линейные треугольники для отрисовки.</summary>
internal static class FireMeshTriangulation
{
    public static IEnumerable<(int N0, int N1, int N2)> CornerTriangles(int[] el)
    {
        if (el.Length == 3)
        {
            yield return (el[0], el[1], el[2]);
            yield break;
        }

        if (el.Length >= 6)
        {
            yield return (el[0], el[3], el[5]);
            yield return (el[3], el[1], el[4]);
            yield return (el[5], el[4], el[2]);
            yield return (el[3], el[4], el[5]);
        }
    }
}

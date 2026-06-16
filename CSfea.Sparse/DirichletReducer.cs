namespace CSfea.Sparse;

/// <summary>
/// Обработка граничных условий Дирихле методом исключения DOF.
/// Из полной матрицы (в формате COO-триплетов) и набора закреплённых DOF
/// формирует редуцированную систему
/// <c>K_ff · u_free = F_free − K_fe · u_fixed</c>
/// (см. assembly.py: <c>_solve_with_bcs</c>). Возвращает свободные индексы,
/// чтобы развернуть решение обратно в полный вектор.
/// </summary>
public static class DirichletReducer
{
    /// <summary>Свободные DOF (дополнение к закреплённым) в порядке возрастания.</summary>
    public static int[] FreeDofs(int ndof, IEnumerable<int> fixedDofs)
    {
        var isFixed = new bool[ndof];
        foreach (int d in fixedDofs)
            isFixed[d] = true;
        var free = new List<int>(ndof);
        for (int i = 0; i < ndof; i++)
            if (!isFixed[i]) free.Add(i);
        return free.ToArray();
    }

    /// <summary>Редуцированная система по свободным DOF.</summary>
    public readonly record struct Reduced(CscMatrix Kff, double[] Fmod, int[] Free);

    /// <summary>
    /// Построить редуцированную систему за один проход по триплетам.
    /// </summary>
    /// <param name="kFull">Полная матрица жёсткости (COO).</param>
    /// <param name="f">Полный вектор нагрузки.</param>
    /// <param name="fixedDofs">Закреплённые DOF.</param>
    /// <param name="uFixed">Предписанные значения на закреплённых DOF (или null = 0).</param>
    public static Reduced Reduce(CooMatrix kFull, double[] f, int[] fixedDofs, double[]? uFixed)
    {
        int ndof = kFull.Rows;
        var isFixed = new bool[ndof];
        var fixedValue = new double[ndof];
        for (int t = 0; t < fixedDofs.Length; t++)
        {
            int d = fixedDofs[t];
            isFixed[d] = true;
            fixedValue[d] = uFixed != null ? uFixed[t] : 0.0;
        }

        int[] free = FreeDofs(ndof, fixedDofs);
        var globalToFree = new int[ndof];
        for (int i = 0; i < ndof; i++) globalToFree[i] = -1;
        for (int i = 0; i < free.Length; i++) globalToFree[free[i]] = i;

        int nf = free.Length;
        var fmod = new double[nf];
        for (int i = 0; i < nf; i++) fmod[i] = f[free[i]];

        // Полная матрица в CSC: один проход по столбцам.
        var csc = kFull.ToCsc();
        var triplet = new CooMatrix(nf, nf, csc.Nnz);
        for (int c = 0; c < csc.Cols; c++)
        {
            int cf = globalToFree[c];
            bool colFixed = isFixed[c];
            double colVal = fixedValue[c];
            for (int p = csc.ColPtr[c]; p < csc.ColPtr[c + 1]; p++)
            {
                int r = csc.RowIdx[p];
                if (isFixed[r]) continue;          // строка закреплена — пропускаем
                int rf = globalToFree[r];
                double v = csc.Values[p];
                if (!colFixed)
                    triplet.Add(rf, cf, v);
                else
                    fmod[rf] -= v * colVal;        // перенос K_fe·u_fixed в ПЧ
            }
        }
        return new Reduced(triplet.ToCsc(), fmod, free);
    }

    /// <summary>Развернуть решение по свободным DOF в полный вектор (с u_fixed).</summary>
    public static double[] Expand(int ndof, int[] free, double[] uFree,
                                  int[] fixedDofs, double[]? uFixed)
    {
        var u = new double[ndof];
        for (int i = 0; i < free.Length; i++)
            u[free[i]] = uFree[i];
        for (int t = 0; t < fixedDofs.Length; t++)
            u[fixedDofs[t]] = uFixed != null ? uFixed[t] : 0.0;
        return u;
    }
}

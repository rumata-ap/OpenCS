using CSfea.Sparse;

namespace CSfea.Core;

/// <summary>Переиспользуемая LU-факторизация линейной системы K·u=F с граничными условиями
/// Дирихле на фиксированном наборе DOF. Факторизует один раз при конструировании; повторные
/// Solve(uFixed) с ДРУГИМИ значениями на тех же fixedDofs пересчитывают только правую часть
/// (без пересборки/переразложения K) — см. спеку, раздел «Кэш CSfea». Требует, чтобы K не
/// зависела от состояния (только линейный материал — ответственность вызывающей стороны,
/// см. ShellMeshPatchPreflight).</summary>
public sealed class LinearDirichletSystem
{
    readonly ShellMesh _mesh;
    readonly int[] _fixedDofs;
    readonly int[] _free;
    readonly int[] _globalToFree;
    readonly bool[] _isFixed;
    readonly CscMatrix _kFullCsc;
    readonly SparseLuSolver _solver = new();

    public int FactorizeCount { get; private set; }

    public LinearDirichletSystem(ShellMesh mesh, int[] fixedDofs)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(fixedDofs);
        if (fixedDofs.Length == 0)
            throw new ArgumentException("Нужен хотя бы один закреплённый DOF.", nameof(fixedDofs));

        _mesh = mesh;
        _fixedDofs = fixedDofs;
        _free = DirichletReducer.FreeDofs(mesh.NDof, fixedDofs);

        _isFixed = new bool[mesh.NDof];
        foreach (int d in fixedDofs) _isFixed[d] = true;

        _globalToFree = new int[mesh.NDof];
        Array.Fill(_globalToFree, -1);
        for (int i = 0; i < _free.Length; i++) _globalToFree[_free[i]] = i;

        CooMatrix kFull = mesh.AssembleK();
        _kFullCsc = kFull.ToCsc();

        var zeroF = new double[mesh.NDof];
        DirichletReducer.Reduced reduced = DirichletReducer.Reduce(kFull, zeroF, fixedDofs, uFixed: null);
        _solver.Factorize(reduced.Kff);
        FactorizeCount = 1;
    }

    /// <summary>Решает K·u=0 (без внешней нагрузки) с предписанными перемещениями uFixed на
    /// fixedDofs (тот же порядок, что передан в конструктор) — возвращает полный вектор
    /// перемещений (NDof).</summary>
    public double[] Solve(double[] uFixed)
    {
        ArgumentNullException.ThrowIfNull(uFixed);
        if (uFixed.Length != _fixedDofs.Length)
            throw new ArgumentException("Длина uFixed должна совпадать с числом fixedDofs.", nameof(uFixed));

        var fixedValue = new double[_mesh.NDof];
        for (int t = 0; t < _fixedDofs.Length; t++)
            fixedValue[_fixedDofs[t]] = uFixed[t];

        var fmod = new double[_free.Length];
        for (int c = 0; c < _kFullCsc.Cols; c++)
        {
            if (!_isFixed[c]) continue;
            double colVal = fixedValue[c];
            if (colVal == 0.0) continue;
            for (int p = _kFullCsc.ColPtr[c]; p < _kFullCsc.ColPtr[c + 1]; p++)
            {
                int r = _kFullCsc.RowIdx[p];
                if (_isFixed[r]) continue;
                fmod[_globalToFree[r]] -= _kFullCsc.Values[p] * colVal;
            }
        }

        double[] uFree = _solver.Solve(fmod);
        return DirichletReducer.Expand(_mesh.NDof, _free, uFree, _fixedDofs, uFixed);
    }
}

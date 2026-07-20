using CScore;

namespace OpenCS.OpenSees.CScore;

/// <summary>Эффективные характеристики сечения для elasticBeamColumn: A, E, Iy, Iz.
/// Приведённые (stiffness-weighted): E·A=EA, E·Iy=EIy, E·Iz=EIx — точно и для многоматериальных.</summary>
public readonly record struct FemSectionStiffness(double A, double E, double Iy, double Iz)
{
    /// <summary>Строит эффективные характеристики из GeoProps. Ось X сечения → локальная z,
    /// ось Y сечения → локальная y: Iy_arg←EIy (∫x²E·dA), Iz_arg←EIx (∫y²E·dA).</summary>
    public static FemSectionStiffness FromGeoProps(GeoProps gp)
    {
        if (gp.A <= 0 || gp.EA <= 0)
            throw new InvalidOperationException("Сечение не готово: A и EA должны быть положительны.");
        double e = gp.EA / gp.A;
        return new FemSectionStiffness(A: gp.A, E: e, Iy: gp.EIy / e, Iz: gp.EIx / e);
    }
}

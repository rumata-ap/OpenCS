namespace CSfea.Core;

/// <summary>
/// Адаптер над <see cref="BeamSection"/> с линейной диагональной касательной.
/// Порт <c>fea/section_response.py: LinearBeamResponse</c>.
/// </summary>
public sealed class LinearBeamResponse : IBeamSectionResponse
{
    private readonly double[,] _tangent;

    public LinearBeamResponse(BeamSection section)
    {
        Section = section;
        EA = section.E * section.A;
        EIy = section.E * section.Iy;
        EIz = section.E * section.Iz;
        GJ = section.G * section.J;
        _tangent = new[,]
        {
            { EA, 0.0, 0.0 },
            { 0.0, EIy, 0.0 },
            { 0.0, 0.0, EIz },
        };
    }

    /// <summary>Исходное сечение.</summary>
    public BeamSection Section { get; }

    /// <summary>Осевая жёсткость.</summary>
    public double EA { get; }

    /// <summary>Изгибная жёсткость вокруг оси y.</summary>
    public double EIy { get; }

    /// <summary>Изгибная жёсткость вокруг оси z.</summary>
    public double EIz { get; }

    /// <summary>Жёсткость кручения.</summary>
    public double GJ { get; }

    public BeamForces Forces(double eps0, double kappaY, double kappaZ)
        => new(EA * eps0, EIy * kappaY, EIz * kappaZ);

    public double[,] Tangent(double eps0, double kappaY, double kappaZ) => _tangent;

    public (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ)
        => (EA, EIy, EIz);

    public double TorsionalStiffness(double twist = 0.0) => GJ;

    public void Commit() { }

    public void Reset() { }
}

namespace CSfea.Core;

/// <summary>
/// Балочное сечение через заранее вычисленную кривую момент–кривизна (M–φ).
/// Осевая жёсткость линейна; изгиб вокруг z — нелинейный по кривой.
/// Порт <c>fea/section_response.py: MomentCurvatureBeamResponse</c>.
/// </summary>
public sealed class MomentCurvatureBeamResponse : IBeamSectionResponse
{
    private readonly double[] _phi;   // кривизна (≥0, возрастает)
    private readonly double[] _m;     // момент (≥0)
    private readonly double[] _dMdPhi;
    private readonly double _ea;
    private readonly double _eIy;

    /// <param name="phi">Узлы кривизны (≥0, монотонно возрастают).</param>
    /// <param name="m">Момент в узлах (≥0).</param>
    /// <param name="ea">Осевая жёсткость (линейная).</param>
    /// <param name="eIyLinear">Жёсткость вокруг оси y (для 3D; в 2D = 0).</param>
    public MomentCurvatureBeamResponse(double[] phi, double[] m, double ea, double eIyLinear = 0.0)
    {
        _phi = phi;
        _m = m;
        _ea = ea;
        _eIy = eIyLinear;
        _dMdPhi = Num.Gradient(m, phi);
    }

    private double InterpM(double kappaZ)
    {
        double mVal = Num.Interp(Math.Abs(kappaZ), _phi, _m);
        return mVal * (kappaZ >= 0.0 ? 1.0 : -1.0);
    }

    private double InterpEiTan(double kappaZ)
        => Num.Interp(Math.Abs(kappaZ), _phi, _dMdPhi);

    public BeamForces Forces(double eps0, double kappaY, double kappaZ)
        => new(_ea * eps0, _eIy * kappaY, InterpM(kappaZ));

    public double[,] Tangent(double eps0, double kappaY, double kappaZ)
    {
        double eiTan = InterpEiTan(kappaZ);
        return new[,]
        {
            { _ea, 0.0, 0.0 },
            { 0.0, _eIy, 0.0 },
            { 0.0, 0.0, eiTan },
        };
    }

    public (double EA, double EIy, double EIz) Secant(double eps0, double kappaY, double kappaZ)
    {
        double eIzS;
        if (Math.Abs(kappaZ) > 1e-14)
            eIzS = Math.Abs(InterpM(kappaZ) / kappaZ);
        else
            eIzS = _eIy;
        return (_ea, _eIy, eIzS);
    }

    public double TorsionalStiffness(double twist = 0.0) => 0.0;

    public void Commit() { }

    public void Reset() { }
}

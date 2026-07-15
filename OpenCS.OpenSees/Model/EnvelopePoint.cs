namespace OpenCS.OpenSees.Model;

/// <summary>Точка монотонной диаграммы материала в единицах SI.</summary>
public readonly record struct EnvelopePoint(double Strain, double StressPa);

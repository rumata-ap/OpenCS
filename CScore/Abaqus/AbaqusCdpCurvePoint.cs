namespace CScore.Abaqus;

/// <summary>Точка преобразованной CDP-кривой с расчётными диагностическими величинами.</summary>
/// <param name="Stress">Положительное напряжение в единицах Abaqus.</param>
/// <param name="TotalStrain">Полная деформация исходной точки.</param>
/// <param name="AbaqusStrain">Неупругая деформация сжатия или трещинная деформация растяжения.</param>
/// <param name="Damage">Повреждение в диапазоне от 0 до 0.999.</param>
/// <param name="PlasticStrain">Пластическая деформация, вычисленная для Abaqus CDP.</param>
/// <param name="ElasticStrain">Упругая деформация stress/E0.</param>
public sealed record AbaqusCdpCurvePoint(
    double Stress,
    double TotalStrain,
    double AbaqusStrain,
    double Damage,
    double PlasticStrain,
    double ElasticStrain);

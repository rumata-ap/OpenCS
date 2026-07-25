namespace OpenCS.OpenSees.Model;

/// <summary>Параметрическое описание материала OpenSees (в противовес кусочно-линейной
/// огибающей `EnvelopePoint`) — источник для типизированного построения Tcl-команды
/// `uniaxialMaterial` без обхода через строки.</summary>
public abstract record NativeMaterialSpec;

/// <summary>uniaxialMaterial Concrete04 tag fc ec ecu Ec [fct et beta] — модель Поповича на
/// сжатие (Popovics 1973) + экспоненциальное затухание растяжения (не линейное до плоского нуля,
/// как Concrete02, — экспонента асимптотически приближается к нулю, никогда не даёт буквально
/// плоского нулевого участка, что снижает риск вырожденной матрицы гибкости на полностью
/// растрескавшихся волокнах). Fc/Ec0/Ecu отрицательные (сжатие), Ec — начальный модуль упругости
/// (не выводится из Fc/Ec0, как в Concrete02, а задаётся напрямую — реальный E материала).
/// Fct/Et/Beta — null, если растяжение бетона отключено (considerConcreteTension=false):
/// Concrete04 поддерживает опциональный хвост параметров, при их отсутствии растяжение не
/// учитывается вовсе. Все величины в Па; Ec0/Ecu/Et — деформации, безразмерные.</summary>
public sealed record Concrete04Spec(
    double Fc, double Ec0, double Ecu, double Ec, double? Fct, double? Et, double? Beta)
    : NativeMaterialSpec;

/// <summary>uniaxialMaterial Steel01 tag Fy E0 b — билинейная сталь/арматура.
/// Fy/E0 положительные величины в Па, b — безразмерное отношение модуля упрочнения к E0.</summary>
public sealed record Steel01Spec(double Fy, double E0, double B) : NativeMaterialSpec;

/// <summary>uniaxialMaterial Steel02 tag Fy E0 b R0 cR1 cR2 — Giuffré-Menegotto-Pinto.
/// R0/cR1/cR2 — безразмерные параметры перехода кривой (стандартные рекомендованные значения).</summary>
public sealed record Steel02Spec(double Fy, double E0, double B, double R0, double CR1, double CR2)
    : NativeMaterialSpec;

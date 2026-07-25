namespace OpenCS.OpenSees.Model;

/// <summary>Параметрическое описание материала OpenSees (в противовес кусочно-линейной
/// огибающей `EnvelopePoint`) — источник для типизированного построения Tcl-команды
/// `uniaxialMaterial` без обхода через строки.</summary>
public abstract record NativeMaterialSpec;

/// <summary>uniaxialMaterial Concrete01 tag fpc epsc0 fpcu epsU — без растяжения.
/// Все величины в Па; fpc/epsc0/fpcu/epsU отрицательные (сжатие).</summary>
public sealed record Concrete01Spec(double Fpc, double Epsc0, double Fpcu, double EpsU)
    : NativeMaterialSpec;

/// <summary>uniaxialMaterial Concrete02 tag fpc epsc0 fpcu epsU lambda ft Ets — с линейным
/// размягчением при растяжении. fpc/epsc0/fpcu/epsU отрицательные (сжатие), ft/Ets положительные
/// (растяжение), все величины в Па (Ets — Па/ед.деформации).</summary>
public sealed record Concrete02Spec(
    double Fpc, double Epsc0, double Fpcu, double EpsU, double Lambda, double Ft, double Ets)
    : NativeMaterialSpec;

/// <summary>uniaxialMaterial Steel01 tag Fy E0 b — билинейная сталь/арматура.
/// Fy/E0 положительные величины в Па, b — безразмерное отношение модуля упрочнения к E0.</summary>
public sealed record Steel01Spec(double Fy, double E0, double B) : NativeMaterialSpec;

/// <summary>uniaxialMaterial Steel02 tag Fy E0 b R0 cR1 cR2 — Giuffré-Menegotto-Pinto.
/// R0/cR1/cR2 — безразмерные параметры перехода кривой (стандартные рекомендованные значения).</summary>
public sealed record Steel02Spec(double Fy, double E0, double B, double R0, double CR1, double CR2)
    : NativeMaterialSpec;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Model;

/// <summary>Capability-описание native nD-материала для shell LayeredShell.</summary>
public abstract record NativeShellMaterialSpec
{
    /// <summary>Имя native material command.</summary>
    public abstract string Kind { get; }

    /// <summary>Детерминированный fingerprint содержательных параметров материала.</summary>
    public abstract string Fingerprint { get; }

    /// <summary>Строит Tcl-команду объявления nD-материала.</summary>
    public abstract string ToTcl(int tag);

    /// <summary>Дополнительные Tcl-команды-зависимости материала.</summary>
    public virtual IReadOnlyList<string> AuxiliaryCommands => [];

    /// <summary>Tag материала, от которого зависит этот material (например, база
    /// PlateFromPlaneStress или uniaxial PlateRebar). `null` — материал независим.</summary>
    public virtual int? DependsOnMaterialTag => null;

    /// <summary>Возвращает копию spec с переопределённым tag'ом зависимости — используется
    /// PlateSectionOpenSeesMapper после финальной перенумеровки цепочки материалов.</summary>
    public virtual NativeShellMaterialSpec WithDependencyTag(int tag) =>
        throw new InvalidOperationException($"{Kind} материал не поддерживает зависимость.");

    /// <summary>Форматирует аргумент материала инвариантно относительно культуры.</summary>
    protected static string F(double value) => TclNumber.Format(value);

    /// <summary>Строит SHA-256 fingerprint канонических частей.</summary>
    protected static string Hash(params string[] parts)
    {
        string canonical = string.Join("|", parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>Упругий isotropic nD-материал OpenSees.</summary>
public sealed record ElasticIsotropicShellMaterialSpec(double E, double Nu) : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "ElasticIsotropic";

    /// <inheritdoc />
    public override string Fingerprint => Hash(Kind, F(E), F(Nu));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        Validate(tag);
        return $"nDMaterial ElasticIsotropic {tag} {F(E)} {F(Nu)}";
    }

    private void Validate(int tag)
    {
        if (tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (!double.IsFinite(E) || E <= 0)
            throw new InvalidOperationException("Модуль E shell-материала должен быть положительным и конечным.");
        if (!double.IsFinite(Nu) || Nu <= -1 || Nu >= 0.5)
            throw new InvalidOperationException("Коэффициент Пуассона shell-материала должен быть в интервале (-1; 0.5).");
    }
}

/// <summary>Ориентированный smeared reinforcement nD-материал PlateRebar.</summary>
public sealed record PlateRebarShellMaterialSpec(int UniaxialMaterialTag, double AngleDegrees)
    : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "PlateRebar";

    /// <inheritdoc />
    public override string Fingerprint => Hash(Kind,
        UniaxialMaterialTag.ToString(CultureInfo.InvariantCulture), F(AngleDegrees));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        if (tag <= 0 || UniaxialMaterialTag <= 0)
            throw new InvalidOperationException("Tags shell-материала должны быть положительными.");
        if (!double.IsFinite(AngleDegrees))
            throw new InvalidOperationException("Направление PlateRebar должно быть конечным.");

        return $"nDMaterial PlateRebar {tag} {UniaxialMaterialTag} {F(AngleDegrees)}";
    }

    /// <inheritdoc />
    public override int? DependsOnMaterialTag => UniaxialMaterialTag;

    /// <inheritdoc />
    public override NativeShellMaterialSpec WithDependencyTag(int tag) =>
        this with { UniaxialMaterialTag = tag };
}

/// <summary>Упругий uniaxial-материал OpenSees — зависимость для UniaxialMaterialTag
/// у ориентированного smeared-армирования (<see cref="PlateRebarShellMaterialSpec"/>).</summary>
public sealed record ElasticUniaxialShellMaterialSpec(double E) : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "Elastic";

    /// <inheritdoc />
    public override string Fingerprint => Hash(Kind, F(E));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        if (tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (!double.IsFinite(E) || E <= 0)
            throw new InvalidOperationException("Модуль E uniaxial-материала должен быть положительным и конечным.");

        return $"uniaxialMaterial Elastic {tag} {F(E)}";
    }
}

/// <summary>nDMaterial PlasticDamageConcretePlaneStress tag E nu ft fc beta Ap An Bn — plane-stress
/// plastic-damage бетон (Lee &amp; Fenves). E/Nu — упругие константы; Ft/Fc — ПОЛОЖИТЕЛЬНЫЕ
/// величины в Па (в отличие от Concrete01/02/04 в CScore.NativeMaterialMapper, где Fc
/// отрицательный — конвенция сжатия). Beta/Ap/An/Bn — калиброванные параметры модели
/// damage-plasticity, не выводятся из диаграммы СП63 напрямую (см. design doc).
/// НЕ совместим с LayeredShell напрямую — требует обёртки PlateFromPlaneStressShellMaterialSpec
/// (подтверждено вручную через реальный OpenSees.exe).</summary>
public sealed record PlasticDamageConcretePlaneStressShellMaterialSpec(
    double E, double Nu, double Ft, double Fc, double Beta, double Ap, double An, double Bn)
    : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "PlasticDamageConcretePlaneStress";

    /// <inheritdoc />
    public override string Fingerprint =>
        Hash(Kind, F(E), F(Nu), F(Ft), F(Fc), F(Beta), F(Ap), F(An), F(Bn));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        Validate(tag);
        return $"nDMaterial PlasticDamageConcretePlaneStress {tag} {F(E)} {F(Nu)} {F(Ft)} {F(Fc)} {F(Beta)} {F(Ap)} {F(An)} {F(Bn)}";
    }

    private void Validate(int tag)
    {
        if (tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (!double.IsFinite(E) || E <= 0)
            throw new InvalidOperationException("Модуль E бетона должен быть положительным и конечным.");
        if (!double.IsFinite(Nu) || Nu <= -1 || Nu >= 0.5)
            throw new InvalidOperationException("Коэффициент Пуассона бетона должен быть в интервале (-1; 0.5).");
        if (!double.IsFinite(Ft) || Ft <= 0)
            throw new InvalidOperationException("Ft бетона должен быть положительным и конечным (растяжение).");
        if (!double.IsFinite(Fc) || Fc <= 0)
            throw new InvalidOperationException("Fc бетона должен быть положительным и конечным (сжатие).");
        if (!double.IsFinite(Beta) || !double.IsFinite(Ap) || !double.IsFinite(An) || !double.IsFinite(Bn))
            throw new InvalidOperationException("Параметры damage-plasticity бетона должны быть конечными.");
    }
}

/// <summary>nDMaterial PlateFromPlaneStress tag baseTag outOfPlaneShearModulus — обязательная
/// обёртка plane-stress материала для использования в LayeredShell (добавляет поперечное
/// сдвиговое условие). BaseMaterialTag заполняется PlateSectionOpenSeesMapper после регистрации
/// зависимого материала — исходное значение в цепочке резолвера служит только локальным
/// идентификатором внутри цепочки.</summary>
public sealed record PlateFromPlaneStressShellMaterialSpec(int BaseMaterialTag, double OutOfPlaneShearModulus)
    : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "PlateFromPlaneStress";

    /// <inheritdoc />
    public override string Fingerprint =>
        Hash(Kind, BaseMaterialTag.ToString(CultureInfo.InvariantCulture), F(OutOfPlaneShearModulus));

    /// <inheritdoc />
    public override int? DependsOnMaterialTag => BaseMaterialTag;

    /// <inheritdoc />
    public override NativeShellMaterialSpec WithDependencyTag(int tag) =>
        this with { BaseMaterialTag = tag };

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        if (tag <= 0 || BaseMaterialTag <= 0)
            throw new InvalidOperationException("Tags PlateFromPlaneStress должны быть положительными.");
        if (!double.IsFinite(OutOfPlaneShearModulus) || OutOfPlaneShearModulus <= 0)
            throw new InvalidOperationException("Поперечный модуль сдвига должен быть положительным и конечным.");

        return $"nDMaterial PlateFromPlaneStress {tag} {BaseMaterialTag} {F(OutOfPlaneShearModulus)}";
    }
}

/// <summary>uniaxialMaterial Steel01 tag Fy E0 b — билинейная сталь, nonlinear backend для
/// PlateRebarShellMaterialSpec. Та же Tcl-команда, что и beam-fiber Steel01Spec, но отдельный
/// самодостаточный тип (NativeShellMaterialSpec не переиспользует beam-контракт напрямую — см.
/// родительскую спеку, "Native shell material capabilities").</summary>
public sealed record Steel01UniaxialShellMaterialSpec(double Fy, double E0, double B)
    : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "Steel01";

    /// <inheritdoc />
    public override string Fingerprint => Hash(Kind, F(Fy), F(E0), F(B));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        Validate(tag);
        return $"uniaxialMaterial Steel01 {tag} {F(Fy)} {F(E0)} {F(B)}";
    }

    private void Validate(int tag)
    {
        if (tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (!double.IsFinite(Fy) || Fy <= 0)
            throw new InvalidOperationException("Fy арматуры должен быть положительным и конечным.");
        if (!double.IsFinite(E0) || E0 <= 0)
            throw new InvalidOperationException("E0 арматуры должен быть положительным и конечным.");
        if (!double.IsFinite(B))
            throw new InvalidOperationException("Коэффициент упрочнения арматуры должен быть конечным.");
    }
}

/// <summary>uniaxialMaterial Steel02 tag Fy E0 b R0 cR1 cR2 — Giuffré-Menegotto-Pinto.</summary>
public sealed record Steel02UniaxialShellMaterialSpec(
    double Fy, double E0, double B, double R0, double CR1, double CR2) : NativeShellMaterialSpec
{
    /// <inheritdoc />
    public override string Kind => "Steel02";

    /// <inheritdoc />
    public override string Fingerprint => Hash(Kind, F(Fy), F(E0), F(B), F(R0), F(CR1), F(CR2));

    /// <inheritdoc />
    public override string ToTcl(int tag)
    {
        Validate(tag);
        return $"uniaxialMaterial Steel02 {tag} {F(Fy)} {F(E0)} {F(B)} {F(R0)} {F(CR1)} {F(CR2)}";
    }

    private void Validate(int tag)
    {
        if (tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (!double.IsFinite(Fy) || Fy <= 0)
            throw new InvalidOperationException("Fy арматуры должен быть положительным и конечным.");
        if (!double.IsFinite(E0) || E0 <= 0)
            throw new InvalidOperationException("E0 арматуры должен быть положительным и конечным.");
        if (!double.IsFinite(B) || !double.IsFinite(R0) || !double.IsFinite(CR1) || !double.IsFinite(CR2))
            throw new InvalidOperationException("Параметры Giuffré-Menegotto-Pinto должны быть конечными.");
    }
}

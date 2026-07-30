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

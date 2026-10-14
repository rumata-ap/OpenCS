namespace OpenCS.ExternalTools;

/// <summary>
/// Внешняя программа, исполняемая через Python-пакет. Записывается в строку «пути к программе»
/// (<c>python:opensees|/usr/bin/python3</c>), чтобы пройти через существующие параметры
/// <c>ExecutablePath</c> без смены сигнатур; разбирает её только запускатель своей программы.
/// Строка видна и в журнале/манифестах артефактов как источник расчёта.
/// </summary>
public sealed record PythonToolCommand(string Tool, string PythonPath)
{
    const string Prefix = "python:";

    public override string ToString() => $"{Prefix}{Tool}|{PythonPath}";

    public static bool TryParse(string? value, out PythonToolCommand command)
    {
        command = null!;
        if (value == null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int bar = value.IndexOf('|', Prefix.Length);
        if (bar <= Prefix.Length || bar == value.Length - 1) return false;
        command = new PythonToolCommand(value[Prefix.Length..bar], value[(bar + 1)..]);
        return true;
    }
}

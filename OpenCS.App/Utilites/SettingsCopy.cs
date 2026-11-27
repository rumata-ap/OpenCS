using System.Text.Json;

namespace OpenCS.Utilites;

/// <summary>
/// Глубокая копия настроек тем же сериализатором, которым они пишутся в базу: новое поле класса копируется без
/// правки Clone (ручной Clone терял <c>CalcSettings.ImportedBarRebarTolerancePercent</c>).
/// </summary>
public static class SettingsCopy
{
    public static T Deep<T>(T source) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source))
        ?? throw new InvalidOperationException(typeof(T).Name);
}

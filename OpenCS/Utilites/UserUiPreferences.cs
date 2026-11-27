using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCS.Utilites;

/// <summary>
/// Настройки пользователя вне проекта (сейчас — язык интерфейса): файл <c>%APPDATA%\OpenCS\ui.json</c>. У одного
/// человека проекты разные, а язык один, поэтому в базу проекта они не пишутся. Ошибки чтения и записи не мешают
/// работе: при отказе берётся язык по умолчанию.
/// </summary>
public sealed class UserUiPreferences
{
    /// <summary>Язык: «ru-RU» или «en-US»; null — по умолчанию (русский).</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenCS", "ui.json");

    public static UserUiPreferences Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<UserUiPreferences>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    /// <summary>Записывает файл; false — не удалось (папка недоступна).</summary>
    public bool Save(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Номер языка меню «Вид → Язык»: 0 — русский, 1 — английский.</summary>
    public int LanguageIndex => Language == "en-US" ? 1 : 0;
}

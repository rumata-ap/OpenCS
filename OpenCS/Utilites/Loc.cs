using System.Collections.Frozen;
using System.IO;

namespace OpenCS.Utilites
{
    /// <summary>
    /// Строки интерфейса и сообщений по ключу. Без WPF: таблицы строятся из вложенных в сборку словарей
    /// <c>Strings.&lt;язык&gt;.xaml</c> (тех же, что подключает App.xaml). Пока язык не задан, возвращает ключ —
    /// так работают тесты; язык задаёт хост (WPF при старте и при переключении, консоль — по --lang).
    /// </summary>
    public static class Loc
    {
        public const string Russian = "ru-RU";
        public const string English = "en-US";

        /// <summary>Поддерживаемые языки.</summary>
        public static IReadOnlyList<string> Languages { get; } = [Russian, English];

        static readonly FrozenDictionary<string, Lazy<FrozenDictionary<string, string>>> s_tables =
            Languages.ToFrozenDictionary(l => l, l => new Lazy<FrozenDictionary<string, string>>(() => LoadTable(l)));

        sealed record State(string Language, FrozenDictionary<string, string> Table);

        static State? s_state;

        /// <summary>Текущий язык; null — не задан (<see cref="S(string)"/> возвращает ключ).</summary>
        public static string? Language => Volatile.Read(ref s_state)?.Language;

        /// <summary>Переключает язык; null — снова возвращать ключи. Потокобезопасно.</summary>
        /// <exception cref="ArgumentException">Неизвестный язык.</exception>
        public static void SetLanguage(string? language)
        {
            if (language == null)
            {
                Volatile.Write(ref s_state, null);
                return;
            }
            if (!s_tables.TryGetValue(language, out var table))
                throw new ArgumentException($"Неизвестный язык «{language}»; допустимы: {string.Join(", ", Languages)}.",
                    nameof(language));
            Volatile.Write(ref s_state, new State(language, table.Value));
        }

        /// <summary>Строка по ключу; нет языка или ключа — сам ключ.</summary>
        public static string S(string key) => TryGet(key, out var value) ? value : key;

        /// <summary>Строка по ключу; нет языка или ключа — <paramref name="fallback"/>.</summary>
        public static string S(string key, string fallback) => TryGet(key, out var value) ? value : fallback;

        public static bool TryGet(string key, out string value)
        {
            if (Volatile.Read(ref s_state)?.Table.TryGetValue(key, out var found) == true)
            {
                value = found;
                return true;
            }
            value = key;
            return false;
        }

        /// <summary>Полная таблица языка (для тестов и проверок словарей).</summary>
        internal static FrozenDictionary<string, string> Table(string language) => s_tables[language].Value;

        static FrozenDictionary<string, string> LoadTable(string language)
        {
            string name = $"OpenCS.Strings.{language}.xaml";
            using Stream stream = typeof(Loc).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"В сборке нет словаря строк «{name}».");
            return XamlStringDictionaryReader.Read(stream, name).ToFrozenDictionary(StringComparer.Ordinal);
        }
    }
}

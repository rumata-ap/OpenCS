using System.IO;
using System.Text;
using System.Xml;

namespace OpenCS.Utilites
{
    /// <summary>
    /// Читает словарь строк WPF (<c>ResourceDictionary</c> из элементов <c>system:String</c> с <c>x:Key</c>)
    /// без WPF. Пробелы нормализуются так же, как это делает XAML: без <c>xml:space="preserve"</c> переводы
    /// строк и табуляции (в том числе из <c>&amp;#x0a;</c>) становятся пробелом, серии пробелов схлопываются,
    /// края обрезаются. Совпадение со словарём, загруженным WPF, проверяет тест эквивалентности.
    /// </summary>
    internal static class XamlStringDictionaryReader
    {
        const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>Возвращает пары «ключ → значение»; ключи сравниваются ordinal (<c>OK</c> и <c>Ok</c> различны).</summary>
        /// <exception cref="InvalidDataException">Чужой элемент, вложенные элементы, нет <c>x:Key</c> или повтор ключа.</exception>
        public static Dictionary<string, string> Read(Stream stream, string sourceName)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            using var reader = XmlReader.Create(stream, settings);
            var lineInfo = reader as IXmlLineInfo;

            reader.MoveToContent();
            if (reader.LocalName != "ResourceDictionary")
                throw Error(sourceName, lineInfo, $"корень «{reader.LocalName}» вместо ResourceDictionary");
            if (reader.IsEmptyElement)
                return result;

            reader.Read();
            while (true)
            {
                reader.MoveToContent();
                if (reader.NodeType == XmlNodeType.EndElement)
                    break;
                if (reader.NodeType != XmlNodeType.Element)
                    throw Error(sourceName, lineInfo, $"неожиданный узел {reader.NodeType}");

                int line = lineInfo?.LineNumber ?? 0;
                if (reader.LocalName != "String" || !reader.NamespaceURI.StartsWith("clr-namespace:System", StringComparison.Ordinal))
                    throw Error(sourceName, lineInfo, $"элемент «{reader.Name}» — в словаре допустимы только system:String");

                string? key = reader.GetAttribute("Key", XamlNamespace);
                if (string.IsNullOrEmpty(key))
                    throw Error(sourceName, lineInfo, "элемент без x:Key");

                bool preserve = reader.XmlSpace == XmlSpace.Preserve;
                string text;
                try
                {
                    text = reader.ReadElementContentAsString();
                }
                catch (XmlException ex)
                {
                    throw new InvalidDataException($"{sourceName}, строка {line}: значение «{key}» — {ex.Message}", ex);
                }

                if (!result.TryAdd(key, preserve ? text : Normalize(text)))
                    throw new InvalidDataException($"{sourceName}, строка {line}: повтор ключа «{key}»");
            }
            return result;
        }

        /// <summary>Нормализация пробелов XAML для содержимого без <c>xml:space="preserve"</c>.</summary>
        internal static string Normalize(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (char c in text)
            {
                if (c is ' ' or '\n' or '\r' or '\t')
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static InvalidDataException Error(string sourceName, IXmlLineInfo? lineInfo, string message) =>
            new($"{sourceName}, строка {lineInfo?.LineNumber ?? 0}: {message}");
    }
}

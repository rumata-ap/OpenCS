using System.Collections.ObjectModel;

namespace OpenCS.Reporting;

/// <summary>Базовый блок нейтрального документа отчёта.</summary>
public abstract record ReportBlock;

/// <summary>Заголовок раздела отчёта.</summary>
public sealed record ReportHeading(int Level, string Text) : ReportBlock;

/// <summary>Абзац обычного текста.</summary>
public sealed record ReportParagraph(string Text) : ReportBlock;

/// <summary>Таблица «ключ-значение»; подписи колонок задаёт провайдер, не рендерер.</summary>
public sealed record ReportKeyValueTable(
    IReadOnlyList<(string Key, string Value)> Rows,
    string KeyHeader,
    string ValueHeader) : ReportBlock;

/// <summary>Таблица с заголовками столбцов.</summary>
public sealed record ReportTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows) : ReportBlock;

/// <summary>Формула СП с подстановкой и вычисленным результатом.</summary>
public sealed record ReportFormula(
    string Reference,
    string Formula,
    string Substitution,
    string Result) : ReportBlock;

/// <summary>Встроированная SVG-иллюстрация отчёта.</summary>
public sealed record ReportImage(string Name, string Svg) : ReportBlock;

/// <summary>Предупреждение или диагностическое сообщение.</summary>
public sealed record ReportWarning(string Text) : ReportBlock;

/// <summary>Принудительный разрыв страницы.</summary>
public sealed record ReportPageBreak : ReportBlock;

/// <summary>Нейтральное представление отчёта, не зависящее от HTML, DOCX или PDF.</summary>
public sealed class ReportDocument
{
    /// <summary>Заголовок документа.</summary>
    public string Title { get; }

    /// <summary>Последовательность блоков документа.</summary>
    public IList<ReportBlock> Blocks { get; } = new ObservableCollection<ReportBlock>();

    /// <summary>Создаёт пустой документ с заданным заголовком.</summary>
    public ReportDocument(string title)
    {
        Title = title;
    }

    /// <summary>Добавляет блок и возвращает этот же документ для цепочки вызовов.</summary>
    public ReportDocument Add(ReportBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Blocks.Add(block);
        return this;
    }
}

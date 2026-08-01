namespace OpenCS.ViewModels;

public enum SectionCutExportFormat
{
    Png,
    Svg,
    Dxf
}

/// <summary>Результат диалога экспорта эпюры разреза.</summary>
public sealed record SectionCutExportOptions(SectionCutExportFormat Format, bool AsOnScreen);

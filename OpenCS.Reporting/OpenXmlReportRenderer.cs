using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace OpenCS.Reporting;

/// <summary>Рендерер нейтрального документа в DOCX средствами OpenXML SDK.
/// SVG-иллюстрации растеризуются во внешнем <see cref="ISvgRasterizer"/> лениво —
/// документ без SVG не требует растеризатора вовсе.</summary>
public sealed class OpenXmlReportRenderer
{
    const int EmuPerPixel = 9525;                 // 96 dpi: 914400 EMU/дюйм / 96
    const string AccentShading = "EAF2F9";
    const string WarningShading = "FFF7DF";

    /// <summary>Собирает DOCX-пакет и возвращает его байты.</summary>
    public async Task<byte[]> RenderAsync(ReportDocument document, ISvgRasterizer? rasterizer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var buffer = new MemoryStream();
        using (var package = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            package.PackageProperties.Title = document.Title;
            var main = package.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            body.AppendChild(Heading(document.Title, 1));
            foreach (var block in document.Blocks)
            {
                ct.ThrowIfCancellationRequested();
                await AppendBlockAsync(main, body, block, rasterizer, ct).ConfigureAwait(false);
            }

            body.AppendChild(SectionProperties());
            main.Document.Save();
        }
        return buffer.ToArray();
    }

    static async Task AppendBlockAsync(MainDocumentPart main, Body body, ReportBlock block,
        ISvgRasterizer? rasterizer, CancellationToken ct)
    {
        switch (block)
        {
            case ReportHeading heading:
                body.AppendChild(Heading(heading.Text, heading.Level));
                break;

            case ReportParagraph paragraph:
                body.AppendChild(new Paragraph(TextRun(paragraph.Text)));
                break;

            case ReportKeyValueTable table:
                body.AppendChild(BuildTable([table.KeyHeader, table.ValueHeader],
                    table.Rows.Select(r => (IReadOnlyList<string>)new[] { r.Key, r.Value }).ToList()));
                break;

            case ReportTable table:
                body.AppendChild(BuildTable(table.Headers, table.Rows));
                break;

            case ReportFormula formula:
                body.AppendChild(FormulaParagraph(new Paragraph(TextRun(formula.Reference))));
                body.AppendChild(FormulaParagraph(new Paragraph(FormulaRuns(formula.Formula))));
                body.AppendChild(FormulaParagraph(new Paragraph(FormulaRuns(formula.Substitution))));
                body.AppendChild(FormulaParagraph(new Paragraph(FormulaRuns(formula.Result))));
                break;

            case ReportImage image when SvgSizing.LooksLikeSvg(image.Svg):
            {
                var size = SvgSizing.ScaleToMaxWidth(SvgSizing.Resolve(image.Svg));
                if (rasterizer == null)
                    throw new InvalidOperationException(
                        "Документ содержит SVG-иллюстрацию, но растеризатор не передан.");

                byte[] png = await rasterizer
                    .RasterizeAsync(SvgSizing.EnsureExplicitDimensions(image.Svg), ct)
                    .ConfigureAwait(false);

                var part = main.AddImagePart(ImagePartType.Png);
                using (var stream = new MemoryStream(png, writable: false))
                    part.FeedData(stream);

                body.AppendChild(new Paragraph(ImageRun(main.GetIdOfPart(part), image.Name, size)));
                body.AppendChild(CaptionParagraph(image.Name));
                break;
            }

            case ReportImage image:
                body.AppendChild(new Paragraph(MonospaceRun(image.Svg)));
                body.AppendChild(CaptionParagraph(image.Name));
                break;

            case ReportWarning warning:
                body.AppendChild(WarningParagraph(warning.Text));
                break;

            case ReportPageBreak:
                body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                break;
        }
    }

    // 25/20/16 px HTML → pt (×0.75) → half-points (×2): h1=38, h2=30, h3=24;
    // ниже третьего уровня — шаг −6 с нижней границей 20.
    static Paragraph Heading(string text, int level)
    {
        int size = Math.Clamp(level, 1, 6) switch
        {
            1 => 38,
            2 => 30,
            3 => 24,
            var deep => Math.Max(20, 24 - 6 * (deep - 3))
        };
        var run = new Run(new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve })
        {
            RunProperties = new RunProperties(new Bold(),
                new FontSize { Val = size.ToString(System.Globalization.CultureInfo.InvariantCulture) })
        };
        return new Paragraph(run);
    }

    static Run TextRun(string? text)
        => new(new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve });

    static Run MonospaceRun(string? text)
        => new(new Text(text ?? "") { Space = SpaceProcessingModeValues.Preserve })
        {
            RunProperties = new RunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" })
        };

    static IEnumerable<Run> FormulaRuns(string? value)
    {
        foreach (var segment in FormulaMarkup.Parse(value))
        {
            var run = TextRun(segment.Text);
            if (segment.Kind == FormulaSegmentKind.Subscript)
                run.RunProperties = new RunProperties(
                    new VerticalTextAlignment { Val = VerticalPositionValues.Subscript });
            else if (segment.Kind == FormulaSegmentKind.Superscript)
                run.RunProperties = new RunProperties(
                    new VerticalTextAlignment { Val = VerticalPositionValues.Superscript });
            yield return run;
        }
    }

    static Paragraph FormulaParagraph(Paragraph paragraph)
    {
        paragraph.ParagraphProperties = new ParagraphProperties(
            new ParagraphBorders(new LeftBorder
            {
                Val = BorderValues.Single, Color = "1769AA", Size = 18U, Space = 4U
            }),
            new Shading { Val = ShadingPatternValues.Clear, Fill = "F5F8FB" });
        return paragraph;
    }

    static Paragraph CaptionParagraph(string? name)
    {
        var run = TextRun(name);
        run.RunProperties = new RunProperties(new Italic(),
            new Color { Val = "64748B" }, new FontSize { Val = "18" });
        return new Paragraph(run);
    }

    static Paragraph WarningParagraph(string? text)
    {
        var paragraph = new Paragraph(TextRun(text))
        {
            ParagraphProperties = new ParagraphProperties(
                new ParagraphBorders(new LeftBorder
                {
                    Val = BorderValues.Single, Color = "F2C36B", Size = 18U, Space = 4U
                }),
                new Shading { Val = ShadingPatternValues.Clear, Fill = WarningShading })
        };
        return paragraph;
    }

    static Table BuildTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var borders = new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" },
            new BottomBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" },
            new LeftBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" },
            new RightBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E1EA" });

        var table = new Table(new TableProperties(borders,
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }));

        var headerRow = new TableRow();
        foreach (var header in headers)
        {
            var run = TextRun(header);
            run.RunProperties = new RunProperties(new Bold());
            headerRow.AppendChild(new TableCell(
                new TableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Fill = AccentShading }),
                new Paragraph(run)));
        }
        table.AppendChild(headerRow);

        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row)
                tableRow.AppendChild(new TableCell(new Paragraph(TextRun(cell))));
            table.AppendChild(tableRow);
        }
        return table;
    }

    static Run ImageRun(string relationshipId, string name, SvgSizing.Size size)
    {
        long cx = Math.Max(1, (long)Math.Round(size.Width)) * EmuPerPixel;
        long cy = Math.Max(1, (long)Math.Round(size.Height)) * EmuPerPixel;
        string safeName = string.IsNullOrWhiteSpace(name) ? "image" : name;

        var drawing = new Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = 1U, Name = safeName },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(new A.GraphicData(
                new PIC.Picture(
                    new PIC.NonVisualPictureProperties(
                        new PIC.NonVisualDrawingProperties { Id = 0U, Name = safeName + ".png" },
                        new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(
                        new A.Blip { Embed = relationshipId },
                        new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(
                        new A.Transform2D(
                            new A.Offset { X = 0L, Y = 0L },
                            new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                )
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            // Поля обтекания принадлежат DW.Inline, а не Drawing.
            DistanceFromTop = 0U, DistanceFromBottom = 0U,
            DistanceFromLeft = 0U, DistanceFromRight = 0U
        });

        return new Run(drawing);
    }

    static SectionProperties SectionProperties()
        => new(
            new PageSize { Width = 11906U, Height = 16838U, Orient = PageOrientationValues.Portrait },
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1134U, Right = 1134U, Header = 0U, Footer = 0U, Gutter = 0U });
}

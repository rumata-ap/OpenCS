using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views.Dialogs;

/// <summary>Шаблон контура «Rect»: размеры в мм, проверка — <see cref="ContourAuthoring.ValidateTemplate"/>.</summary>
public partial class TemplateRectDialog : Window
{
    /// <summary>Шаблон, который строит диалог.</summary>
    public ContourTemplate Kind => ContourTemplate.Rect;

    /// <summary>Обозначение нового контура (пусто — подпись шаблона).</summary>
    public string ContourName => TemplateDialogInput.NameOr(NameBox, "TemplateRect");

    /// <summary>Проверенные размеры, м (после «OK»).</summary>
    public ContourTemplateSize Size { get; private set; } = null!;

    public TemplateRectDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        NameBox.Text = Loc.S("TemplateRect");
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        TemplateDialogInput.Field[] fields =
        [
            new("width", WidthBox, "TplWidthMm"),
            new("height", HeightBox, "TplHeightMm")
        ];
        if (!TemplateDialogInput.TryRead(this, Kind, fields, 0, out var size)) return;
        Size = size;
        DialogResult = true;
    }
}

using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views.Dialogs;

/// <summary>Шаблон контура «IBeam»: размеры в мм, проверка — <see cref="ContourAuthoring.ValidateTemplate"/>.</summary>
public partial class TemplateIBeamDialog : Window
{
    /// <summary>Шаблон, который строит диалог.</summary>
    public ContourTemplate Kind => ContourTemplate.IBeam;

    /// <summary>Обозначение нового контура (пусто — подпись шаблона).</summary>
    public string ContourName => TemplateDialogInput.NameOr(NameBox, "TemplateIBeam");

    /// <summary>Проверенные размеры, м (после «OK»).</summary>
    public ContourTemplateSize Size { get; private set; } = null!;

    public TemplateIBeamDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        NameBox.Text = Loc.S("TemplateIBeam");
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        TemplateDialogInput.Field[] fields =
        [
            new("height", HeightBox, "TplHeightMm"),
            new("width", WidthBox, "TplFlangesWidthMm"),
            new("tw", TwBox, "TplWebThicknessMm"),
            new("tf", TfBox, "TplFlangeThicknessMm")
        ];
        if (!TemplateDialogInput.TryRead(this, Kind, fields, 0, out var size)) return;
        Size = size;
        DialogResult = true;
    }
}

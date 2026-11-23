using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views.Dialogs;

/// <summary>Шаблон контура «Angle»: размеры в мм, проверка — <see cref="ContourAuthoring.ValidateTemplate"/>.</summary>
public partial class TemplateAngleDialog : Window
{
    /// <summary>Шаблон, который строит диалог.</summary>
    public ContourTemplate Kind => ContourTemplate.Angle;

    /// <summary>Обозначение нового контура (пусто — подпись шаблона).</summary>
    public string ContourName => TemplateDialogInput.NameOr(NameBox, "TemplateAngle");

    /// <summary>Проверенные размеры, м (после «OK»).</summary>
    public ContourTemplateSize Size { get; private set; } = null!;

    public TemplateAngleDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        NameBox.Text = Loc.S("TemplateAngle");
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        TemplateDialogInput.Field[] fields =
        [
            new("width", WidthBox, "TplWidthMm"),
            new("height", HeightBox, "TplHeightMm"),
            new("tw", TwBox, "TplVertLegThicknessMm"),
            new("tf", TfBox, "TplHorizLegThicknessMm")
        ];
        if (!TemplateDialogInput.TryRead(this, Kind, fields, 0, out var size)) return;
        Size = size;
        DialogResult = true;
    }
}

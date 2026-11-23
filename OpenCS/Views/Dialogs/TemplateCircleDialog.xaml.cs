using OpenCS.Services;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views.Dialogs;

/// <summary>Шаблон контура «Circle»: размеры в мм, проверка — <see cref="ContourAuthoring.ValidateTemplate"/>.</summary>
public partial class TemplateCircleDialog : Window
{
    /// <summary>Шаблон, который строит диалог.</summary>
    public ContourTemplate Kind => ContourTemplate.Circle;

    /// <summary>Обозначение нового контура (пусто — подпись шаблона).</summary>
    public string ContourName => TemplateDialogInput.NameOr(NameBox, "TemplateCircle");

    /// <summary>Проверенные размеры, м (после «OK»).</summary>
    public ContourTemplateSize Size { get; private set; } = null!;

    public TemplateCircleDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        NameBox.Text = Loc.S("TemplateCircle");
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        TemplateDialogInput.Field[] fields =
        [
            new("d", DiameterBox, "TplDiameterMm")
        ];
        if (!TemplateDialogInput.TryRead(this, Kind, fields, int.TryParse(SegmentsBox.Text, out var n) ? n : 0, out var size)) return;
        Size = size;
        DialogResult = true;
    }
}

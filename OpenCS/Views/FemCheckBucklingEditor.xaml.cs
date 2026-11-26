using System.Windows;
using System.Windows.Controls;
using OpenCS.Views.Helpers;

namespace OpenCS.Views;

/// <summary>Блок «Продольный изгиб» проверки по КЭ; DataContext — <see cref="ViewModels.FemCheckBucklingVM"/>.</summary>
public partial class FemCheckBucklingEditor : UserControl
{
    public FemCheckBucklingEditor() => InitializeComponent();

    /// <summary>
    /// Числа видимых и доступных полей блока (<see cref="FieldCheck"/>): нераспознанное или вне правила — рамка,
    /// сообщение с подписью поля, фокус. false — ошибка показана, записывать нельзя.
    /// </summary>
    public bool CheckFields(Window owner)
    {
        FieldCheck.Clear(EtaLengthBox, EtaMuXBox, EtaMuYBox, EtaPsiXBox, EtaPsiYBox, SteelMuXBox, SteelMuYBox, SteelMuBBox);
        static bool Active(TextBox b) => b.IsVisible && b.IsEnabled;
        bool Need(TextBox b, string label, Func<double, string?> rule) => !Active(b) || FieldCheck.Read(owner, b, label, out _, rule);
        if (Active(EtaLengthBox) && !FieldCheck.ReadOptional(owner, EtaLengthBox, "FemCheckEtaLength", out _, FieldCheck.Positive))
            return false;
        return Need(EtaMuXBox, "EtaMuxLabel", FieldCheck.Positive)
            && Need(EtaMuYBox, "EtaMuyLabel", FieldCheck.Positive)
            && Need(EtaPsiXBox, "EtaPsixLabel", FieldCheck.NonNegative)
            && Need(EtaPsiYBox, "EtaPsiyLabel", FieldCheck.NonNegative)
            && Need(SteelMuXBox, "EtaMuxLabel", FieldCheck.Positive)
            && Need(SteelMuYBox, "EtaMuyLabel", FieldCheck.Positive)
            && Need(SteelMuBBox, "Sp16MeshMuBShort", FieldCheck.NonNegative);
    }
}

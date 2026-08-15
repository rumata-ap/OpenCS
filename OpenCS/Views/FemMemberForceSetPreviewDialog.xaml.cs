using System.Windows;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог проверки и выбора источника усилий для mesh-узлов стержня.</summary>
public partial class FemMemberForceSetPreviewDialog : Window
{
    readonly FemMemberForceSetPreviewVM _vm;

    /// <summary>Подтверждённый выбор; null до нажатия кнопки сохранения.</summary>
    public FemMemberForceSetSelection? Result { get; private set; }

    /// <summary>Создаёт окно preview.</summary>
    public FemMemberForceSetPreviewDialog(FemMemberForceSetPreview preview)
    {
        InitializeComponent();
        _vm = new FemMemberForceSetPreviewVM(
            preview,
            string.Format(Loc.S("FemForceSetDefaultTag"), preview.MemberTag, preview.StepLabel),
            string.Format(Loc.S("FemForceSetDefaultDescription"), preview.SchemaTag, preview.MemberTag, preview.StepLabel));
        DataContext = _vm;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanSave) return;
        Result = _vm.BuildSelection();
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

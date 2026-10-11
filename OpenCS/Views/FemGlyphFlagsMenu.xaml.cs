using System.Windows;
using System.Windows.Controls;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Меню флагов рисования глифов 3D-вида схемы с пунктами «Показать всё» и «Скрыть всё».</summary>
public partial class FemGlyphFlagsMenu : UserControl
{
    public FemGlyphFlagsMenu() => InitializeComponent();

    void ShowAll_Click(object sender, RoutedEventArgs e) => (DataContext as Fem3DVM)?.SetAllGlyphs(true);

    void HideAll_Click(object sender, RoutedEventArgs e) => (DataContext as Fem3DVM)?.SetAllGlyphs(false);
}

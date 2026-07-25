using System.Windows;

namespace OpenCS.Views.Dialogs;

public partial class TextInputDialog : Window
{
    public string Value { get; private set; } = "";

    public TextInputDialog(string title, string label, string defaultValue = "")
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = defaultValue;
        ValueBox.Focus();
        ValueBox.SelectAll();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        string trimmed = ValueBox.Text.Trim();
        if (trimmed.Length == 0)
        {
            MessageBox.Show(Utilites.Loc.S("FemRenameInvalid"), Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ValueBox.Focus();
            return;
        }
        Value = trimmed;
        DialogResult = true;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem.Fragment;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>«Фрагмент по отметкам…»: отметки z₁ &lt; z₂ (середины этажей ниже и выше перекрытия) и имя новой схемы.
/// Имя по умолчанию следует за отметками, пока пользователь не ввёл своё.</summary>
public partial class StoreyFragmentDialog : Window
{
    readonly string _parentTag;
    bool _nameEdited, _settingName;

    /// <summary>Запрос сборки после «Собрать».</summary>
    public StoreyFragmentRequest? Request { get; private set; }

    /// <param name="zRange">Диапазон z узлов сетки родителя (подсказка); null — у родителя нет сетки.</param>
    public StoreyFragmentDialog(string parentTag, (double Min, double Max)? zRange)
    {
        InitializeComponent();
        _parentTag = parentTag;
        schemaText.Text = parentTag;
        string F(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);
        rangeText.Text = zRange is { } r
            ? string.Format(Loc.S("StoreyFragmentMeshRange"), F(r.Min), F(r.Max))
            : Loc.S("StoreyFragmentNoMesh");
        Loaded += (_, _) => z1Box.Focus();
    }

    void Level_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_nameEdited || nameBox is null) return;
        if (!Pars.ParseAny(z1Box.Text.Trim(), out var z1) || !Pars.ParseAny(z2Box.Text.Trim(), out var z2)) return;
        _settingName = true;
        nameBox.Text = StoreyFragmentForm.DefaultName(_parentTag, z1, z2, CultureInfo.CurrentCulture);
        _settingName = false;
    }

    void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_settingName) _nameEdited = nameBox.Text.Length > 0;
    }

    /// <summary>Запрос из полей; ошибка — текст в <paramref name="error"/>, null.</summary>
    internal StoreyFragmentRequest? ReadRequest(out string? error) =>
        StoreyFragmentForm.TryRead(z1Box.Text, z2Box.Text, nameBox.Text, out error);

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ReadRequest(out var error) is not { } request)
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Request = request;
        DialogResult = true;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог задания распределённой нагрузки выбранным стержням 3D-схемы.</summary>
public partial class FemMemberLoadDialog : Window
{
    readonly IReadOnlyList<FemMember> _members;
    readonly FemSchemaEditorVM _editor;
    bool _initializing = true;

    public FemMemberLoadDialog(IReadOnlyList<FemMember> members, FemSchemaEditorVM editor)
    {
        InitializeComponent();
        _members = members;
        _editor = editor;
        selectionText.Text = string.Format(Loc.S("FemLoadSelectedCount"), members.Count);
        loadCaseCombo.ItemsSource = editor.Session.LoadCases;
        loadCaseCombo.SelectedItem = editor.SelectedLoadCase ?? editor.Session.LoadCases.FirstOrDefault();
        coordinateCombo.SelectedValue = "local";
        distributionCombo.SelectedValue = "uniform";
        LoadFields();
        _initializing = false;
    }

    void LoadCaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) LoadFields();
    }

    void LoadFields()
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase) return;
        var load = _members.Select(member => _editor.Session.MemberLoads.FirstOrDefault(item =>
                item.LoadCaseId == loadCase.Id && item.MemberId == member.Id))
            .FirstOrDefault(item => item != null);
        coordinateCombo.SelectedValue = load?.CoordinateSystem ?? "local";
        distributionCombo.SelectedValue = load?.DistributionType ?? "uniform";
        startOffsetBox.Text = Format(load?.StartOffsetM ?? 0);
        endOffsetBox.Text = Format(load?.EndOffsetM ?? 0);
        qxStartBox.Text = Format(load?.QxStart ?? 0);
        qyStartBox.Text = Format(load?.QyStart ?? 0);
        qzStartBox.Text = Format(load?.QzStart ?? 0);
        qxEndBox.Text = Format(load?.QxEnd ?? 0);
        qyEndBox.Text = Format(load?.QyEnd ?? 0);
        qzEndBox.Text = Format(load?.QzEnd ?? 0);
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase) return;
        double Parse(TextBox box) => double.TryParse(box.Text, NumberStyles.Float,
            CultureInfo.CurrentCulture, out var value) ? value : 0;
        string coordinate = coordinateCombo.SelectedValue as string ?? "local";
        string distribution = distributionCombo.SelectedValue as string ?? "uniform";
        double qxStart = Parse(qxStartBox), qyStart = Parse(qyStartBox), qzStart = Parse(qzStartBox);
        double qxEnd = distribution == "uniform" ? qxStart : Parse(qxEndBox);
        double qyEnd = distribution == "uniform" ? qyStart : Parse(qyEndBox);
        double qzEnd = distribution == "uniform" ? qzStart : Parse(qzEndBox);
        int applied = 0;

        foreach (var member in _members.Where(member => member.Id != 0))
        {
            var old = _editor.Session.MemberLoads.FirstOrDefault(load =>
                load.LoadCaseId == loadCase.Id && load.MemberId == member.Id);
            _editor.Session.Execute(new SetMemberLoadCommand(new FemMemberLoad
            {
                Id = old?.Id ?? 0,
                SchemaId = _editor.Session.Schema.Id,
                LoadCaseId = loadCase.Id,
                MemberId = member.Id,
                CoordinateSystem = coordinate,
                DistributionType = distribution,
                StartOffsetM = Parse(startOffsetBox),
                EndOffsetM = Parse(endOffsetBox),
                QxStart = qxStart, QyStart = qyStart, QzStart = qzStart,
                QxEnd = qxEnd, QyEnd = qyEnd, QzEnd = qzEnd
            }));
            applied++;
        }
        _editor.RefreshCollections();
        if (applied < _members.Count)
            MessageBox.Show(Loc.S("FemMemberLoadSkippedUnsaved"), Loc.S("FemMemberLoadToolTip"),
                MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static string Format(double value) => value.ToString("G15", CultureInfo.CurrentCulture);
}

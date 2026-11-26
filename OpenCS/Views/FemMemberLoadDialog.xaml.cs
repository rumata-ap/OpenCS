using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CScore.Fem;
using CScore.Fem.Editing;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Views;

/// <summary>Диалог задания распределённой или сосредоточенной нагрузки выбранным стержням 3D-схемы.</summary>
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
        // Без загружений нагрузку задать некуда — сказать об этом сразу, а не молчать на «Применить».
        noLoadCasesText.Visibility = editor.Session.LoadCases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        loadCaseCombo.SelectedItem = editor.SelectedLoadCase ?? editor.Session.LoadCases.FirstOrDefault();
        coordinateCombo.SelectedValue = "local";
        distributionCombo.SelectedValue = "uniform";
        LoadFields();
        UpdateVisibility();
        _initializing = false;
    }

    // Окно добавляет новую нагрузку: смена загружения поля не перечитывает (имеющиеся нагрузки — в таблице «На стержни»).
    void LoadCaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    void DistributionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdateVisibility();
    }

    bool IsPoint => (distributionCombo.SelectedValue as string) == "point";

    void UpdateVisibility()
    {
        var pointVisibility = IsPoint ? Visibility.Visible : Visibility.Collapsed;
        var nonPointVisibility = IsPoint ? Visibility.Collapsed : Visibility.Visible;
        endOffsetPanel.Visibility = nonPointVisibility;
        endHeaderText.Visibility = nonPointVisibility;
        qxEndBox.Visibility = nonPointVisibility;
        qyEndBox.Visibility = nonPointVisibility;
        qzEndBox.Visibility = nonPointVisibility;
        momentGrid.Visibility = pointVisibility;
        // Сосредоточенная — сила P, кН; распределённая — интенсивность q, кН/м.
        qxLabel.SetResourceReference(TextBlock.TextProperty, IsPoint ? "FemMemberLoadPx" : "FemMemberLoadQx");
        qyLabel.SetResourceReference(TextBlock.TextProperty, IsPoint ? "FemMemberLoadPy" : "FemMemberLoadQy");
        qzLabel.SetResourceReference(TextBlock.TextProperty, IsPoint ? "FemMemberLoadPz" : "FemMemberLoadQz");
        unitsHintUniform.Visibility = nonPointVisibility;
        unitsHintPoint.Visibility = pointVisibility;
    }

    void LoadFields()
    {
        // Пустые поля: окно добавляет нагрузку, а не правит первую имеющуюся (раньше «Применить» заменяло её — у стержня
        // с несколькими нагрузками загружения пропадала одна из них).
        FemMemberLoad? load = null;
        coordinateCombo.SelectedValue = load?.CoordinateSystem ?? "local";
        distributionCombo.SelectedValue = load?.DistributionType ?? "uniform";
        startOffsetBox.Text = Format(load?.StartOffsetM ?? 0);
        endOffsetBox.Text = Format(load?.EndOffsetM ?? 0);
        qxStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QxStart ?? 0));
        qyStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QyStart ?? 0));
        qzStartBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QzStart ?? 0));
        qxEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QxEnd ?? 0));
        qyEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QyEnd ?? 0));
        qzEndBox.Text = Format(FemUnitConverter.NewtonsToKiloNewtons(load?.QzEnd ?? 0));
        mxBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mx ?? 0));
        myBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.My ?? 0));
        mzBox.Text = Format(FemUnitConverter.NewtonMetersToKiloNewtonMeters(load?.Mz ?? 0));
        UpdateVisibility();
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (loadCaseCombo.SelectedItem is not FemLoadCase loadCase)
        {
            MessageBox.Show(this, Loc.S("FemNoLoadCasesHint"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string coordinate = coordinateCombo.SelectedValue as string ?? "local";
        string distribution = distributionCombo.SelectedValue as string ?? "uniform";
        bool isPoint = distribution == "point";
        bool trapezoid = !isPoint && distribution != "uniform";
        // Проверяются только поля, участвующие в выбранном виде; нераспознанное — ошибка у поля, а не молчаливый 0.
        Helpers.FieldCheck.Clear(qxStartBox, qyStartBox, qzStartBox, qxEndBox, qyEndBox, qzEndBox,
            startOffsetBox, endOffsetBox, mxBox, myBox, mzBox);
        bool R(TextBox box, string key, out double v, Func<double, string?>? rule = null) =>
            Helpers.FieldCheck.Read(this, box, key, out v, rule);
        double qxEndK = 0, qyEndK = 0, qzEndK = 0, endOffset = 0, mxK = 0, myK = 0, mzK = 0;
        if (!R(startOffsetBox, "FemMemberLoadStartOffset", out double startOffset, Helpers.FieldCheck.NonNegative)
            || !R(qxStartBox, "FemMemberLoadQx", out double qxStartK)
            || !R(qyStartBox, "FemMemberLoadQy", out double qyStartK)
            || !R(qzStartBox, "FemMemberLoadQz", out double qzStartK)
            || (trapezoid && (!R(qxEndBox, "FemMemberLoadQx", out qxEndK) || !R(qyEndBox, "FemMemberLoadQy", out qyEndK)
                              || !R(qzEndBox, "FemMemberLoadQz", out qzEndK)))
            || (!isPoint && !R(endOffsetBox, "FemMemberLoadEndOffset", out endOffset, Helpers.FieldCheck.NonNegative))
            || (isPoint && (!R(mxBox, "FemMemberLoadMx", out mxK) || !R(myBox, "FemMemberLoadMy", out myK)
                            || !R(mzBox, "FemMemberLoadMz", out mzK))))
            return;
        double qxStart = FemUnitConverter.KiloNewtonsToNewtons(qxStartK);
        double qyStart = FemUnitConverter.KiloNewtonsToNewtons(qyStartK);
        double qzStart = FemUnitConverter.KiloNewtonsToNewtons(qzStartK);
        double qxEnd = isPoint ? 0 : trapezoid ? FemUnitConverter.KiloNewtonsToNewtons(qxEndK) : qxStart;
        double qyEnd = isPoint ? 0 : trapezoid ? FemUnitConverter.KiloNewtonsToNewtons(qyEndK) : qyStart;
        double qzEnd = isPoint ? 0 : trapezoid ? FemUnitConverter.KiloNewtonsToNewtons(qzEndK) : qzStart;
        double mx = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mxK);
        double my = FemUnitConverter.KiloNewtonMetersToNewtonMeters(myK);
        double mz = FemUnitConverter.KiloNewtonMetersToNewtonMeters(mzK);
        int applied = 0;

        // Участок — в пределах каждого выбранного стержня (иначе ошибка всплывала бы только при «Сохранить»).
        foreach (var member in _members.Where(member => member.Id != 0))
        {
            var probe = new FemMemberLoad { MemberId = member.Id, DistributionType = distribution, StartOffsetM = startOffset, EndOffsetM = endOffset };
            if (Services.FemLoadAuthoring.MemberLoadOffsetError(_editor.Session, probe) is { } offsetError)
            {
                Helpers.FieldCheck.Fail(this, startOffsetBox, "FemMemberLoadStartOffset",
                    _members.Count > 1 ? $"{member.ElemTag}: {offsetError}" : offsetError);
                return;
            }
        }

        foreach (var member in _members.Where(member => member.Id != 0))
        {
            _editor.Session.Execute(new SetMemberLoadCommand(new FemMemberLoad
            {
                Id = 0,
                SchemaId = _editor.Session.Schema.Id,
                LoadCaseId = loadCase.Id,
                MemberId = member.Id,
                CoordinateSystem = coordinate,
                DistributionType = distribution,
                StartOffsetM = startOffset,
                EndOffsetM = endOffset,
                QxStart = qxStart, QyStart = qyStart, QzStart = qzStart,
                QxEnd = qxEnd, QyEnd = qyEnd, QzEnd = qzEnd,
                Mx = mx, My = my, Mz = mz
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

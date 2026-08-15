using System.Collections.ObjectModel;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Строка WPF preview с уведомлениями об изменении источника.</summary>
public sealed class FemMemberForceSetPreviewRowVM : ViewModelBase
{
    /// <summary>Создаёт VM строки поверх модели preview.</summary>
    public FemMemberForceSetPreviewRowVM(FemMemberForceSetPreviewRow model)
    {
        Model = model;
    }

    /// <summary>Исходная модель строки, передаваемая фабрике.</summary>
    public FemMemberForceSetPreviewRow Model { get; }

    /// <summary>Тег mesh-узла.</summary>
    public string MeshNodeTag => Model.MeshNodeTag;

    /// <summary>Положение по стержню, м.</summary>
    public double PositionS => Model.PositionS;

    /// <summary>Кандидат слева.</summary>
    public FemMemberForceCandidate? LeftCandidate => Model.LeftCandidate;

    /// <summary>Кандидат справа.</summary>
    public FemMemberForceCandidate? RightCandidate => Model.RightCandidate;

    /// <summary>Допустимые источники для ComboBox этой строки.</summary>
    public IReadOnlyList<FemForceSourceSide> SourceOptions =>
        LeftCandidate is not null && RightCandidate is not null
            ? [FemForceSourceSide.Left, FemForceSourceSide.Right]
            : [FemForceSourceSide.Only];

    /// <summary>Выбранная сторона.</summary>
    public FemForceSourceSide SelectedSource
    {
        get => Model.SelectedSource;
        set
        {
            if (value == Model.SelectedSource) return;
            Model.SelectedSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCandidate));
            OnPropertyChanged(nameof(N));
            OnPropertyChanged(nameof(Mx));
            OnPropertyChanged(nameof(My));
            OnPropertyChanged(nameof(Vx));
            OnPropertyChanged(nameof(Vy));
            OnPropertyChanged(nameof(T));
        }
    }

    /// <summary>Текущий выбранный кандидат.</summary>
    public FemMemberForceCandidate SelectedCandidate => Model.SelectedCandidate;

    /// <summary>Продольная сила, кН.</summary>
    public double N => SelectedCandidate.Values.N / 1000.0;

    /// <summary>Изгибающий момент Mx, кН·м.</summary>
    public double Mx => SelectedCandidate.Values.Mz / 1000.0;

    /// <summary>Изгибающий момент My, кН·м.</summary>
    public double My => SelectedCandidate.Values.My / 1000.0;

    /// <summary>Поперечная сила Vx, кН.</summary>
    public double Vx => SelectedCandidate.Values.Qz / 1000.0;

    /// <summary>Поперечная сила Vy, кН.</summary>
    public double Vy => SelectedCandidate.Values.Qy / 1000.0;

    /// <summary>Крутящий момент, кН·м.</summary>
    public double T => SelectedCandidate.Values.Mx / 1000.0;
}

/// <summary>ViewModel окна preview набора усилий.</summary>
public sealed class FemMemberForceSetPreviewVM : ViewModelBase
{
    /// <summary>Создаёт VM из результата builder-а.</summary>
    public FemMemberForceSetPreviewVM(
        FemMemberForceSetPreview preview,
        string tag,
        string? description)
    {
        Preview = preview;
        Tag = tag;
        Description = description;
        Rows = new ObservableCollection<FemMemberForceSetPreviewRowVM>(
            preview.Rows.Select(row => new FemMemberForceSetPreviewRowVM(row)));
    }

    /// <summary>Исходный preview builder-а.</summary>
    public FemMemberForceSetPreview Preview { get; }

    string _tag = "";
    /// <summary>Имя создаваемого набора.</summary>
    public string Tag
    {
        get => _tag;
        set
        {
            if (value == _tag) return;
            _tag = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSave));
        }
    }

    string? _description;
    /// <summary>Описание создаваемого набора.</summary>
    public string? Description
    {
        get => _description;
        set { if (value == _description) return; _description = value; OnPropertyChanged(); }
    }

    /// <summary>Схема результата.</summary>
    public string SchemaTag => Preview.SchemaTag;

    /// <summary>Конструктивный стержень результата.</summary>
    public string MemberTag => Preview.MemberTag;

    /// <summary>Выбранный шаг результата.</summary>
    public string StepLabel => Preview.StepLabel;

    /// <summary>Строки mesh-узлов.</summary>
    public ObservableCollection<FemMemberForceSetPreviewRowVM> Rows { get; }

    /// <summary>Можно ли подтвердить окно.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Tag) && Rows.Count > 0;

    /// <summary>Формирует selection с актуальным выбором каждой строки.</summary>
    public FemMemberForceSetSelection BuildSelection() =>
        new(
            Tag.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Rows.Select(row => row.Model).ToArray());
}

using CScore;

namespace OpenCS.ViewModels;

/// <summary>Источник усилий для строки mesh-узла.</summary>
public enum FemForceSourceSide
{
    /// <summary>У узла есть только один кандидат.</summary>
    Only,
    /// <summary>Кандидат от элемента слева по цепочке.</summary>
    Left,
    /// <summary>Кандидат от элемента справа по цепочке.</summary>
    Right
}

/// <summary>Кандидат усилий одного элемента на одном mesh-узле.</summary>
public sealed record FemMemberForceCandidate(
    int ElementTag,
    FemForceEndpointValues Values);

/// <summary>Строка preview с кандидатами усилий и выбранной стороной.</summary>
public sealed class FemMemberForceSetPreviewRow
{
    /// <summary>Создаёт строку preview.</summary>
    public FemMemberForceSetPreviewRow(
        string meshNodeTag,
        double positionS,
        FemMemberForceCandidate? leftCandidate,
        FemMemberForceCandidate? rightCandidate,
        FemForceSourceSide selectedSource)
    {
        MeshNodeTag = meshNodeTag;
        PositionS = positionS;
        LeftCandidate = leftCandidate;
        RightCandidate = rightCandidate;
        SelectedSource = selectedSource;
    }

    /// <summary>Тег mesh-узла.</summary>
    public string MeshNodeTag { get; }

    /// <summary>Накопленная длина от начала цепочки, м.</summary>
    public double PositionS { get; }

    /// <summary>Кандидат от предыдущего элемента.</summary>
    public FemMemberForceCandidate? LeftCandidate { get; }

    /// <summary>Кандидат от следующего элемента.</summary>
    public FemMemberForceCandidate? RightCandidate { get; }

    /// <summary>Выбранный источник значения строки.</summary>
    public FemForceSourceSide SelectedSource { get; set; }

    /// <summary>Текущий выбранный кандидат без суммирования соседних значений.</summary>
    public FemMemberForceCandidate SelectedCandidate =>
        SelectedSource == FemForceSourceSide.Right
            ? RightCandidate ?? LeftCandidate ?? throw new InvalidOperationException()
            : LeftCandidate ?? RightCandidate ?? throw new InvalidOperationException();

    /// <summary>Переводит выбранного кандидата в строку расчётного набора.</summary>
    public LoadItem ToLoadItem(int num) =>
        FemForceEndpointConverter.ToLoadItem(
            SelectedCandidate.Values, num, $"node {MeshNodeTag}");
}

/// <summary>Данные preview набора усилий одного конструктивного стержня.</summary>
public sealed record FemMemberForceSetPreview(
    int SchemaId,
    string SchemaTag,
    int MemberId,
    string MemberTag,
    int StepIndex,
    string StepLabel,
    IReadOnlyList<FemMemberForceSetPreviewRow> Rows);

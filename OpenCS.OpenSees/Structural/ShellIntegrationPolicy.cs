namespace OpenCS.OpenSees.Structural;

/// <summary>Политика интегрирования shell-элемента.</summary>
public enum ShellIntegrationPolicy
{
    /// <summary>Полная интеграция: 4 точки для Q4 и 3 точки для T3.</summary>
    Full,

    /// <summary>Reduced integration: одна точка; допустимо только для T3.</summary>
    Reduced
}

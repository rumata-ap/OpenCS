namespace OpenCS.OpenSees.Runtime;

/// <summary>Абстракция запуска OpenSees для отделения сервиса от Process.</summary>
public interface IOpenSeesProcessRunner
{
    /// <summary>Запускает процесс и возвращает stdout, stderr и статус завершения.</summary>
    Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken);
}

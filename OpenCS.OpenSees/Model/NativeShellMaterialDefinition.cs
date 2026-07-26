namespace OpenCS.OpenSees.Model;

/// <summary>Материал shell-модели с native tag и provenance исходного материала.</summary>
public sealed record NativeShellMaterialDefinition(
    int Tag,
    string SourceId,
    NativeShellMaterialSpec Spec)
{
    /// <summary>Fingerprint содержательного native material.</summary>
    public string Fingerprint => Spec.Fingerprint;

    /// <summary>Проверяет tag, provenance и native specification.</summary>
    public void Validate()
    {
        if (Tag <= 0)
            throw new InvalidOperationException("Tag shell-материала должен быть положительным.");
        if (Spec is null)
            throw new InvalidOperationException($"Материал {Tag}: отсутствует native specification.");
        _ = Spec.ToTcl(Tag);
    }
}

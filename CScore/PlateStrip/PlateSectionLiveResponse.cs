namespace CScore.PlateStrip;

/// <summary>
/// Нелинейный источник плитного отклика: прямой, <b>не замороженный</b> вызов PlateSection на
/// переданном состоянии. Контраст с <see cref="PlateSectionTangentSnapshot"/>, который по
/// контракту снимает касательную один раз в нулевом состоянии и потому пригоден только для
/// линейной редукции Срезов 2–6.
///
/// <b>Ограничение, обязательное к учёту вызывающей стороной:</b> PlateSection — total-strain
/// модель <b>без внутреннего состояния</b>. Отклик зависит только от текущей ShellStrainState;
/// история трещин и пластичности не хранится, разгрузка идёт по кривой нагружения. Формулировка
/// родительской спеки «PlateSection для каждой width integration point хранит собственное
/// constitutive state» описывает целевую архитектуру, а не текущую реализацию. Следствия:
/// <list type="bullet">
/// <item>отклик материала однозначен, но единственность решения нелинейной системы равновесия
/// отсюда <b>не</b> следует: при немонотонном отклике f_int(u) = f_ext имеет несколько корней,
/// и разные схемы нагружения сходятся к разным ветвям;</item>
/// <item>касательная PlateSection.ComputeTangent строится конечными разностями, поэтому
/// квадратичной сходимости Ньютона на этом источнике не будет;</item>
/// <item>диаграмма с нисходящей ветвью даёт немонотонный отклик — расходимость Ньютона здесь
/// штатный исход, а не дефект решателя.</item>
/// </list>
///
/// Единицы не конвертируются: PlateSection принимает материалы в том же виде, в каком их
/// передаёт вызывающая сторона (см. память platesection-kpa-not-mpa-units).
///
/// Источник request-local и не персистентный: в SQLite хранится линейный EquivalentSection.
/// </summary>
public sealed class PlateSectionLiveResponse : IPlateSectionResponse
{
    readonly PlateSection _section;
    readonly Diagramm _concrete;
    readonly Diagramm _rebar;
    readonly IReadOnlyList<Diagramm?>? _layerDiagrams;
    readonly bool? _tensionOverride;

    public PlateSectionLiveResponse(
        PlateSection section,
        Diagramm concreteDiagram,
        Diagramm rebarDiagram,
        IReadOnlyList<Diagramm?>? layerDiagrams = null,
        bool? tensionOverride = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(concreteDiagram);
        ArgumentNullException.ThrowIfNull(rebarDiagram);

        _section = section;
        _concrete = concreteDiagram;
        _rebar = rebarDiagram;
        _layerDiagrams = layerDiagrams;
        _tensionOverride = tensionOverride;

        var parts = PlateSectionSourceFingerprint.BaseParts(section, concreteDiagram, rebarDiagram, layerDiagrams);
        parts.Add($"tension-override:{tensionOverride?.ToString() ?? "none"}");
        Fingerprint = PlateSectionSourceFingerprint.Hash(parts);
    }

    /// <summary>Вид источника.</summary>
    public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.PlateSectionLive;

    /// <summary>Отпечаток входов источника. Не зависит от состояний, на которых его вызывали:
    /// касательная живого источника не константа и в отпечаток не входит.</summary>
    public string Fingerprint { get; }

    /// <summary>Усилия плитного сечения в заданном состоянии.
    ///
    /// Используется <see cref="PlateSection.Compute"/> с <c>computeStiffness: false</c>:
    /// приватный <c>Integrate</c> напрямую недоступен, а со <c>true</c> Compute делает пять
    /// прогонов интегратора ради EAx/EIx/Zc, которые редукции полосы не нужны — внутри цикла
    /// Ньютона это пятикратная цена на каждый вызов.</summary>
    public PlateResultants Forces(ShellStrainState state)
    {
        Validate(state);
        var result = _section.Compute(
            state, _concrete, _rebar, _layerDiagrams,
            computeStiffness: false, tensionOverride: _tensionOverride);
        return new PlateResultants(result.Nx, result.Ny, result.Nxy, result.Mx, result.My, result.Mxy);
    }

    /// <summary>Касательные блоки A/B/D/As и усилия в заданном состоянии. Результат несёт и
    /// усилия тоже, поэтому потребителю не нужен отдельный вызов <see cref="Forces"/>.</summary>
    public PlateShellTangentResult Tangent(ShellStrainState state)
    {
        Validate(state);
        return _section.ComputeTangent(
            state, _concrete, _rebar, _layerDiagrams, tensionOverride: _tensionOverride);
    }

    static void Validate(ShellStrainState state)
    {
        foreach (double value in state.ToArray())
            if (!double.IsFinite(value))
                throw new ArgumentException("Состояние деформации плиты должно быть конечным.", nameof(state));
    }
}

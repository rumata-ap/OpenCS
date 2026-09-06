using CScore;

namespace OpenCS.Reporting;

/// <summary>Поставщик отчёта по раскрытию нормальных трещин.</summary>
public sealed class CrackWidthReportProvider : IReportProvider
{
    /// <inheritdoc/>
    public string TaskKind => "crack_width";

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedKinds => [TaskKind];

    /// <inheritdoc/>
    public bool CanHandle(CalcTask task) => SupportedKinds.Contains(task.Kind, StringComparer.Ordinal);

    /// <inheritdoc/>
    public IReadOnlyList<ReportImageRequest> DescribeImages(CalcTask task, CalcResult result)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(result);
        var data = CrackWidthReportData.Parse(result.DataJson);
        var plane = new Kurvature { e0 = data.E0 ?? 0, ky = data.Ky ?? 0, kz = data.Kz ?? 0 };
        return [new("strain", "Карта деформаций ε при длительной нагрузке",
            plane, CalcType.N, ReportImageMode.Strain)];
    }

    /// <inheritdoc/>
    public ReportDocument Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!CanHandle(context.Task))
            throw new ArgumentException("Поставщик не поддерживает данный тип задачи.", nameof(context));

        var data = CrackWidthReportData.Parse(context.Result.DataJson);
        string tag = string.IsNullOrWhiteSpace(context.Task.Tag) ? "без метки" : context.Task.Tag;
        var document = new ReportDocument($"Расчёт по раскрытию нормальных трещин — {tag}");
        SectionReportSections.Identification(document, context,
            "координаты и размеры — м; координаты стержней и ширины трещин — мм; площади As,tens и Abt — см²; ε — безразмерная; κ — 1/м; σ — МПа; N — кН; M — кН·м");

        document
            .Add(new ReportHeading(1, "Исходные данные"))
            .Add(new ReportKeyValueTable(
            [
                ("N", SectionReportSections.Force(data.N)),
                ("Mx длительный / полный", $"{SectionReportSections.Moment(data.MxLong)} / {SectionReportSections.Moment(data.MxTotal)}"),
                ("My длительный / полный", $"{SectionReportSections.Moment(data.MyLong)} / {SectionReportSections.Moment(data.MyTotal)}"),
                ("Mx исходный длительный / полный", $"{SectionReportSections.Moment(data.MxLongInput)} / {SectionReportSections.Moment(data.MxTotalInput)}"),
                ("My исходный длительный / полный", $"{SectionReportSections.Moment(data.MyLongInput)} / {SectionReportSections.Moment(data.MyTotalInput)}"),
                ("Предельная ширина длительная / кратковременная", $"{F(data.AcrcUltLong)} / {F(data.AcrcUltShort)} мм")
            ], "Параметр", "Значение"))
            .Add(new ReportHeading(1, "Проверка образования трещин"))
            .Add(new ReportKeyValueTable(
            [
                ("Трещины", data.Cracked ? "образуются" : "не образуются"),
                ("Mcrc", $"{F(data.Mcrc)} кН·м"),
                ("Mx_crc / My_crc", $"{F(data.MxCrc)} / {F(data.MyCrc)} кН·м"),
                ("εmax,t / εt,ult", $"{F(data.EpsMaxTension)} / {F(data.EpsTensionLimit)}")
            ], "Параметр", "Значение"));

        if (!data.Cracked)
        {
            document.Add(new ReportParagraph("Трещины не образуются, расчёт по раскрытию не требуется согласно п. 8.2.6 СП 63."));
            AddCommonSections(document, context, data);
            return document;
        }

        document
            .Add(new ReportHeading(1, "Расчёт ширины раскрытия"))
            .Add(new ReportFormula(
                "(8.128)",
                $"a<sub>crc</sub> = φ<sub>1</sub>·φ<sub>2</sub>·φ<sub>3</sub>·ψ<sub>s</sub>·(σ<sub>s</sub>/E<sub>s</sub>)·l<sub>s</sub>",
                $"σs = {F(data.SigmaS)} МПа; ψs = {F(data.PsiS)}; ls = {F(data.Ls)} мм",
                $"a<sub>crc,long</sub> = {F(data.Acrc1)} мм"))
            .Add(new ReportFormula(
                "(8.136)",
                "l<sub>s</sub> = 0,5·(A<sub>bt</sub>/A<sub>s</sub>)·d<sub>s</sub>",
                $"0,5·({F(data.Abt)} см²/{F(data.AsTens)} см²)·{F(data.DsEq)} мм",
                $"l<sub>s</sub> = {F(data.Ls)} мм; ограничения: 10d<sub>s</sub>…40d<sub>s</sub> и 10…40 см"));

        double psiCandidateLong = data.SigmaS == 0
            ? double.NaN
            : 1.0 - 0.8 * data.SigmaSCrc / data.SigmaS;
        double psiCandidateShort = data.SigmaS == 0
            ? double.NaN
            : 1.0 - 0.8 * data.SigmaSCrc2 / data.SigmaS;
        bool stressMethod = double.IsFinite(psiCandidateLong)
            && Math.Abs(psiCandidateLong - data.PsiS) <= 1e-3
            && Math.Abs(psiCandidateShort - data.PsiS2) <= 1e-3;
        if (stressMethod)
        {
            document.Add(new ReportFormula(
                "(8.137)",
                "ψ<sub>s</sub> = 1 − 0,8·σ<sub>s,cr</sub>/σ<sub>s</sub>",
                $"1 − 0,8·{F(data.SigmaSCrc)}/{F(data.SigmaS)}",
                $"ψs = {F(data.PsiS)}; ψs2 = {F(data.PsiS2)}"));
        }
        else
        {
            document
                .Add(new ReportParagraph("ψs определён по методу, выбранному в настройках расчёта; нейтральная формулировка применяется, поскольку portable-результат не содержит самого флага метода."))
                .Add(new ReportFormula(
                    "(8.137)",
                    "ψ<sub>s</sub> = 1 − 0,8·σ<sub>s,cr</sub>/σ<sub>s</sub>",
                    $"1 − 0,8·{F(data.SigmaSCrc)} / {F(data.SigmaS)} = {F(psiCandidateLong)}; результат ψs = {F(data.PsiS)}",
                    "кандидат по отношению напряжений"))
                .Add(new ReportFormula(
                    "(8.161)",
                    "ψ<sub>s</sub> = 1/(1 + 0,8·ε<sub>s,cr</sub>/ε<sub>s</sub>)",
                    "деформационный метод выбран настройками расчёта",
                    $"ψs = {F(data.PsiS)}; ψs2 = {F(data.PsiS2)}"));
        }

        document
            .Add(new ReportTable(
                ["Составляющая", "Расшифровка", "Ширина, мм"],
                [
                    (IReadOnlyList<string>)["acrc1", "постоянные и длительные нагрузки", F(data.Acrc1)],
                    (IReadOnlyList<string>)["acrc2", "вся нагрузка, кратковременно", F(data.Acrc2)],
                    (IReadOnlyList<string>)["acrc3", "постоянные и длительные, кратковременно", F(data.Acrc3)],
                    (IReadOnlyList<string>)["acrc,long", "acrc1", F(data.AcrcLong)],
                    (IReadOnlyList<string>)["acrc,short", "acrc1 + acrc2 − acrc3", F(data.AcrcShort)]
                ]))
            .Add(new ReportFormula("(8.2.6)", "a<sub>crc,long</sub> ≤ a<sub>crc,ult,long</sub>",
                $"{F(data.AcrcLong)} ≤ {F(data.AcrcUltLong)} мм", data.PassedLong ? "проверка пройдена" : "проверка не пройдена"))
            .Add(new ReportFormula("(8.2.6)", "a<sub>crc,short</sub> ≤ a<sub>crc,ult,short</sub>",
                $"{F(data.AcrcShort)} ≤ {F(data.AcrcUltShort)} мм", data.PassedShort ? "проверка пройдена" : "проверка не пройдена"))
            .Add(new ReportParagraph("Таблица по стержням — внормативное уточнение: норма даёт одно значение по представительному стержню, здесь ψs и ширина рассчитаны в точке каждого растянутого стержня при общем ls."))
            .Add(new ReportTable(
                ["x, мм", "y, мм", "ψs", "a_crc,long, мм", "ψs2", "a_crc,short, мм"],
                data.AcrcByRebar.Select(row => (IReadOnlyList<string>)[
                    F(row.X), F(row.Y), F(row.PsiS), F(row.AcrcLongMm), F(row.PsiS2), F(row.AcrcShortMm)]).ToList()))
            .Add(new ReportHeading(1, "Промежуточные величины"))
            .Add(new ReportKeyValueTable(
            [
                ("h0", $"{F(data.H0)} мм"),
                ("σs / σs,cr / σs,cr2", $"{F(data.SigmaS)} / {F(data.SigmaSCrc)} / {F(data.SigmaSCrc2)} МПа"),
                ("As,tens / Abt", $"{F(data.AsTens)} / {F(data.Abt)} см²"),
                ("ls / ds,eq", $"{F(data.Ls)} / {F(data.DsEq)} мм")
            ], "Параметр", "Значение"));

        AddCommonSections(document, context, data);
        if (!data.PassedLong)
            document.Add(new ReportWarning("Длительная ширина раскрытия превышает предельное значение."));
        if (!data.PassedShort)
            document.Add(new ReportWarning("Кратковременная ширина раскрытия превышает предельное значение."));
        if (!data.CrcConverged)
            document.Add(new ReportWarning("Определение момента образования трещин не сошлось."));
        if (!data.PlaneConverged)
            document.Add(new ReportWarning("Плоскость деформаций длительной нагрузки не сошлась."));
        return document;
    }

    static void AddCommonSections(ReportDocument document, ReportContext context, CrackWidthReportData data)
    {
        SectionReportSections.Eta(document, data.Eta);
        if (context.Images.TryGetValue("strain", out var strain))
            document.Add(new ReportImage("Карта деформаций ε при длительной нагрузке", strain));
        var plane = new Kurvature { e0 = data.E0 ?? 0, ky = data.Ky ?? 0, kz = data.Kz ?? 0 };
        if (context.Section is { } section)
        {
            SectionReportSections.Geometry(document, section, plane);
            SectionReportSections.MaterialsAndDiagrams(document, section, CalcType.N, plane);
            SectionReportSections.Rebar(document, section, plane, CalcType.N);
            SectionReportSections.Prestress(document, data.Prestress);
        }
        else
            SectionReportSections.SectionMissingWarning(document);
    }

    static string F(double value) => SectionReportSections.F(value);
}

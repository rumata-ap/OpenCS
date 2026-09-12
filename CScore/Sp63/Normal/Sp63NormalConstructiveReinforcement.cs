namespace CScore.Sp63.Normal;

/// <summary>
/// Справочная проверка минимального процента продольного армирования по п. 10.3.6
/// СП 63.13330.2018 для прямоугольного профиля. Не влияет на вердикт прочности:
/// элемент, не удовлетворяющий этому требованию, норма относит к бетонным, а не
/// к разрушившимся железобетонным.
/// </summary>
public static class Sp63NormalConstructiveReinforcement
{
    const double AreaTolerance = 1e-9;

    // Пороги гибкости l0/h для прямоугольного сечения, эквивалентные l0/i = 17 и l0/i = 87.
    const double SlendernessLow = 5.0;
    const double SlendernessHigh = 25.0;
    const double MuMinLowPercent = 0.1;
    const double MuMinHighPercent = 0.25;

    // п. 10.3.2: защитный слой не менее диаметра стержня и не менее 10 мм.
    const double MinCoverAbsolute = 0.010;

    // п. 10.3.9: не менее двух растянутых стержней при ширине элемента более 150 мм.
    const double MinWidthForTwoTensionBars = 0.150;

    // Для арматуры, равномерной по контуру, и для центрально-растянутых элементов
    // норма требует удвоенное значение, отнесённое к полной площади сечения бетона.
    const double MuMinCentralTensionPercent = 2.0 * MuMinLowPercent;

    /// <summary>
    /// Вычисляет справочные проверки минимального армирования для выбранной ветви расчёта.
    /// </summary>
    /// <param name="branch">Ветвь, выбранная <see cref="Sp63NormalChecker"/>.</param>
    /// <param name="profile">Профиль прямоугольного сечения.</param>
    /// <param name="memberContext">Контекст элемента (нужен l0 для внецентренного сжатия).</param>
    public static (List<CheckDetail> Details, List<Sp63NormalMessage> Notes) Check(
        string branch, Sp63NormalSectionProfile profile, Sp63MemberContext memberContext)
    {
        var details = new List<CheckDetail>();
        var notes = new List<Sp63NormalMessage>();
        switch (branch)
        {
            case "central_tension":
                AddCheck(details, "Sp63Normal_MinReinforcementCentralTension",
                    profile.TotalRebarArea, profile.B * profile.Height,
                    MuMinCentralTensionPercent);
                break;

            case "bending":
            case "eccentric_tension_between":
            case "eccentric_tension_outside":
                AddCheck(details, "Sp63Normal_MinReinforcementTension",
                    profile.TensionLayer.Area, profile.B * profile.H0, MuMinLowPercent);
                AddCompressionIfPresent(details, profile, MuMinLowPercent);
                break;

            case "compression":
                double? muMin = CompressionMuMinPercent(memberContext, profile.Height);
                if (muMin is { } value)
                {
                    AddCheck(details, "Sp63Normal_MinReinforcementTension",
                        profile.TensionLayer.Area, profile.B * profile.H0, value);
                    AddCompressionIfPresent(details, profile, value);
                }
                else
                {
                    notes.Add(new Sp63NormalMessage(
                        "min_reinforcement_slenderness_unknown",
                        Sp63NormalMessageKind.Information,
                        "10.3.6",
                        "Sp63Normal_MinReinforcementSlendernessUnknown"));
                }

                break;
        }

        return (details, notes);
    }

    /// <summary>
    /// Вычисляет справочные геометрические проверки раздела 10.3 для прямоугольного
    /// профиля: частичная проверка защитного слоя по диаметру стержня (п. 10.3.2 —
    /// без учёта таблицы условий эксплуатации, которая в OpenCS пока не выбирается)
    /// и минимальное число растянутых стержней при широком сечении (п. 10.3.9).
    /// Не входит в <see cref="Sp63NormalResult.StrengthPassed"/>.
    /// </summary>
    public static (List<CheckDetail> Details, List<Sp63NormalMessage> Notes) CheckCoverAndSpacing(
        Sp63NormalSectionProfile profile)
    {
        var details = new List<CheckDetail>();
        var notes = new List<Sp63NormalMessage>();

        AddCoverCheck(details, notes, "Sp63Normal_MinCoverTension",
            profile.TensionLayer, profile.Height - profile.H0);
        if (profile.CompressionLayer.Area > AreaTolerance)
            AddCoverCheck(details, notes, "Sp63Normal_MinCoverCompression",
                profile.CompressionLayer, profile.APrime);

        AddTensionBarCountCheck(details, profile);

        return (details, notes);
    }

    static void AddCoverCheck(List<CheckDetail> details, List<Sp63NormalMessage> notes,
        string descriptionKey, Sp63NormalRebarLayer layer, double edgeToCenterDistance)
    {
        if (layer.Area <= AreaTolerance) return;

        double maxDiameter = layer.Bars.Count > 0
            ? layer.Bars.Max(bar => bar.Diameter)
            : 0.0;
        if (!(maxDiameter > 0))
        {
            notes.Add(new Sp63NormalMessage(
                "cover_bar_diameter_unknown",
                Sp63NormalMessageKind.Information,
                "10.3.2",
                "Sp63Normal_CoverBarDiameterUnknown"));
            return;
        }

        double requiredCover = Math.Max(maxDiameter, MinCoverAbsolute);
        double actualCover = edgeToCenterDistance - maxDiameter / 2.0;
        details.Add(new CheckDetail
        {
            Formula = "10.3.2",
            Description = descriptionKey,
            NormReference = "10.3.2",
            Applied = requiredCover,
            Allowable = actualCover,
            Variables = new Dictionary<string, double>
            {
                ["requiredCover"] = requiredCover,
                ["actualCover"] = actualCover,
                ["maxDiameter"] = maxDiameter
            }
        });
    }

    static void AddTensionBarCountCheck(List<CheckDetail> details,
        Sp63NormalSectionProfile profile)
    {
        if (profile.TensionLayer.Area <= AreaTolerance) return;
        if (profile.B <= MinWidthForTwoTensionBars) return;

        int count = profile.TensionLayer.Bars.Count;
        if (count == 0) return;

        details.Add(new CheckDetail
        {
            Formula = "10.3.9",
            Description = "Sp63Normal_MinTensionBarCount",
            NormReference = "10.3.9",
            Applied = 2.0,
            Allowable = count,
            Variables = new Dictionary<string, double>
            {
                ["tensionBarCount"] = count,
                ["b"] = profile.B
            }
        });
    }

    static double? CompressionMuMinPercent(Sp63MemberContext memberContext, double height)
    {
        if (memberContext.EffectiveLengthL0 is not > 0 || height <= 0)
            return null;

        double slenderness = memberContext.EffectiveLengthL0.Value / height;
        if (slenderness <= SlendernessLow) return MuMinLowPercent;
        if (slenderness >= SlendernessHigh) return MuMinHighPercent;

        double t = (slenderness - SlendernessLow) / (SlendernessHigh - SlendernessLow);
        return MuMinLowPercent + t * (MuMinHighPercent - MuMinLowPercent);
    }

    static void AddCompressionIfPresent(List<CheckDetail> details,
        Sp63NormalSectionProfile profile, double muMinPercent)
    {
        if (profile.CompressionLayer.Area > AreaTolerance)
            AddCheck(details, "Sp63Normal_MinReinforcementCompression",
                profile.CompressionLayer.Area, profile.B * profile.H0, muMinPercent);
    }

    static void AddCheck(List<CheckDetail> details, string descriptionKey,
        double rebarArea, double baseArea, double muMinPercent)
    {
        if (baseArea <= 0) return;

        double muActualPercent = rebarArea / baseArea * 100.0;
        details.Add(new CheckDetail
        {
            Formula = "(10.3.6)",
            Description = descriptionKey,
            NormReference = "10.3.6",
            Applied = muMinPercent,
            Allowable = muActualPercent,
            Variables = new Dictionary<string, double>
            {
                ["muMinPercent"] = muMinPercent,
                ["muActualPercent"] = muActualPercent,
                ["rebarArea"] = rebarArea,
                ["baseArea"] = baseArea
            }
        });
    }
}

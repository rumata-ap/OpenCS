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

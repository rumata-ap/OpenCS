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
    /// <param name="profile">Профиль прямоугольного сечения.</param>
    /// <param name="axis">Ось изгиба (Mx — слой с большей Y считается верхним).</param>
    /// <param name="elementKind">
    /// Тип элемента для пп. 10.3.5 и 10.3.8; <see langword="null"/> — проверки расстояний
    /// между стержнями не выполняются и не упоминаются.
    /// </param>
    public static (List<CheckDetail> Details, List<Sp63NormalMessage> Notes) CheckCoverAndSpacing(
        Sp63NormalSectionProfile profile,
        Sp63NormalAxis axis = Sp63NormalAxis.Mx,
        Sp63ElementKind? elementKind = null)
    {
        var details = new List<CheckDetail>();
        var notes = new List<Sp63NormalMessage>();

        bool hasIdealized = profile.TensionLayer.IsIdealized ||
            profile.CompressionLayer.IsIdealized;
        if (hasIdealized)
        {
            notes.Add(new Sp63NormalMessage(
                "idealized_rebar_layer",
                Sp63NormalMessageKind.Information,
                "10.3.2/10.3.9",
                "Sp63Normal_IdealizedRebarLayer"));
        }

        if (!profile.TensionLayer.IsIdealized)
            AddCoverCheck(details, notes, "Sp63Normal_MinCoverTension",
                profile.TensionLayer, profile.Height - profile.H0);
        if (profile.CompressionLayer.Area > AreaTolerance &&
            !profile.CompressionLayer.IsIdealized)
            AddCoverCheck(details, notes, "Sp63Normal_MinCoverCompression",
                profile.CompressionLayer, profile.APrime);

        if (!profile.TensionLayer.IsIdealized)
            AddTensionBarCountCheck(details, profile);

        if (elementKind is { } kind)
            AddBarSpacingChecks(details, notes, profile, axis, kind);

        return (details, notes);
    }

    /// <summary>
    /// Расстояния между стержнями: минимальный зазор в свету по п. 10.3.5 и наибольший
    /// шаг осей по п. 10.3.8. Внутри слоя расстояния считаются между соседними стержнями
    /// вдоль слоя (поперёк плоскости изгиба); для колонн дополнительно — наибольший шаг
    /// уровней арматуры в плоскости изгиба (включая промежуточные уровни).
    /// </summary>
    static void AddBarSpacingChecks(List<CheckDetail> details, List<Sp63NormalMessage> notes,
        Sp63NormalSectionProfile profile, Sp63NormalAxis axis, Sp63ElementKind kind)
    {
        if (kind == Sp63ElementKind.Unspecified)
        {
            notes.Add(new Sp63NormalMessage(
                "spacing_element_kind_unspecified",
                Sp63NormalMessageKind.Information,
                "10.3.5/10.3.8",
                "Sp63Normal_SpacingElementKindUnspecified"));
            return;
        }

        var tension = profile.TensionLayer;
        var compression = profile.CompressionLayer;
        bool tensionIsTop = tension.Coordinate > compression.Coordinate;
        AddLayerSpacingChecks(details, profile, axis, kind, tension, isTop: tensionIsTop,
            "Sp63Normal_MinClearSpacingTension", "Sp63Normal_MaxBarSpacingTension");
        if (compression.Area > AreaTolerance)
            AddLayerSpacingChecks(details, profile, axis, kind, compression, isTop: !tensionIsTop,
                "Sp63Normal_MinClearSpacingCompression", "Sp63Normal_MaxBarSpacingCompression");

        if (kind == Sp63ElementKind.Column && profile.LayerCoordinates.Count >= 2 &&
            !tension.IsIdealized && !compression.IsIdealized)
        {
            var levels = profile.LayerCoordinates;
            double maxGap = 0.0;
            for (int i = 1; i < levels.Count; i++)
                maxGap = Math.Max(maxGap, levels[i] - levels[i - 1]);
            details.Add(new CheckDetail
            {
                Formula = "10.3.8",
                Description = "Sp63Normal_MaxLevelSpacingColumn",
                NormReference = "10.3.8",
                Applied = maxGap,
                Allowable = ColumnMaxSpacingInPlane,
                Variables = new Dictionary<string, double>
                {
                    ["maxLevelSpacing"] = maxGap,
                    ["limit"] = ColumnMaxSpacingInPlane,
                    ["levelCount"] = levels.Count
                }
            });
        }
    }

    static void AddLayerSpacingChecks(List<CheckDetail> details,
        Sp63NormalSectionProfile profile, Sp63NormalAxis axis, Sp63ElementKind kind,
        Sp63NormalRebarLayer layer, bool isTop, string minKey, string maxKey)
    {
        if (layer.IsIdealized || layer.Bars.Count < 2) return;

        // Координата вдоль слоя — перпендикулярна оси высоты сечения.
        var bars = layer.Bars
            .Select(bar => (Along: axis == Sp63NormalAxis.Mx ? bar.X : bar.Y, bar.Diameter))
            .OrderBy(bar => bar.Along)
            .ToList();
        double maxCenterSpacing = 0.0;
        double minClear = double.PositiveInfinity;
        bool diametersKnown = bars.All(bar => bar.Diameter > 0);
        for (int i = 1; i < bars.Count; i++)
        {
            double centers = bars[i].Along - bars[i - 1].Along;
            maxCenterSpacing = Math.Max(maxCenterSpacing, centers);
            if (diametersKnown)
                minClear = Math.Min(minClear,
                    centers - (bars[i].Diameter + bars[i - 1].Diameter) / 2.0);
        }

        if (diametersKnown)
        {
            double maxDiameter = bars.Max(bar => bar.Diameter);
            double absolute = MinClearSpacingAbsolute(kind, axis, isTop);
            double required = Math.Max(maxDiameter, absolute);
            details.Add(new CheckDetail
            {
                Formula = "10.3.5",
                Description = minKey,
                NormReference = "10.3.5",
                Applied = required,
                Allowable = minClear,
                Variables = new Dictionary<string, double>
                {
                    ["requiredClearSpacing"] = required,
                    ["actualMinClearSpacing"] = minClear,
                    ["maxDiameter"] = maxDiameter,
                    ["absoluteMinimum"] = absolute
                }
            });
        }

        double limit = kind == Sp63ElementKind.Column
            ? ColumnMaxSpacingAcrossPlane
            : BeamMaxSpacing(profile.Height);
        details.Add(new CheckDetail
        {
            Formula = "10.3.8",
            Description = maxKey,
            NormReference = "10.3.8",
            Applied = maxCenterSpacing,
            Allowable = limit,
            Variables = new Dictionary<string, double>
            {
                ["maxCenterSpacing"] = maxCenterSpacing,
                ["limit"] = limit,
                ["h"] = profile.Height
            }
        });
    }

    /// <summary>
    /// Абсолютный минимум зазора в свету по п. 10.3.5: колонна (вертикальные стержни
    /// при бетонировании) — 50 мм; балка/плита — 25 мм для нижней и 30 мм для верхней
    /// арматуры. При изгибе My верх/низ по чертежу не определяется — принимается 30 мм.
    /// Случай «более двух нижних рядов» (50 мм) не распознаётся: профиль содержит два
    /// крайних уровня.
    /// </summary>
    static double MinClearSpacingAbsolute(Sp63ElementKind kind, Sp63NormalAxis axis, bool isTop) =>
        kind == Sp63ElementKind.Column
            ? 0.050
            : axis == Sp63NormalAxis.Mx && !isTop ? 0.025 : 0.030;

    /// <summary>Наибольший шаг стержней балок и плит по п. 10.3.8, м.</summary>
    static double BeamMaxSpacing(double h) =>
        h <= 0.150 ? 0.200 : Math.Min(1.5 * h, 0.400);

    // п. 10.3.8, колонны: 400 мм поперёк плоскости изгиба, 500 мм в плоскости изгиба.
    const double ColumnMaxSpacingAcrossPlane = 0.400;
    const double ColumnMaxSpacingInPlane = 0.500;

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

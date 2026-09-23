using CScore.Sp63.Normal;

namespace CScore.Sp63.CrackWidth;

/// <summary>
/// Строит ориентированный полосовой профиль <see cref="Sp63SlsSectionGeometry"/> из реального
/// <see cref="CrossSection"/>: прямоугольник — через <see cref="Sp63RectangularGeometryPolicy"/>
/// и <see cref="Sp63RebarLayoutAnalyzer"/>, тавр/двутавр — через
/// <see cref="Sp63TeeGeometryPolicy"/> и <see cref="Sp63TeeRebarLayoutAnalyzer"/>.
/// Ориентация идёт от сжатой грани (0) к растянутой (Height); смена знака момента задаётся
/// <paramref name="tensionDirection"/> и не меняет исходный контур.
/// </summary>
public static class Sp63SlsSectionGeometryFactory
{
    /// <summary>Строит ориентированный профиль или возвращает типизированные причины неприменимости.</summary>
    /// <param name="section">Расчётное сечение.</param>
    /// <param name="shapeKind">Выбранная форма: Rectangular или Tee (тавр/двутавр).</param>
    /// <param name="axis">Ось изгиба.</param>
    /// <param name="calc">Вид расчёта для характеристик материалов.</param>
    /// <param name="tensionDirection">Направление растяжения: +1 по положительной оси, −1 по отрицательной.</param>
    /// <param name="geometry">Построенный профиль или <see langword="null"/>.</param>
    /// <param name="messages">Причины неприменимости (стабильные коды и ключи локализации).</param>
    public static bool TryCreate(
        CrossSection section,
        Sp63NormalShapeKind shapeKind,
        Sp63NormalAxis axis,
        CalcType calc,
        int tensionDirection,
        out Sp63SlsSectionGeometry? geometry,
        out IReadOnlyList<Sp63NormalMessage> messages)
    {
        geometry = null;
        ArgumentNullException.ThrowIfNull(section);
        if (tensionDirection is not (1 or -1))
            return Fail("invalid_tension_direction", "Sp63Normal_InvalidTensionDirection", "8.1", out messages);
        if (shapeKind is not (Sp63NormalShapeKind.Rectangular or Sp63NormalShapeKind.Tee))
            return Fail("unsupported_shape", "Sp63Normal_ShapeNotSupported", "8.2", out messages);

        var concreteArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.Region && area.Material?.Type == MatType.Concrete);
        var concreteChars = concreteArea?.Material?.GetChars(calc);
        if (concreteChars is null || !Positive(concreteChars.E) ||
            !Positive(Math.Abs(concreteChars.Fc)) || !Positive(concreteChars.Ft))
            return Fail("missing_concrete_chars", "Sp63Normal_MissingConcreteResistance", "8.2.11", out messages);

        var rebarArea = section.Areas.FirstOrDefault(area =>
            area.Category == AreaCategory.RebarGroup && area.Material != null);
        if (rebarArea is not null)
        {
            var rebarChars = rebarArea.Material!.GetChars(calc);
            if (rebarChars is null || !Positive(rebarChars.E) || !Positive(Math.Abs(rebarChars.Ft)))
                return Fail("missing_rebar_chars", "Sp63Normal_MissingRebarResistance", "8.2.16", out messages);
        }

        return shapeKind == Sp63NormalShapeKind.Rectangular
            ? TryCreateRectangular(section, axis, calc, tensionDirection, out geometry, out messages)
            : TryCreateTee(section, axis, calc, tensionDirection, out geometry, out messages);
    }

    /// <summary>Прямоугольный профиль: одна полоса по всей высоте.</summary>
    static bool TryCreateRectangular(CrossSection section, Sp63NormalAxis axis, CalcType calc,
        int tensionDirection, out Sp63SlsSectionGeometry? geometry,
        out IReadOnlyList<Sp63NormalMessage> messages)
    {
        geometry = null;
        var classification = Sp63RectangularGeometryPolicy.Classify(section);
        if (!classification.IsApplicable)
            return Fail("unsupported_geometry", "Sp63Normal_GeometryNotSupported", "8.2.9", out messages);

        var analysis = Sp63RebarLayoutAnalyzer.Analyze(section, axis, calc,
            tensionDirection, requireAtLeastTwoLayers: true);
        if (analysis.Profile is null)
        {
            messages = analysis.Messages;
            return false;
        }

        var profile = analysis.Profile;
        if (profile.LayerCoordinates.Count > 2)
            return Fail("extra_rebar_layers", "Sp63Normal_ExtraRebarLayers", "8.2.9", out messages);

        var rect = classification.Geometry!;
        double height = axis == Sp63NormalAxis.Mx ? rect.Height : rect.Width;
        double width = axis == Sp63NormalAxis.Mx ? rect.Width : rect.Height;
        geometry = new Sp63SlsSectionGeometry(
            Sp63NormalShapeKind.Rectangular,
            height,
            [new Sp63SlsSectionBand(0.0, height, width)],
            new Sp63SlsRebarLayer(profile.H0, profile.TensionLayer.Area,
                EffectiveDiameter(profile.TensionLayer)),
            new Sp63SlsRebarLayer(profile.APrime, profile.CompressionLayer.Area,
                EffectiveDiameter(profile.CompressionLayer)),
            hasCompressionFlange: false,
            hasTensionFlange: false);
        messages = [];
        return true;
    }

    /// <summary>
    /// Профиль тавра/двутавра: две или три полосы постоянной ширины из нормализованной
    /// геометрии <see cref="Sp63TeeGeometry"/>, ориентированные от сжатой грани.
    /// </summary>
    static bool TryCreateTee(CrossSection section, Sp63NormalAxis axis, CalcType calc,
        int tensionDirection, out Sp63SlsSectionGeometry? geometry,
        out IReadOnlyList<Sp63NormalMessage> messages)
    {
        geometry = null;
        var classification = Sp63TeeGeometryPolicy.Classify(section, axis);
        if (!classification.IsApplicable)
        {
            bool notATee = classification.Failure == Sp63TeeGeometryFailure.NotATeeShape;
            return Fail(
                notATee ? "not_a_tee_shape" : "unsupported_geometry",
                notATee ? "Sp63Normal_NotATeeShape" : "Sp63Normal_TeeGeometryNotSupported",
                "8.1.11", out messages);
        }

        if (!Sp63RebarBarCollector.TryCollect(section, axis, calc, out var layers,
                out var collectMessage))
        {
            messages = [collectMessage!];
            return false;
        }
        if (layers.Count < 2)
            return Fail("insufficient_rebar_layers", "Sp63Normal_InsufficientRebarLayers", "8.1.8", out messages);
        if (layers.Count > 2)
            return Fail("extra_rebar_layers", "Sp63Normal_ExtraRebarLayers", "8.2.9", out messages);

        var analysis = Sp63TeeRebarLayoutAnalyzer.Analyze(section, axis, calc, tensionDirection);
        if (analysis.Profile is null)
        {
            messages = analysis.Messages;
            return false;
        }

        var tee = classification.Geometry!;
        var profile = analysis.Profile;
        double height = tee.H;
        double tBottom = tee.BottomFlangeThickness, tTop = tee.TopFlangeThickness;
        var strips = new List<Sp63SlsSectionBand>();
        if (tBottom > Sp63SlsSectionGeometry.Tolerance)
            strips.Add(new Sp63SlsSectionBand(0.0, tBottom, tee.BottomFlangeWidth));
        strips.Add(new Sp63SlsSectionBand(tBottom, height - tTop, tee.Bw));
        if (tTop > Sp63SlsSectionGeometry.Tolerance)
            strips.Add(new Sp63SlsSectionBand(height - tTop, height, tee.TopFlangeWidth));

        IReadOnlyList<Sp63SlsSectionBand> bands = tensionDirection > 0
            ? strips
            : strips.AsEnumerable().Reverse()
                .Select(band => new Sp63SlsSectionBand(
                    height - band.End, height - band.Start, band.Width))
                .ToList();

        geometry = new Sp63SlsSectionGeometry(
            Sp63NormalShapeKind.Tee,
            height,
            bands,
            new Sp63SlsRebarLayer(profile.H0, profile.TensionLayer.Area,
                EffectiveDiameter(profile.TensionLayer)),
            new Sp63SlsRebarLayer(profile.APrime, profile.CompressionLayer.Area,
                EffectiveDiameter(profile.CompressionLayer)),
            hasCompressionFlange: bands[0].Width > tee.Bw + Sp63SlsSectionGeometry.Tolerance,
            hasTensionFlange: bands[^1].Width > tee.Bw + Sp63SlsSectionGeometry.Tolerance);
        messages = [];
        return true;
    }

    /// <summary>Эффективный диаметр слоя — средневзвешенный по площади стержней, м.</summary>
    static double EffectiveDiameter(Sp63NormalRebarLayer layer)
    {
        double totalArea = layer.Bars.Sum(bar => bar.Area);
        if (totalArea <= 1e-14)
            return 0.012;
        return layer.Bars.Sum(bar => bar.Diameter * bar.Area) / totalArea;
    }

    static bool Fail(string code, string text, string reference,
        out IReadOnlyList<Sp63NormalMessage> messages)
    {
        messages = [new Sp63NormalMessage(code, Sp63NormalMessageKind.Applicability, reference, text)];
        return false;
    }

    static bool Positive(double value) => double.IsFinite(value) && value > 0;
}

using System;
using System.Collections.Generic;

namespace CScore;

/// <summary>
/// Конструктивные проверки по СП 16.13330.2017, раздел 10.
/// </summary>
public static class SteelConstructiveCheck
{
    /// <summary>
    /// Минимальная толщина элемента (10.1.1).
    /// t ≥ tmin = 2.5 мм для несущих элементов
    /// </summary>
    public static CheckDetail CheckMinThickness(
        double thickness, double minThickness = 0.0025)
    {
        return new CheckDetail
        {
            Formula = "10.1.1",
            Description = "Минимальная толщина элемента",
            NormReference = "СП 16.13330.2017, п. 10.1.1",
            Applied = thickness,
            Allowable = minThickness,
            Variables = new Dictionary<string, double>
            {
                ["t"] = thickness, ["tmin"] = minThickness
            }
        };
    }

    /// <summary>
    /// Максимальная гибкость элемента (10.2.1).
    /// λ ≤ λmax (таблица 9)
    /// </summary>
    public static CheckDetail CheckMaxSlenderness(
        double slenderness, double maxSlenderness, string axis = "")
    {
        string desc = string.IsNullOrEmpty(axis)
            ? "Максимальная гибкость"
            : $"Максимальная гибкость ({axis})";
        return new CheckDetail
        {
            Formula = "10.2.1",
            Description = desc,
            NormReference = "СП 16.13330.2017, п. 10.2.1",
            Applied = slenderness,
            Allowable = maxSlenderness,
            Variables = new Dictionary<string, double>
            {
                ["λ"] = slenderness, ["λmax"] = maxSlenderness
            }
        };
    }

    /// <summary>
    /// Получить λmax по типу элемента (таблица 9, СП 16.13330.2017).
    /// </summary>
    public static double GetMaxSlenderness(StructuralElementType elementType)
    {
        return elementType switch
        {
            StructuralElementType.CompressionMember => 180,
            StructuralElementType.CompressionMemberInTruss => 120,
            StructuralElementType.TensionMemberInTruss => 400,
            StructuralElementType.BeamWeb => 250,
            StructuralElementType.CraneBeam => 250,
            StructuralElementType.ColumnInFrame => 150,
            StructuralElementType.Tie => 300,
            _ => 200
        };
    }

    /// <summary>
    /// Полная проверка конструктивных требований.
    /// </summary>
    public static List<CheckDetail> CheckAll(
        SteelSection section, DesignContext context)
    {
        var details = new List<CheckDetail>();

        // Минимальная толщина (10.1.1)
        double minThickness = EstimateMinThickness(section);
        if (minThickness > 1e-10)
            details.Add(CheckMinThickness(minThickness));

        // Максимальная гибкость (10.2.1)
        double lambdaX = context.DesignLengthX / section.ix;
        double lambdaY = context.DesignLengthY / section.iy;
        double lambdaMax = GetMaxSlenderness(context.ElementType);

        details.Add(CheckMaxSlenderness(lambdaX, lambdaMax, "X"));
        details.Add(CheckMaxSlenderness(lambdaY, lambdaMax, "Y"));

        return details;
    }

    static double EstimateMinThickness(SteelSection section)
    {
        var pts = section.OuterContour;
        if (pts.Count < 4) return 0;

        double yMin = double.MaxValue, yMax = double.MinValue;
        double xMin = double.MaxValue, xMax = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
        }

        double h = yMax - yMin;
        double b = xMax - xMin;
        if (h < 1e-10 || b < 1e-10) return 0;

        // Толщина стенки: минимальная протяжённость по X в средней зоне
        double midY = (yMin + yMax) / 2;
        double bandH = h * 0.25;
        double xMinMid = double.MaxValue, xMaxMid = double.MinValue;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - midY) < bandH)
            {
                if (x < xMinMid) xMinMid = x;
                if (x > xMaxMid) xMaxMid = x;
            }
        }
        double tw = xMaxMid > xMinMid + 1e-10 ? xMaxMid - xMinMid : 0;

        // Толщина полки: минимальная протяжённость по Y в крайних зонах
        double bandEdge = h * 0.15;
        double yRangeTop = 0, yRangeBot = 0;
        foreach (var (x, y) in pts)
        {
            if (Math.Abs(y - yMax) < bandEdge) yRangeTop = Math.Max(yRangeTop, yMax - y);
            if (Math.Abs(y - yMin) < bandEdge) yRangeBot = Math.Max(yRangeBot, y - yMin);
        }
        double tf = Math.Max(yRangeTop, yRangeBot);

        return Math.Min(tw, tf);
    }
}

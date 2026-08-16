using System;
using System.Collections.Generic;
using System.Text.Json;
using CScore;
using OpenCS.Utilites;

namespace OpenCS.ViewModels;

/// <summary>Данные одной стадии полной кривизны для построения графических полей.</summary>
public sealed class TotalCurvatureStageVM
{
    /// <summary>Номер стадии во внутреннем контракте результата.</summary>
    public int Number { get; }

    /// <summary>Инженерное название стадии для заголовка и кнопки.</summary>
    public string Label { get; }

    /// <summary>Найденная плоскость деформаций.</summary>
    public Kurvature Plane { get; }

    /// <summary>Тип диаграмм материала.</summary>
    public CalcType CalcType { get; }

    /// <summary>Учитывается ли растяжение бетона.</summary>
    public bool ConcreteTension { get; }

    /// <summary>Признак сходимости стадии.</summary>
    public bool Converged { get; }

    /// <summary>Индивидуальные значения ψs по стержням.</summary>
    public IReadOnlyList<TotalCurvatureRebarPsiVM> PsiSByRebar { get; }

    /// <summary>Возвращает ψs для фибры арматуры текущей стадии.</summary>
    public double PsiSFor(Fiber fiber)
    {
        double bestDistance2 = double.PositiveInfinity;
        TotalCurvatureRebarPsiVM? best = null;
        foreach (var value in PsiSByRebar)
        {
            double dx = value.X - fiber.X;
            double dy = value.Y - fiber.Y;
            double distance2 = dx * dx + dy * dy;
            if (distance2 < bestDistance2)
            {
                bestDistance2 = distance2;
                best = value;
            }
        }

        // Координаты сохраняются в метрах; 1e-8 м — запас только на
        // погрешность сериализации, а не поиск другого стержня.
        return best != null && bestDistance2 <= 1e-16 && best.Applicable
            ? best.PsiS
            : 1.0;
    }

    /// <summary>Эффективное напряжение стержня в кПа для равновесия и жёсткости.</summary>
    public double EffectiveStressKpa(Fiber fiber) =>
        Curvature8232.CorrectedStress(fiber.Sig, PsiSFor(fiber));

    TotalCurvatureStageVM(
        int number, string label, Kurvature plane, CalcType calcType,
        bool concreteTension, bool converged,
        IReadOnlyList<TotalCurvatureRebarPsiVM> psiSByRebar)
    {
        Number = number;
        Label = label;
        Plane = plane;
        CalcType = calcType;
        ConcreteTension = concreteTension;
        Converged = converged;
        PsiSByRebar = psiSByRebar;
    }

    /// <summary>Разбирает стадию из JSON; старые результаты без плоскости не считаются графическими.</summary>
    public static TotalCurvatureStageVM? Parse(JsonElement root, int number, bool cracked)
    {
        string property = $"stage{number}";
        if (!root.TryGetProperty(property, out var stage)
            || stage.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryGetNumber(stage, "e0", out var e0)
            || !TryGetNumber(stage, "ky", out var ky)
            || !TryGetNumber(stage, "kz", out var kz))
            return null;

        var calcType = number == 3 ? CalcType.NL : CalcType.N;
        if (stage.TryGetProperty("calc_type", out var calcTypeValue)
            && calcTypeValue.ValueKind == JsonValueKind.String
            && Enum.TryParse<CalcType>(calcTypeValue.GetString(), true, out var parsedCalcType))
            calcType = parsedCalcType;

        bool concreteTension = !cracked;
        if (stage.TryGetProperty("concrete_tension", out var tensionValue)
            && (tensionValue.ValueKind == JsonValueKind.True
                || tensionValue.ValueKind == JsonValueKind.False))
            concreteTension = tensionValue.GetBoolean();

        bool converged = stage.TryGetProperty("converged", out var convergedValue)
            && convergedValue.ValueKind == JsonValueKind.True
            && convergedValue.GetBoolean();

        var psi = new List<TotalCurvatureRebarPsiVM>();
        if (stage.TryGetProperty("psi_s_by_rebar", out var psiValue)
            && psiValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in psiValue.EnumerateArray())
            {
                if (!TryGetNumber(item, "x", out var x)
                    || !TryGetNumber(item, "y", out var y)
                    || !TryGetNumber(item, "psi_s", out var psiS))
                    continue;

                int? num = null;
                if (item.TryGetProperty("num", out var numValue)
                    && numValue.ValueKind == JsonValueKind.Number
                    && numValue.TryGetInt32(out var parsedNum))
                    num = parsedNum;

                bool applicable = item.TryGetProperty("applicable", out var applicableValue)
                    && applicableValue.ValueKind == JsonValueKind.True
                    && applicableValue.GetBoolean();
                psi.Add(new TotalCurvatureRebarPsiVM(num, x, y, psiS, applicable));
            }
        }

        return new TotalCurvatureStageVM(
            number,
            LabelFor(number, cracked),
            new Kurvature { e0 = e0, ky = ky, kz = kz },
            calcType,
            concreteTension,
            converged,
            psi);
    }

    /// <summary>Возвращает локализованное название стадии без технического номера.</summary>
    public static string LabelFor(int number, bool cracked) => (cracked, number) switch
    {
        (false, 1) => Loc.S("TotalCurvature_Stage1_UncrackedLabel"),
        (false, 2) => Loc.S("TotalCurvature_Stage2_UncrackedLabel"),
        (true, 1) => Loc.S("TotalCurvature_Stage1_CrackedLabel"),
        (true, 2) => Loc.S("TotalCurvature_Stage2_CrackedLabel"),
        (true, 3) => Loc.S("TotalCurvature_Stage3_CrackedLabel"),
        _ => Loc.S("TotalCurvature_StagePlotTitle")
    };

    static bool TryGetNumber(JsonElement element, string key, out double value)
    {
        if (element.TryGetProperty(key, out var number)
            && number.ValueKind == JsonValueKind.Number)
        {
            value = number.GetDouble();
            return double.IsFinite(value);
        }

        value = 0.0;
        return false;
    }
}

/// <summary>Значение ψs отдельного стержня для подсказки графического поля.</summary>
public sealed record TotalCurvatureRebarPsiVM(
    int? Num, double X, double Y, double PsiS, bool Applicable);

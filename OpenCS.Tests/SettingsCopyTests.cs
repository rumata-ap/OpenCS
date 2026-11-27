using System.Reflection;
using System.Text.Json;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Настройки: копия без потерь полей, словари слоёв AutoCAD без учёта регистра, хорда дуг в мм.</summary>
public sealed class SettingsCopyTests
{
    /// <summary>Значение «не по умолчанию» для свойства типа <paramref name="t"/>; null — тип пропускается.</summary>
    static object? Changed(Type t, object? current)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(double)) return (current as double? ?? 0) + 0.123;
        if (t == typeof(int)) return (current as int? ?? 0) + 7;
        if (t == typeof(bool)) return !(current as bool? ?? false);
        if (t == typeof(string)) return (current as string ?? "") + "_x";
        if (t.IsEnum)
        {
            var values = Enum.GetValues(t);
            foreach (var v in values) if (!v.Equals(current)) return v;
            return null;
        }
        return null;
    }

    static void AssertCloneKeepsEveryProperty<T>(T source, Func<T, T> clone) where T : class
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite).ToList();
        int set = 0;
        foreach (var p in props)
            if (Changed(p.PropertyType, p.GetValue(source)) is { } v) { p.SetValue(source, v); set++; }
        Assert.True(set > 0);
        var copy = clone(source);
        Assert.NotSame(source, copy);
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(copy));
        foreach (var p in props.Where(p => Changed(p.PropertyType, null) != null || p.PropertyType == typeof(string)))
            Assert.True(Equals(p.GetValue(source), p.GetValue(copy)), $"{typeof(T).Name}.{p.Name} не скопировано");
    }

    [Fact]
    public void Clone_KeepsEveryProperty_AllSettingsClasses()
    {
        AssertCloneKeepsEveryProperty(new CalcSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new PlotSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new CsvExportSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new LiraImportSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new AcadImportSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new GmshSettings(), s => s.Clone());
        AssertCloneKeepsEveryProperty(new SectionCutWindowSettings(), s => s.Clone());
    }

    [Fact]
    public void CalcClone_KeepsRebarTolerance_AndNestedIsDeep()
    {
        var s = new CalcSettings { ImportedBarRebarTolerancePercent = 12.5 };
        s.SectionCutWindow.Width = 1234;
        var c = s.Clone();
        Assert.Equal(12.5, c.ImportedBarRebarTolerancePercent);
        c.SectionCutWindow.Width = 1;
        Assert.Equal(1234, s.SectionCutWindow.Width);
    }

    [Fact]
    public void AcadLayerDictionaries_CaseInsensitive_AfterCloneAndJson()
    {
        var s = new AcadImportSettings();
        s.SectionLayerMaterials["Бетон"] = 3;
        Assert.Equal(3, s.Clone().SectionLayerMaterials["БЕТОН"]);
        var loaded = JsonSerializer.Deserialize<AcadImportSettings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(3, loaded.SectionLayerMaterials["бетон"]);
    }

    [Fact]
    public void AcadChord_Millimetres_SameSegments_ForMmAndMetreDrawings()
    {
        // Полукруг r = 500 мм: в чертеже в мм r = 500, в метрах r = 0,5; хорда 10 мм.
        int mm = OpenCS.Services.AcadImporter.ChordSegments(Math.PI, 500, OpenCS.Services.AcadImporter.ChordInDrawingUnits(10, 0.001));
        int m = OpenCS.Services.AcadImporter.ChordSegments(Math.PI, 0.5, OpenCS.Services.AcadImporter.ChordInDrawingUnits(10, 1.0));
        Assert.Equal(mm, m);
        Assert.True(m > 100); // π·500/10 ≈ 157 отрезков, а не 3
    }
}

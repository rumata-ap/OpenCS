using System.Text;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Источник свойств КЭ схемы (упругие характеристики, удельный вес, площади) — по программе-источнику.</summary>
public static class FemSelfWeightSourceFactory
{
    /// <inheritdoc cref="Create(DatabaseService, int, out IReadOnlyList{string})"/>
    public static IFemElementStiffnessSource? Create(DatabaseService db, int schemaId) => Create(db, schemaId, out _);

    /// <summary>
    /// SCAD — жёсткости в единицах вложения и профили сортамента; ЛИРА — жёсткости таблицы API в единицах вложения
    /// <see cref="FemSchemaSourceFileKind.LiraUnits"/> (нет — т и м) и профили сортамента ЛИРЫ; за ними (и для своей
    /// схемы) — сечения проекта (<see cref="ProjectElementStiffnessSource"/>, удельный вес по умолчанию).
    /// </summary>
    /// <param name="notes">Допущения источника для отчёта (единицы и удельный вес по умолчанию).</param>
    public static IFemElementStiffnessSource? Create(DatabaseService db, int schemaId, out IReadOnlyList<string> notes)
    {
        var list = new List<string>();
        notes = list;
        var sources = new List<IFemElementStiffnessSource>();
        if (ScadSource(db, schemaId) is { } scad) sources.Add(scad);
        else if (LiraSource(db, schemaId, list) is { } lira) sources.Add(lira);

        var members = db.GetFemMembers(schemaId);
        if (members.Count > 0 || sources.Count == 0)
        {
            sources.Add(new ProjectElementStiffnessSource(members, db.CrossSections, db.PlateSections, db.Materials));
            list.Add(string.Format(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                "Удельный вес КЭ с сечениями проекта принят по умолчанию: бетон {0} кН/м³, сталь и арматура {1} кН/м³.",
                ProjectElementStiffnessSource.ConcreteUnitWeight / 1e3, ProjectElementStiffnessSource.SteelUnitWeight / 1e3));
        }
        return sources.Count == 1 ? sources[0] : new FemCompositeStiffnessSource([.. sources]);
    }

    static ScadElementStiffnessSource? ScadSource(DatabaseService db, int schemaId)
    {
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
        try
        {
            var model = ScadAnalysisModel.FromJson(Encoding.UTF8.GetString(file.Data));
            return new ScadElementStiffnessSource(db.GetFemSchemaStiffnesses(schemaId), model.ForceUnitN, model.LengthUnitM,
                SteelIndex(db, schemaId, FemSchemaSourceFileKind.ScadSteelProfiles));
        }
        catch (System.IO.InvalidDataException) { return null; }
    }

    static LiraElementStiffnessSource? LiraSource(DatabaseService db, int schemaId, List<string> notes)
    {
        if (db.FemSchemas.FirstOrDefault(s => s.Id == schemaId)?.SourceType != "lira") return null;
        var stiffnesses = db.GetFemSchemaStiffnesses(schemaId);
        if (stiffnesses.Count == 0) return null;
        LiraUnits? units = null;
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.LiraUnits) is { } file)
            try { units = LiraUnits.FromJson(Encoding.UTF8.GetString(file.Data)); }
            catch (System.IO.InvalidDataException) { }
        if (units == null)
        {
            units = LiraUnits.Default((db.LoadLiraImportSettings() ?? LiraImportSettings.Default).TonToKnFactor);
            notes.Add("Единицы жёсткостей ЛИРЫ не сохранены при схеме — приняты т и м (обновите жёсткости из ЛИРЫ).");
        }
        return new LiraElementStiffnessSource(stiffnesses, units, SteelIndex(db, schemaId, FemSchemaSourceFileKind.LiraSteelProfiles));
    }

    static SteelProfileIndex? SteelIndex(DatabaseService db, int schemaId, string kind)
    {
        if (db.GetFemSchemaSourceFile(schemaId, kind) is not { } file) return null;
        try { return SteelProfileIndex.FromJson(Encoding.UTF8.GetString(file.Data)); }
        catch (System.IO.InvalidDataException) { return null; }
    }
}

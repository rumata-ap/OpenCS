using System.Text;
using CScore.Fem;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Источник свойств КЭ схемы (упругие характеристики, удельный вес, площади) — по программе-источнику.</summary>
public static class FemSelfWeightSourceFactory
{
    /// <summary>
    /// SCAD — жёсткости в единицах вложения и профили сортамента; прочие источники (ЛИРА, своя схема) — пока нет:
    /// собственный вес их КЭ попадает в диагностики «нет удельного веса».
    /// </summary>
    public static IFemElementStiffnessSource? Create(DatabaseService db, int schemaId)
    {
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
        try
        {
            var model = ScadAnalysisModel.FromJson(Encoding.UTF8.GetString(file.Data));
            SteelProfileIndex? steel = null;
            if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadSteelProfiles) is { } steelFile)
                try { steel = SteelProfileIndex.FromJson(Encoding.UTF8.GetString(steelFile.Data)); }
                catch (System.IO.InvalidDataException) { }
            return new ScadElementStiffnessSource(db.GetFemSchemaStiffnesses(schemaId), model.ForceUnitN, model.LengthUnitM, steel);
        }
        catch (System.IO.InvalidDataException) { return null; }
    }
}

using CScore.Fem.Loads;
using CScore.Import;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Источник удельного веса и площадей для собственного веса КЭ схемы — по программе-источнику.</summary>
public static class FemSelfWeightSourceFactory
{
    /// <summary>
    /// SCAD — RO и брус жёсткостей в единицах вложения; прочие источники (ЛИРА, своя схема) — пока нет (срез 4в):
    /// собственный вес их КЭ попадает в диагностики «нет удельного веса».
    /// </summary>
    public static IFemSelfWeightSource? Create(DatabaseService db, int schemaId)
    {
        if (db.GetFemSchemaSourceFile(schemaId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
        try
        {
            var model = ScadAnalysisModel.FromJson(System.Text.Encoding.UTF8.GetString(file.Data));
            return new ScadSelfWeightSource(db.GetFemSchemaStiffnesses(schemaId), model.ForceUnitN, model.LengthUnitM);
        }
        catch (System.IO.InvalidDataException) { return null; }
    }
}

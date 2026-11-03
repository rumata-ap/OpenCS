using System.Text;
using CScore.Fem;
using CScore.Fem.Fragment;
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
    /// <param name="grossBars">Стержни с сечениями проекта — без арматуры (<see cref="ProjectElementStiffnessSource"/>).</param>
    /// <param name="reducedStiffness">
    /// Понижающие коэффициенты жёсткости ЛИРЫ к упругой жёсткости бетона (PLKE, WLKE, EIy/EIz бруса) — только линейный
    /// расчёт и секущий без физической нелинейности; применённые коэффициенты — в <paramref name="notes"/>. У SCAD в
    /// строках жёсткостей коэффициентов нет — на них не влияет.
    /// </param>
    public static IFemElementStiffnessSource? Create(DatabaseService db, int schemaId, out IReadOnlyList<string> notes,
        bool grossBars = false, bool reducedStiffness = false)
    {
        var list = new List<string>();
        notes = list;
        var sources = new List<IFemElementStiffnessSource>();
        // Фрагмент этажа: программа, файлы жёсткостей и единицы — у родителя; таблица жёсткостей — копия фрагмента,
        // номера жёсткостей КЭ — из опорных КЭ родителя (см. ParentStiffnessNumberSource).
        var schema = db.FemSchemas.FirstOrDefault(s => s.Id == schemaId);
        int? parentId = schema?.ParentSchemaId;
        int unitsId = parentId ?? schemaId;
        string? program = parentId is int pid ? schema!.ParentSourceType ?? db.GetFemSchemaSourceType(pid) : schema?.SourceType;

        IFemElementStiffnessSource? programSource = null;
        if (ScadSource(db, unitsId, schemaId) is { } scad) programSource = scad;
        else if (LiraSource(db, unitsId, schemaId, program, list, reducedStiffness) is { } lira)
        {
            programSource = lira;
            var applied = lira.ReductionSummary();
            if (applied.Count > 0)
                list.Add("Понижающие коэффициенты жёсткости ЛИРЫ учтены (E′, h′ пластин; I бруса): " + string.Join("; ", applied) + ".");
        }
        if (programSource is not null)
        {
            if (parentId is not null) programSource = new ParentStiffnessNumberSource(programSource, FragmentMap(db, schemaId));
            sources.Add(programSource);
        }

        var members = db.GetFemMembers(schemaId);
        if (members.Count > 0 || sources.Count == 0)
        {
            sources.Add(new ProjectElementStiffnessSource(members, db.CrossSections, db.PlateSections, db.Materials,
                grossBars: grossBars));
            list.Add(string.Format(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                "Удельный вес КЭ с сечениями проекта принят по умолчанию: бетон {0} кН/м³, сталь и арматура {1} кН/м³.",
                ProjectElementStiffnessSource.ConcreteUnitWeight / 1e3, ProjectElementStiffnessSource.SteelUnitWeight / 1e3));
        }
        return sources.Count == 1 ? sources[0] : new FemCompositeStiffnessSource([.. sources]);
    }

    /// <param name="unitsId">Схема, где лежат файл модели и профили программы (у фрагмента — родитель).</param>
    /// <param name="tableId">Схема таблицы жёсткостей (у фрагмента — своя копия).</param>
    static ScadElementStiffnessSource? ScadSource(DatabaseService db, int unitsId, int tableId)
    {
        if (db.GetFemSchemaSourceFile(unitsId, FemSchemaSourceFileKind.ScadAnalysisModel) is not { } file) return null;
        try
        {
            var model = ScadAnalysisModel.FromJson(Encoding.UTF8.GetString(file.Data));
            return new ScadElementStiffnessSource(db.GetFemSchemaStiffnesses(tableId), model.ForceUnitN, model.LengthUnitM,
                SteelIndex(db, unitsId, FemSchemaSourceFileKind.ScadSteelProfiles));
        }
        catch (System.IO.InvalidDataException) { return null; }
    }

    static LiraElementStiffnessSource? LiraSource(DatabaseService db, int unitsId, int tableId, string? program, List<string> notes,
        bool reducedStiffness)
    {
        if (program != "lira") return null;
        var stiffnesses = db.GetFemSchemaStiffnesses(tableId);
        if (stiffnesses.Count == 0) return null;
        LiraUnits? units = null;
        if (db.GetFemSchemaSourceFile(unitsId, FemSchemaSourceFileKind.LiraUnits) is { } file)
            try { units = LiraUnits.FromJson(Encoding.UTF8.GetString(file.Data)); }
            catch (System.IO.InvalidDataException) { }
        if (units == null)
        {
            units = LiraUnits.Default((db.LoadLiraImportSettings() ?? LiraImportSettings.Default).TonToKnFactor);
            notes.Add("Единицы жёсткостей ЛИРЫ не сохранены при схеме — приняты т и м (обновите жёсткости из ЛИРЫ).");
        }
        return new LiraElementStiffnessSource(stiffnesses, units, SteelIndex(db, unitsId, FemSchemaSourceFileKind.LiraSteelProfiles),
            reducedStiffness);
    }

    /// <summary>Сопоставление КЭ фрагмента с опорными КЭ родителя по геометрии сетки фрагмента.</summary>
    static FragmentParentMap FragmentMap(DatabaseService db, int schemaId)
    {
        var nodes = new Dictionary<string, CScore.Fem.FemMeshNode>(StringComparer.Ordinal);
        foreach (var n in db.GetFemMeshNodes(schemaId)) nodes.TryAdd(n.NodeTag, n);
        return FragmentParentMap.Build(db.GetFemMeshElements(schemaId), nodes,
            new ParentMeshMapper(db.GetFragmentRefElements(schemaId)));
    }

    static SteelProfileIndex? SteelIndex(DatabaseService db, int schemaId, string kind)
    {
        if (db.GetFemSchemaSourceFile(schemaId, kind) is not { } file) return null;
        try { return SteelProfileIndex.FromJson(Encoding.UTF8.GetString(file.Data)); }
        catch (System.IO.InvalidDataException) { return null; }
    }
}

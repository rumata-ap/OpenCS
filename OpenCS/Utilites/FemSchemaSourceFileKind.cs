namespace OpenCS.Utilites
{
   /// <summary>Виды вложений FEM-схемы (столбец <c>kind</c> таблицы <c>fem_schema_source_files</c>).</summary>
   public static class FemSchemaSourceFileKind
   {
      /// <summary>Выгрузка плагина SCAD «Экспорт для OpenCS» (*.opencs-scad.json) — как есть.</summary>
      public const string ScadSelectedRebar = "scad_selected_rebar";

      /// <summary>ЖБ-группы SCAD схемы — JSON <see cref="CScore.Import.ScadConcreteGroupIndex.ToJson"/>.</summary>
      public const string ScadConcreteGroups = "scad_concrete_groups";

      /// <summary>Заданное армирование SCAD схемы — JSON <see cref="CScore.Import.ScadAssignedRebarFile.ToJson"/>.</summary>
      public const string ScadAssignedRebar = "scad_assigned_rebar";

      /// <summary>
      /// Стальные профили жёсткостей STZ схемы SCAD по сортаменту SCAD (PRF) — JSON
      /// <see cref="CScore.Import.ScadSteelProfileIndex.ToJson"/>.
      /// </summary>
      public const string ScadSteelProfiles = "scad_steel_profiles";
   }
}

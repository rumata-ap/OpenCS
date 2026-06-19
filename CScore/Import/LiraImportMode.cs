namespace CScore.Import
{
   /// <summary>Режим импорта таблицы усилий LIRA SAPR (HTML).</summary>
   public enum LiraImportMode
   {
      /// <summary>Усилия по отдельным загружениям — несколько наборов.</summary>
      LoadCases,

      /// <summary>РСН — несколько наборов (по сочетанию).</summary>
      Rsn,

      /// <summary>РСУ — один набор на файл.</summary>
      Rsu,
   }
}

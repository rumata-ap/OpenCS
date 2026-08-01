namespace OpenCS.Services
{
   /// <summary>Контракт активного редактора FEM-схемы (FemSchemaEditorVM). Регистрируется
   /// в AppViewModel на время жизни страницы редактора, чтобы команды дерева
   /// могли переключать режимы создания и выполнять Save на выходе.</summary>
   public interface IFemSchemaEditor
   {
      /// <summary>Признак несохранённых изменений сессии редактора.</summary>
      bool IsDirty { get; }

      /// <summary>Сохраняет изменения сессии. Возвращает true при успехе.</summary>
      bool Save();

      /// <summary>Режим создания плоской плиты кликами по узлам (тумблер тулбара 3D-вида).</summary>
      bool CreatePlateMode { get; set; }

      /// <summary>Режим создания плоской стены кликами по узлам (тумблер тулбара 3D-вида).</summary>
      bool CreateWallMode { get; set; }

      /// <summary>Режим создания пространственной пластины кликами по узлам (тумблер тулбара 3D-вида).</summary>
      bool CreateSpatialPlateMode { get; set; }
   }
}

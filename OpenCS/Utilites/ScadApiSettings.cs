using System.Text.Json.Serialization;

namespace OpenCS.Utilites
{
   /// <summary>Настройки импорта проекта SCAD (.SPR) через SCADAPIX.dll (таблица settings, ключ «scad_api»).</summary>
   public class ScadApiSettings
   {
      /// <summary>Каталог с SCADAPIX.dll (…\SCAD Soft\…\64). Пусто — искать установку.</summary>
      [JsonPropertyName("dllDirectory")]
      public string? DllDirectory { get; set; }

      /// <summary>Рабочий каталог SCAD (результаты расчёта). Пусто — из SCADX.ini.</summary>
      [JsonPropertyName("workDirectory")]
      public string? WorkDirectory { get; set; }

      /// <summary>Последний импортированный проект .SPR.</summary>
      [JsonPropertyName("lastProjectPath")]
      public string? LastProjectPath { get; set; }

      /// <summary>Читать оси выдачи усилий пластин.</summary>
      [JsonPropertyName("outputAxes")]
      public bool ReadOutputAxes { get; set; } = true;

      /// <summary>Создавать группы КЭ по ЖБ-группам SCAD.</summary>
      [JsonPropertyName("concreteGroups")]
      public bool ConcreteGroupsAsMemberGroups { get; set; } = true;

      public static ScadApiSettings Default => new();
   }
}

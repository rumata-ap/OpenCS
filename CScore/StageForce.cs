namespace CScore;

/// <summary>Описание усилия одного этапа истории нагружения.</summary>
public sealed class StageForce
{
   /// <summary>Режим усилия: "manual" (ручные N/Mx/My), "item" (строка набора), "set" (весь набор).</summary>
   public string Mode { get; set; } = "item";

   /// <summary>Ручное усилие N (кН) при Mode == "manual".</summary>
   public double N { get; set; }
   /// <summary>Ручное усилие Mx (кН·м) при Mode == "manual".</summary>
   public double Mx { get; set; }
   /// <summary>Ручное усилие My (кН·м) при Mode == "manual".</summary>
   public double My { get; set; }

   /// <summary>Id набора усилий при Mode == "item"/"set".</summary>
   public int ForceSetId { get; set; }
   /// <summary>Id строки набора при Mode == "item".</summary>
   public int ForceItemId { get; set; }
}

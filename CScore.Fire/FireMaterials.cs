namespace CScore.Fire;

/// <summary>
/// Фасад коэффициентов условий работы для огневых расчётов.
/// Данные — в <see cref="Sp468Tables"/>.
/// </summary>
public static class FireMaterials
{
   /// <summary>γ_bt(T) по таблице 5.1 СП 468.</summary>
   /// <param name="concreteId">Идентификатор бетона (зарезервирован на будущее).</param>
   /// <param name="aggregateType">Тип заполнителя: silicate, carbonate, lightweight.</param>
   /// <param name="T">Температура, °C.</param>
   public static double GammaBt(string concreteId, string aggregateType, double T)
   {
      _ = concreteId;
      return Sp468Tables.GammaBt(aggregateType, T);
   }

   /// <summary>γ_st(T) по таблице 5.6 СП 468, единый для растяжения и сжатия.</summary>
   public static double GammaSt(FireRebarClass group, double T) => Sp468Tables.GammaSt(group, T);

}

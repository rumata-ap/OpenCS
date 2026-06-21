using System.Collections.Generic;

namespace CScore.PrestressLoss
{
    public enum TensionMethod    { OnSupports, OnConcrete }
    public enum HumidityClass    { Above75, H40_75, Below40 }
    public enum RelaxFormula     { HotRolled, ColdDrawnOrStrand, StabilizedStrand }
    public enum TensionSubMethod { Mechanical, ElectroThermal }

    public class PrestressLossParams
    {
        public TensionMethod Method              { get; set; } = TensionMethod.OnSupports;
        public bool          HeatTreated         { get; set; } = false;
        public HumidityClass Humidity            { get; set; } = HumidityClass.H40_75;
        public bool          ConcreteClassAuto   { get; set; } = true;
        public double        ConcreteClassOverride { get; set; } = 30;
        public List<PrestressGroupParams> Groups { get; set; } = [];
    }

    public class PrestressGroupParams
    {
        public int    AreaId { get; set; }
        public double SigSp0 { get; set; }   // начальное σ_sp [МПа]

        public RelaxFormula     RelaxFormula { get; set; } = RelaxFormula.HotRolled;
        public TensionSubMethod SubMethod    { get; set; } = TensionSubMethod.Mechanical;
        public double           RelaxR       { get; set; } = 2.5;   // r [%], StabilizedStrand

        // Температурный перепад (OnSupports)
        public bool   UseDefaultDeltaT { get; set; } = true;
        public double DeltaT           { get; set; } = 65.0;  // [°C]

        // Деформация формы (OnSupports + Mechanical)
        public bool   UseDefaultFormDeform { get; set; } = true;
        public int    NForms               { get; set; } = 1;
        public double DeltaLForm           { get; set; } = 0;   // [мм]
        public double LForm                { get; set; } = 1;   // [м]

        // Деформация анкеров (OnSupports + Mechanical)
        public bool   UseDefaultAnchorDeform { get; set; } = true;
        public double DeltaLAnchor           { get; set; } = 2.0; // [мм]
        public double LAnchor                { get; set; } = 0;   // [м], 0 = не задано → Δσ_sp4 не вычисляется

        // Трение (OnConcrete)
        public double Omega1    { get; set; } = 0.01;
        public double KFriction { get; set; } = 0.35;
        public double XLength   { get; set; } = 0;   // [м]
        public double Theta     { get; set; } = 0;   // [рад]

        // σ_bpj для ползучести
        public bool   SigmaBpAuto   { get; set; } = true;
        public double SigmaBpManual { get; set; } = 0;  // [МПа]
    }
}

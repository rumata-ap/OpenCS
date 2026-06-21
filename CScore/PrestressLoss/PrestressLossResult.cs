using System.Collections.Generic;

namespace CScore.PrestressLoss
{
    public class PrestressLossResult
    {
        public List<PrestressGroupResult> Groups { get; set; } = [];
        public double PrecompForceFirst          { get; set; }  // P̄_(1) [кН]
        public double PrecompForceTotal          { get; set; }  // P̄_(2) [кН]
        public List<string> Errors               { get; set; } = [];
        public List<string> Warnings             { get; set; } = [];
    }

    public class PrestressGroupResult
    {
        public int    AreaId  { get; set; }
        public string AreaTag { get; set; } = "";
        public double AreaMm2 { get; set; }   // суммарная площадь арматуры [мм²]

        public double SigSp0 { get; set; }   // начальное [МПа]

        // Первые потери [МПа]
        public double DSp1 { get; set; }   // релаксация
        public double DSp2 { get; set; }   // температурный перепад
        public double DSp3 { get; set; }   // деформация формы
        public double DSp4 { get; set; }   // деформация анкеров
        public double DSp7 { get; set; }   // трение

        public double TotalFirst { get; set; }
        public double SigSp1     { get; set; }  // после первых потерь [МПа]

        // Вторые потери [МПа]
        public double SigmaBpj { get; set; }  // напряжение в бетоне [МПа]
        public double DSp5     { get; set; }  // усадка
        public double DSp6     { get; set; }  // ползучесть

        public double TotalSecond    { get; set; }
        public double TotalAll       { get; set; }
        public double SigSp2         { get; set; }  // после всех потерь [МПа]
        public bool   MinLossWarning { get; set; }  // TotalAll < 100 МПа
    }
}

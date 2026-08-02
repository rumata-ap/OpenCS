using System;
using System.Collections.Generic;

namespace CScore.Planar.Fragments
{
    /// <summary>
    /// Параметры нелинейного решателя OpenSees на конкретной стадии.
    /// </summary>
    public class SolverParameters
    {
        public string Algorithm { get; set; } = "NewtonWithLineSearch";
        public double EnergyTolerance { get; set; } = 1e-6;
        public double UnbalanceTolerance { get; set; } = 1e-4;
        public int MaxIterations { get; set; } = 40;
        public double InitialStep { get; set; } = 0.1;
        public double MinStep { get; set; } = 1e-5;
        public double MaxStep { get; set; } = 0.2;
    }

    /// <summary>
    /// Описание одной стадии нелинейного приложения нагрузок на фрагмент.
    /// </summary>
    public class FragmentStage
    {
        public int StageIndex { get; set; }
        public string Name { get; set; } = string.Empty;
        public double SurfaceLoadScale { get; set; } = 1.0;
        public double CutInterfaceScale { get; set; } = 1.0;
        public SolverParameters Solver { get; set; } = new SolverParameters();
    }

    /// <summary>
    /// Конфигурация стадийного нелинейного нагружения вертикального фрагмента.
    /// </summary>
    public class FragmentStageConfig
    {
        public List<FragmentStage> Stages { get; set; } = new List<FragmentStage>();

        /// <summary>
        /// Одностадийный монотонный прогон (100% нагрузок и резов одновременно).
        /// </summary>
        public static FragmentStageConfig CreateDefault1Stage()
        {
            return new FragmentStageConfig
            {
                Stages = new List<FragmentStage>
                {
                    new FragmentStage
                    {
                        StageIndex = 1,
                        Name = "Full Monotonic Stage",
                        SurfaceLoadScale = 1.0,
                        CutInterfaceScale = 1.0
                    }
                }
            };
        }

        /// <summary>
        /// Двухстадийный прогон (Стадия 1: Преднатяг/гравитация -> Стадия 2: Полная нелинейная нагрузка).
        /// </summary>
        public static FragmentStageConfig CreateDefault2Stage()
        {
            return new FragmentStageConfig
            {
                Stages = new List<FragmentStage>
                {
                    new FragmentStage
                    {
                        StageIndex = 1,
                        Name = "Gravity Preload Stage",
                        SurfaceLoadScale = 1.0,
                        CutInterfaceScale = 0.5
                    },
                    new FragmentStage
                    {
                        StageIndex = 2,
                        Name = "Full Non-linear Action Stage",
                        SurfaceLoadScale = 1.0,
                        CutInterfaceScale = 1.0
                    }
                }
            };
        }
    }
}

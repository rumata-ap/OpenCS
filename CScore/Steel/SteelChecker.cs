using System;
using System.Collections.Generic;
using System.Linq;

namespace CScore;

/// <summary>
/// Единая точка входа для проверки стальных сечений по СП 16.13330.2017.
/// </summary>
public static class SteelChecker
{
    public static SteelCheckResult Run(
        SteelSection section, InternalForces forces, DesignContext context)
    {
        var classification = section.Classification;
        var strengthDetails = SteelStrengthCheck.CheckAll(
            section, forces, context);
        var stabilityDetails = SteelStabilityCheck.CheckAll(
            section, forces, context);
        var constructiveDetails = SteelConstructiveCheck.CheckAll(
            section, context);

        var allDetails = new List<CheckDetail>();
        allDetails.AddRange(strengthDetails);
        allDetails.AddRange(stabilityDetails);
        allDetails.AddRange(constructiveDetails);

        double maxUtil = allDetails.Count > 0
            ? allDetails.Max(d => d.Ratio) : 0;

        return new SteelCheckResult
        {
            LoadCaseName = forces.LoadCaseName,
            Utilization = maxUtil,
            Details = allDetails
        };
    }

    public static SteelBatchResult RunBatch(
        SteelSection section,
        IReadOnlyList<InternalForces> forceSets,
        DesignContext context)
    {
        if (forceSets.Count == 0)
            return new SteelBatchResult { AllPassed = true };

        var results = forceSets.Select(f => Run(section, f, context)).ToList();
        return new SteelBatchResult
        {
            Results = results,
            WorstCase = results.OrderByDescending(r => r.Utilization).First(),
            AllPassed = results.All(r => r.IsPassed)
        };
    }
}

public class SteelBatchResult
{
    public List<SteelCheckResult> Results { get; set; } = [];
    public SteelCheckResult WorstCase { get; set; } = new();
    public bool AllPassed { get; set; }
}

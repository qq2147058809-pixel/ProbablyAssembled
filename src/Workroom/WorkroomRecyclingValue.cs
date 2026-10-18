using System;

namespace PCExpansion;

/// <summary>One budget rule for mixed recycling and single-target discard eligibility.</summary>
internal static class WorkroomRecyclingValue
{
    internal static int CurrentPercent => StartingPerks.HasEfficientRecycling() ? 125 : 50;

    internal static long Budget(long totalValue, int percent)
    {
        if (totalValue < 0) throw new ArgumentOutOfRangeException(nameof(totalValue));
        return percent switch
        {
            50 => totalValue / 2,
            // Divide first so valid large budgets do not overflow at value * 5.
            125 => checked(totalValue / 4 * 5 + totalValue % 4 * 5 / 4),
            _ => throw new ArgumentOutOfRangeException(nameof(percent))
        };
    }

}

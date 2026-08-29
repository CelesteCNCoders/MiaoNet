namespace MiaoNet.ClientShared;

internal readonly record struct CleanupStep(string Name, Action Action);

internal readonly record struct CleanupFailure(string StepName, Exception Exception);

/// <summary>
/// Runs independent cleanup steps without allowing one failure to prevent the
/// remaining steps or the final cleanup steps from running.
/// </summary>
internal static class BestEffortCleanup
{
    internal static IReadOnlyList<CleanupFailure> Run(
        IEnumerable<CleanupStep> steps,
        IEnumerable<CleanupStep> finalSteps)
    {
        List<CleanupFailure> failures = [];
        try
        {
            RunSteps(steps, failures);
        }
        finally
        {
            RunSteps(finalSteps, failures);
        }
        return failures;
    }

    private static void RunSteps(
        IEnumerable<CleanupStep> steps,
        List<CleanupFailure> failures)
    {
        foreach (CleanupStep step in steps)
        {
            try
            {
                step.Action();
            }
            catch (Exception e)
            {
                failures.Add(new(step.Name, e));
            }
        }
    }
}

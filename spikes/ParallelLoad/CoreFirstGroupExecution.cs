using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstGroupExecution
{
    public static async Task RunAsync(CoreFirstGroup group, Func<Task> load, CancellationToken cancellationToken)
    {
        group.Status = "running";
        group.Progress.Start();
        try
        {
            await load();
            group.Status = "ok";
        }
        catch (Exception ex)
        {
            group.Status = cancellationToken.IsCancellationRequested ? "timeout" : "failed";
            group.Error = ex.ToString();
        }
        finally
        {
            group.Progress.Stop();
        }
    }
}

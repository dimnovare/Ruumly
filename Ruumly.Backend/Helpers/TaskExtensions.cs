namespace Ruumly.Backend.Helpers;

public static class TaskExtensions
{
    public static void FireAndForget(this Task task, ILogger logger, string opName)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                logger.LogError(t.Exception, "Background task '{Operation}' failed", opName);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}

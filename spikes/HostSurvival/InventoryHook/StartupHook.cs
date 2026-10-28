using System.Runtime;
using System.Text.Json;

internal static class StartupHook
{
    public static void Initialize()
    {
        var destination = Environment.GetEnvironmentVariable("HOST_SURVIVAL_INVENTORY");
        if (string.IsNullOrEmpty(destination))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetName().Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            File.WriteAllText(destination, JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId,
                utc = DateTimeOffset.UtcNow,
                runtime = Environment.Version.ToString(),
                serverGc = GCSettings.IsServerGC,
                assemblies
            }, new JsonSerializerOptions { WriteIndented = true }));
        });
    }
}

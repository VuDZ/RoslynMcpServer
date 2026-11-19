using System.Diagnostics;

namespace AffectedTestsProbe;

internal static class GitRevision
{
    public static async Task<string?> TryHeadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            var output = (await stdout.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

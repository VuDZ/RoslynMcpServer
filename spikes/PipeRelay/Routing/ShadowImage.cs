using System.Diagnostics;

namespace PipeRelay.Routing;

internal static class ShadowImage
{
    public static string Prepare(RouteFiles files)
    {
        var image = files.PathFor("image");
        if (Directory.Exists(image))
        {
            Validate(image);
            return image;
        }

        var staging = files.PathFor("staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
        {
            File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
        }

        Validate(staging);
        Directory.Move(staging, image);
        files.Event("image-published", Guid.Empty, BuildIdentity.Value);
        return image;
    }

    public static Process Start(string image, string optionsFile)
    {
        var dotnet = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet";
        var executable = dotnet ? Environment.ProcessPath! : Path.Combine(image,
            OperatingSystem.IsWindows() ? "PipeRelay.exe" : "PipeRelay");
        // setsid deliberately tests self-exit without relying on the proxy's Unix process group.
        var start = new ProcessStartInfo(OperatingSystem.IsLinux() ? "/usr/bin/setsid" : executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        if (OperatingSystem.IsLinux())
        {
            start.ArgumentList.Add(executable);
        }

        if (dotnet)
        {
            start.ArgumentList.Add(Path.Combine(image, "PipeRelay.dll"));
        }

        start.ArgumentList.Add("routed-host");
        start.ArgumentList.Add(optionsFile);
        var process = Process.Start(start)!;
        process.StandardInput.Close();
        process.StandardOutput.Close();
        process.StandardError.Close();
        return process;
    }

    private static void Validate(string directory)
    {
        if (BuildIdentity.Compute(directory) != BuildIdentity.Value
            || !File.Exists(Path.Combine(directory, "PipeRelay.runtimeconfig.json"))
            || !File.Exists(Path.Combine(directory, "PipeRelay.deps.json")))
        {
            throw new InvalidDataException("The shadow image is incomplete or has a different identity.");
        }

        foreach (var source in Directory.GetFiles(AppContext.BaseDirectory))
        {
            var copy = Path.Combine(directory, Path.GetFileName(source));
            if (!File.Exists(copy) || !System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))
                .SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(copy))))
            {
                throw new InvalidDataException("The shadow image has a partial or modified file.");
            }
        }
    }
}

# Task-02 — временный harness замера

Подготовлен 2026-11-18 для координатора; **не запускался исполнителем**. Не является продуктовым API или тестом с performance-порогом.

Создать временный каталог `AGENT_WORK_ROOT` (вне репозитория, чтобы SDK glob production-проекта не подхватил Program.cs) с `Timing.csproj` и `Program.cs`. Использовать уже установленное координатором окружение SDK 10.0.402, включая DOTNET_ROOT/DOTNET_HOST_PATH/PATH для subprocesses. Имя временной сборки использует уже существующий friend-доступ; основная тестовая сборка в этот процесс не загружается.

`Timing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <AssemblyName>RoslynMcpServer.Tests</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="RoslynMcpServer">
      <HintPath>REPO_ROOT/bin/Release/net10.0/RoslynMcpServer.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

`Program.cs`:

```csharp
using System.Collections.Immutable;
using System.Diagnostics;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Pass at least one assembly path.");
        }

        var solution = new HashSet<string>(StringComparer.Ordinal)
        {
            "RoslynMcpServer", "RoslynMcpServer.Tests", "RoslynMcpServer.LifecycleTestHost",
            "DeadlockWitnessHost", "RoslynMcpPlugin",
        };
        foreach (var path in args)
        {
            var image = new IlAssemblyImage(path, ImmutableArray.CreateRange(File.ReadAllBytes(path)));
            var warmup = IlAssemblyFingerprints.Compute(image, solution);
            if (warmup.Types.Count == 0)
            {
                throw new InvalidOperationException("The timing image has no types.");
            }

            var samples = new double[5];
            for (var i = 0; i < samples.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                var snapshot = IlAssemblyFingerprints.Compute(image, solution);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (snapshot.AssemblyHash != warmup.AssemblyHash || snapshot.Types.Count != warmup.Types.Count)
                {
                    throw new InvalidOperationException("The repeated fingerprint changed.");
                }
            }

            var ordered = samples.Order().ToArray();
            Console.WriteLine($"{path}: bytes={image.Content.Length}, types={warmup.Types.Count}, " +
                $"warmup=1, measured=5, min_ms={ordered[0]:F3}, median_ms={ordered[2]:F3}, " +
                $"total_ms={samples.Sum():F3}, samples_ms={string.Join(',', samples.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))}");
        }
    }
}
```

Команда из корня репозитория, при уже активном SDK10 environment:

```powershell
& 'AGENT_WORK_ROOT/dotnet.exe' run --project 'AGENT_WORK_ROOT/Timing.csproj' --configuration Release -- 'REPO_ROOT/bin/Release/net10.0/RoslynMcpServer.dll' 'REPO_ROOT/RoslynMcpServer.Tests/bin/Release/net10.0/RoslynMcpServer.Tests.dll'
```

Замерять окончательные Release-образы после validation. Сохранить SDK version, UTC-время и stdout в evidence. Файловое чтение, warmup, проверка повторяемости и форматирование вывода находятся вне измеряемого участка. Ничего не исполняется из измеряемой сборки: reader получает её байты, а исполняется только код уже собранного fingerprint-сервиса.

# task-03 — предварительное Windows-падение вне изменённых файлов

- Дата: 2026-11-03.
- Окончательный C# freeze после R7-001, без последующих изменений кода.
- Первый Windows main этой версии: **1687 total /1686 passed /1 failed**.
- Упал неизменённый CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit: dotnet не стартовал, так как временный рабочий каталог RoslynMcpWsGuide уже отсутствовал.
- В этом тесте workDir берётся из process-global Environment.CurrentDirectory (CliProgressTests.cs:153). Неизменённый WorkspaceLoadGuidanceTests.FormatNoWorkspaceLoadedMessage_includes_candidates_under_cwd временно устанавливает такой каталог и удаляет его в finally (строки20,33–37). Это согласуется с существующей гонкой общей cwd; отдельная трасса планировщика не собиралась.
- Оба файла и DotNetCliRunner.cs в diff задачи не менялись. Task-03 исправления не расширялись на эту отдельную гонку.
- Отдельный повтор упавшего теста на тех же binaries: **1/1 passed**. Полный main повторяется без изменения кода; его окончательный результат записывается в финальной validation.
- Это наблюдавшийся failure, он не скрывается успешным повтором и не объявляется исправленным дефектом task-03.

## Первый полный main

## Filtered test run

**Filter:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`
**Match:** Caller-supplied VSTest filter

❌ 1 Tests Failed.

Total: **1687** · Passed: **1686** · Failed: **1**

1. **TestName:** `RoslynMcpServer.Tests.Build.CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit`
   **Error:** System.ComponentModel.Win32Exception : An error occurred trying to start process 'C:\Program Files\dotnet\dotnet.exe' with working directory 'C:\Users\VuDZ\AppData\Local\Temp\RoslynMcpWsGuide\f9b4ada3986045cbaad2fc0a295d6500'. The directory name is invalid.
   **Stack:** at System.Diagnostics.Process.StartWithCreateProcess(ProcessStartInfo startInfo)    at RoslynMcpServer.Services.Build.DotNetCliRunner.RunCoreAsync(String arguments, String workingDirectory, CancellationToken cancellationToken, Nullable'1 timeout) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Build\DotNetCliRunner.cs:line 179    at RoslynMcpServer.Services.Build.DotNetCliRunner.RunWithMetadataAsync(String arguments, String workingDirectory, CancellationToken cancellationToken, Nullable'1 timeout, CliProgressWatch progress) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Build\DotNetCliRunner.cs:line 157    at RoslynMcpServer.Services.Build.DotNetCliRunner.RunWithMetadataAsync(String arguments, String workingDirectory, CancellationToken cancellationToken, Nullable'1 timeout, CliProgressWatch progress) in E:\Devel\RoslynMcpServer-rapid-tia\Services\Build\DotNetCliRunner.cs:line 161    at RoslynMcpServer.Tests.Build.CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit() in E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests\Build\CliProgressTests.cs:line 155 --- End of stack trace from previous location ---   Passed RoslynMcpServer.Tests.B...

2. **TestName:** `RoslynMcpServer.Tests.Build.CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit`

### Execution context
- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.402` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.402\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.402\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **SDK from MSBuild log paths:** `10.0.402`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer-rapid-tia\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** skipped (`noBuild=true`)
- **TestOutput:** StdOut 100000 chars, StdErr 100000 chars (`includeFullOutput`)

## Отдельный повтор

## Filtered test run

**Filter:** `FullyQualifiedName~CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **1** · Passed: **1** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Build.CliProgressTests.Test_orchestration_reports_build_then_test_labels_with_previous_exit`

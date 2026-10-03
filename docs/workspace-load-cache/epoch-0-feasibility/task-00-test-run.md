# Task-00 — focused baseline runner output

Дата: 2026-10-03. Это проверка существующего кода, не результат hydrate spike.

Runner: `mcp__RoslynMcpServer__run_test_by_filter`.

```json
{
  "workspacePath": "E:\\Devel\\RoslynMcpServer\\RoslynMcpServer.Tests\\RoslynMcpServer.Tests.csproj",
  "configuration": "Release",
  "filter": "FullyQualifiedName~RoslynMcpServer.Tests.Workspace",
  "noBuild": false,
  "noRestore": true,
  "timeoutSeconds": 600
}
```

Ниже сохранён ответ runner без изменения чисел. Отдельный TRX этим вызовом не предоставлен; skip counter в ответе отсутствует. Старые TRX из TestResults не использованы как evidence текущего запуска.

## Filtered test run

**Filter:** `FullyQualifiedName~RoslynMcpServer.Tests.Workspace`
**Match:** Caller-supplied VSTest filter

## Filtered tests passed

Total: **275** · Passed: **275** · Failed: **0**

Matched tests: `RoslynMcpServer.Tests.Workspace.WorkspaceDiagnosticFormatterTests.IsBlockingLoadFailure_true_for_wrapped_msbuild_error', 'RoslynMcpServer.Tests.Workspace.WorkspaceDiskPathFilterTests.IsPathUnderDirectory_does_not_match_prefix_sibling', 'RoslynMcpServer.Tests.Workspace.SkippedWorkspaceWriteDiskPolicyTests.ShouldWriteSkippedPathToDisk_only_for_unknown_snapshot_paths', 'RoslynMcpServer.Tests.Workspace.SolutionProjectTargetResolverTests.TryResolve_rejects_csproj_extension', 'RoslynMcpServer.Tests.Workspace.WorkspaceNonCSharpDiskSyncTests.Syncs_additional_file_and_analyzer_config_but_not_markup_or_evaluation`

- **dotnet host:** `C:\Program Files\dotnet\dotnet.exe` (64-bit MCP process)
- **dotnet --version:** `10.0.300` (from working directory)
- **Inherited MSBuildSDKsPath:** `C:\Program Files\dotnet\sdk\10.0.300\Sdks`
- **Inherited MSBUILD_EXE_PATH:** `C:\Program Files\dotnet\sdk\10.0.300\MSBuild.dll`
- **SDK env action:** `stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)`
- **WorkingDirectory:** `E:\Devel\RoslynMcpServer\RoslynMcpServer.Tests`
- **DOTNET_CLI_UI_LANGUAGE:** `en-US`
- **Configuration:** Release
- **Platform:** (SDK/solution default)
- **BuildArgs:** (none)
- **PreTestBuild:** yes (`dotnet build` then `dotnet test --no-build`)
- **TestOutput:** StdOut 2500 chars, StdErr 1000 chars


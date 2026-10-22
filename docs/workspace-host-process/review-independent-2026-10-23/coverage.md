# Coverage — independent review, 2026-10-23

## Reviewed target

- Checkout: `C:/Users/VuDZ/.codex/worktrees/workspace-host-process/RoslynMcpServer`.
- Reviewed commit: `dc1b1c5f260031be7e1298bcb82498f4385fc730`.
- Branch observed: `research/workspace-host-process`.
- Reviewer session identifier available in this execution: `/root/independent_plan_review` (canonical subagent task name). A separate platform session UUID and a verified model identifier were not exposed; neither is inferred.
- Review date: 2026-10-23.
- Scope: fresh independent adversarial review of the updated research plan. This is not acceptance of design, implementation, E0 completion, or production activation.

All eight target Markdown files were read in full:

1. `../README.md`.
2. `../epoch-0-design-and-spike/README.md`.
3. `../epoch-0-design-and-spike/task-00-process-state-inventory-med.md`.
4. `../epoch-0-design-and-spike/task-01-architecture-decision-hi.md`.
5. `../epoch-0-design-and-spike/task-02-bridge-spike-hi.md`.
6. `../epoch-0-design-and-spike/task-03-pipe-relay-spike-med.md`.
7. `../epoch-0-design-and-spike/task-04-proxy-routing-and-recovery-spike-hi.md`.
8. `../evidence/2026-10-23-plan-update.md`.

The worktree was clean at initial escalated Git inspection. HEAD and branch matched the requested version. No other reviewer output, defence, arbitration, or later decision ledger was read.

## Context inspected

All repository paths in this section refer to the reviewed checkout, not to the original experimental checkout.

- `AGENTS.md` in full; repository enumeration found no nested AGENTS.md. The supplied coordinator instructions were also applied.
- `docs/ARCHITECTURE.md` in full.
- `.cursor/rules/roslyn-mcp-pitfalls.mdc` in full, with particular attention to 13, 20, 26 and 27.
- `Program.cs`, `Hosting/RoslynMcpServiceCollectionExtensions.cs`, `RoslynMcpServer.csproj` in full.
- `Hosting/McpToolActivationService.cs`, `Hosting/McpRuntimeToolCollection.cs`, `Tools/McpToolProgressReporter.cs`, `Tools/ServerLifecycleTools.cs` in full.
- Targeted source inspection: `Services/Workspace/SolutionManager.cs` (load argument resolution, load/prepare lock, physical load/cache path, publication boundary, reset and disposal); `Tools/WorkspaceTools.cs` (load metadata, call into load/prepare, error handling); `Services/Analyzers/InProcessAnalyzerAssemblyLoader.cs` (process-lifetime state, identity collision checks and load path).
- Targeted source inspection/search: `Diagnostics/MsBuildBootstrapper.cs` (startup registration and SDK selection), `Services/Build/SessionBuildState.cs` (proof/context state and serialization limits), `Config/RoslynMcpFileSettings.cs` (configuration locations and cwd resolution), `Plugins/PluginStartup.cs` (discovery/load before provider and plugin report), `Tools/UtilityTools.cs` (scratchpad cwd semantics).
- `publish-and-verify.ps1`: targeted inspection of holder discovery, kill policy and publish/reload flow.
- `RoslynMcpServer.Tests/Hosting/McpToolGroupEnablementIntegrationTests.cs` in full; `RoslynMcpServer.Tests/Support/McpInitializeHandshake.cs` in full.
- `RoslynMcpServer.Tests/Build/BuildProgressIntegrationTests.cs`: live stream-client build tests and progress/no-token behavior.
- `RoslynMcpServer.Tests/AnalyzerLifecycle/AnalyzerLifecycleHostTests.cs`: exact V1/V2 cached/reset/restart tests, plus targeted test-name searches in its Publication and Regression companion files.
- `.github/workflows/test-suite.yml` in full.
- `docs/workspace-load-performance/epoch-0-stage-measurement/task-01-load-stage-timings-med.md`: status, purpose, seams, planned event fields and initial checks. It remains a planned prerequisite in this checkout, not a shipped logging API.

Installed SDK context was examined independently of another workspace's runtime state:

- Package DLL metadata and accompanying XML documentation at `C:/Users/VuDZ/.nuget/packages/modelcontextprotocol/2.2.0/lib/net10.0/` and `C:/Users/VuDZ/.nuget/packages/modelcontextprotocol.core/2.2.0/lib/net10.0/`.
- Both DLL assembly versions: `2.2.0.0`, matching the target csproj package version `2.2.0`.
- `ModelContextProtocol.dll` SHA256: `8CD6F626C8D6A0A5FF3E154789D3FCDE2B1D66A595C0EF0198E092C2C30FE93A`.
- `ModelContextProtocol.Core.dll` SHA256: `B1752096EA9D2F38BA88E467F04AEA43FB300D9A54D881C165F62B69028EE3AB`.
- XML entries checked: `StreamServerTransport`, `StreamClientTransport`, `McpServer.Create`, `McpServerOptions.ToolCollection`/`Handlers`, `StdioClientTransport`, `WithStdioServerTransport`, `SingleSessionMcpServerHostedService`.

## Context not inspected

- The original `E:/Devel/RoslynMcpServer` source/workspace and its uncommitted experiments. The managed worktree's `.git` pointer and shared Git administrative index were read only to establish provenance and preservation; no source from that checkout was used.
- Other `review-*`, defence, arbitration and later decision documents.
- Cache option B / H-003 drafts from the separate context; they are not implicit dependencies of this target.
- Exhaustive plugin implementation contracts, the entire analyzer historical archive, every production tool/caller and every existing test. The review used the concrete seams above rather than claiming a repository-wide audit.
- C# style review: no style finding was made and no C# file was created or edited, so `docs/code-style.md` was not required for this task.

## Source/code verification

Verification here means static inspection, not a test pass.

- Current DI ties one stdio session to singleton workspace and activation state. The plan correctly requires separation rather than forwarding unchanged core registration to every proxy.
- `Program.cs` registers MSBuild and loads Roslyn services at startup, reads configuration against executable/cwd locations, and performs plugin startup before building the provider. The lightweight proxy and bootstrap assumptions therefore need the scheduled experiments.
- `SolutionManager.LoadAndPrepareAsync` resolves omitted values against existing/file settings under its lock. `LoadCoreAsync` disposes the old workspace before a fresh open; existing code cannot provide transactional proxy-switch rollback merely by reusing this method. The plan explicitly assigns facade binding/defaults and host handoff semantics to design and integration.
- Reset disposes the workspace but not CLR-loaded analyzer assemblies. The exact V1/V2 lifecycle tests require fresh process execution for V2. The plan explicitly distinguishes proxy restart from host analyzer restart and calls out demotion memory/identity.
- Current activation and integration tests distinguish `2025-11-25` from `2026-07-28` subscription behavior. The plan's two-protocol matrix reflects this baseline.
- SDK package XML confirms the available stream transports and per-server options seams. It also documents that the single-session hosted service requests application termination when the session finishes and that StdioClientTransport owns child-process lifecycle. These are reasons for task-04's explicit EOF/forced-kill/idle experiments, not evidence that combined-role lifetime already works.
- Publish tooling kills matching holders in its existing workflow; the plan appropriately treats combined-role shadow bootstrap and original-publish replacement as unproved experiments.
- The plan's validation requirements are consistent with the applicable source-structure, Release main-suite and additional AnalyzerLifecycle obligations.

## Coverage limitations

- No build, tests, Roslyn integration, IPC run, process-kill experiment, ACL test, memory measurement, latency measurement, or platform execution was performed. This review changes only its own Markdown results.
- This checkout has no `bin` or `obj` directory. No solution output assembly was available for decompilation. Installed package identity and XML were checked; SDK implementation internals were not decompiled or executed.
- Roslyn MCP was not used: no loaded-workspace state from the original checkout was imported as evidence. Source inspection used explicit paths in this checkout.
- Actual host ownership, fencing, retry budgets, descriptor fields, session defaults, last-waiter policy, no-candidate outcome and handoff failure boundary remain design choices for task-01, constrained by the agreed scope. Their future correctness is not established here.
- Windows/Linux/macOS lifecycle and channel guarantees remain unproved. The target explicitly requires recording unverified platforms rather than awarding them a pass.
- The strongest runtime requirements are scheduled across task-03, task-04 and task-02. Their presence is sufficient for this draft plan review, but does not substitute for later red/green evidence or real multi-process/Roslyn acceptance.
- The review produced zero findings. This is bounded by the inspected source areas and the research-plan status; it is not a production safety claim.

## Preservation and final self-check

- No production/source, tests, target documents, task status/index, review statistics, Git index or branch were intentionally changed. Only this review directory is an output.
- Target SHA256 hashes and the worktree index hash were captured before creating this directory for final preservation checks.
- PASS: all eight target SHA256 hashes, HEAD, branch and the worktree index hash were unchanged after result creation; tracked and staged diffs were empty. Git status contained only this review directory. Required sections, zero-finding consistency, the local coverage link, whitespace and merge-marker checks passed. The execution marker was absent during self-check and is written last.

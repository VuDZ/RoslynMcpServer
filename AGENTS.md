# Code style for agents

C# style for this repository is in [docs/code-style.md](docs/code-style.md), relative to the repository root.

- Read that file in full once before the first creation or edit of C# code in the current task, including tests.
- Read it in full when reviewing style or discussing changes to these rules.
- Planning, research, and discussion that do not change code do not require reading the file, unless the task is about the style itself.
- If the task moves from discussion to implementation, read the file before the first code change.
- If `docs/code-style.md` changes on disk during the task, read it again in full before the next code change.
- Apply the rules to new code and to lines this task already changes. That includes the section "Applying the rules to existing code": leave the rest of a file as it is, including member order in types this task does not edit.
- If you launch a subagent that will create, edit, or review the style of C# code, its prompt must require it to read `docs/code-style.md` in full before the first edit or before style comments, and to apply those rules only to new code and to lines that subagent changes.
- Text that lives in code is English: comments, XML docs, log and diagnostic messages, exception messages, test failure text, and asserted fragments. Localized external output kept as test data stays verbatim; project documentation keeps its current language.

# Test selection and final validation

- Use focused tests during implementation for quick feedback. Tests named by a plan or epoch are a minimum, not the complete validation scope.
- After changing production C# code, run all tests in `RoslynMcpServer.Tests/SourceStructure`. They analyze the entire production source tree, including transitive calls and registered lambdas, so searching for references to changed symbols is not enough to select them.
- Before final acceptance or committing code or test changes, build the solution and run the complete main CI suite (`Category!=AnalyzerLifecycle`) in the `RoslynMcpServer.Tests` project against the final code, in Release configuration as specified in `.github/workflows/test-suite.yml`. After a fix, rerun affected tests during development and the complete main suite before acceptance or commit.
- Run `AnalyzerLifecycle` additionally when changes can affect analyzer/shadow-copy lifecycle, the workspace load/prepare/publication paths used by that lifecycle, the lifecycle host, or its test/build configuration. Enable `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` for that run. Unrelated changes do not require this specialized suite.
- With Roslyn MCP available, prefer `run_dotnet_build`, `run_specific_test`, and `run_dotnet_test`. If a specialized tool cannot express the required integration environment, use an available runner that can. With another harness, use its supported build/test runner for the same checks. A zero-test run is not a pass; report failures, skips, timeouts, and unavailable integration environments instead of claiming full validation.
- In subagent workflows, the coordinator owns final build/test validation. Independent review and specification acceptance do not replace running the tests.
- Preserve exact inventories of known structural-analysis limits. When production changes legitimately add a limit, explain it and update the expected identity and kind; do not merely loosen the count or ignore unexpected limits.

# Task status bookkeeping

- After completing a documented task, update its explicit status in the task file and the parent README or task index before the final response. Record the date, accepted scope, and a link to the validation or acceptance report; a report alone does not replace the task status.
- Include those status fields and evidence links in the task completion scope even when the implementation packet only lists code and evidence files. These bookkeeping edits must not change requirements, implementation contracts, or activation permissions.
- Keep task, index, report, and packet execution-status summaries consistent. Mark a task accepted only after its required validation and acceptance; unfinished, blocked, or deferred work must retain an accurate status and reason.
- In subagent workflows, the coordinator owns the final status updates after integration, review, and validation. Acceptance of an isolated experiment does not imply production readiness, epoch completion, or public activation.

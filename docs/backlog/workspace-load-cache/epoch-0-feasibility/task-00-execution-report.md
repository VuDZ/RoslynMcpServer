# E0/task-00 — execution evidence

Дата: 2026-10-03 (Europe/Moscow). Scope: current-baseline audit и execution
contract для feasibility; production/test C# не меняется.
Статус: **accepted, docs-only** после независимого review и проверки координатора.

## Provenance и границы

- Historical spec pin: `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source 1.3.21.
- Audited HEAD: `07859db586c68452771c88ba76409bb78e0e289f`; source Version 1.5.4,
  Assembly/FileVersion 1.5.4.0. Аудит относится к этому HEAD плюс исходному dirty tree.
- Исходные untracked paths: `.agents/skills/prepare-github-release/SKILL.md`,
  `.cursor/rules/roslyn-mcp.mdc`, `iotester.py`, `scripts/Get-McpToolUsage.ps1`.
  Tracked изменений перед работой не было. Эти пользовательские файлы не изменялись.
- Running MCP установлен отдельным `get_mcp_server_info`: 1.5.4.0,
  `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
  binary modified `2026-10-01T17:41:09.1475207+03:00`, workspace not loaded,
  profile full, 54 tools. Совпадение номера версии не доказывает идентичность binary HEAD.
- `dotnet --info`: SDK 10.0.300, MSBuild 18.6.3+caa81fa49, runtime 10.0.8,
  Windows 10.0.26200, win-x64; SDK root `C:\Program Files\dotnet\sdk\10.0.300\`.
  Package pin Roslyn — 5.9.0, MSBuild assemblies — 18.6.3.
- Хост: `VUDZ-PC`; `Environment.ProcessorCount` = 16; CPU из registry:
  AMD Ryzen 7 9800X3D 8-Core Processor. Physical RAM в этой сессии не подтверждена:
  `Get-CimInstance Win32_ComputerSystem/Win32_Processor` вернул Access denied.
  Это не новый performance baseline и не замена office-host report.

## Commands и outcomes

1. `git rev-parse HEAD`, `git status --porcelain=v1 --untracked-files=all` —
   исходные commit/dirty paths зафиксированы выше.
2. `Get-Content -Raw` task-00, spec, task-execution, cache-contract, verification,
   UNRESOLVED-v2, task-01…05, epoch-1 spec, csproj и CI workflow — прочитаны;
   текущие load/prepare/flush/write/publication paths проверены по исходникам.
3. `rg --files` / `rg -n` по production, tests и docs — inventory и ссылки в
   [audit](current-baseline-audit.md). SDK spike harness/raw evidence не найден;
   NonCSharpDocumentTextSyncSpikeTests не является таким harness.
4. MCP `get_mcp_server_info` — успешно; это read-only запрос без load.
5. MCP `run_test_by_filter` с Release, noBuild=false, noRestore=true и
   `FullyQualifiedName~RoslynMcpServer.Tests.Workspace` — **275/275 passed,
   0 failed**, pre-test build включён. Полные параметры и ответ runner сохранены
   в [task-00-test-run.md](task-00-test-run.md). Отдельный skip counter и TRX
   runner не предоставил; старые TRX не использованы.
6. Полный solution build/main CI suite, SourceStructure suite и opt-in
   AnalyzerLifecycle — **not run**. По task-execution docs-only изменение их не
   требует. Pre-test project build не выдаётся за полный solution build.
7. SDK dependency spike, hydrate host, independent equivalence, V23 cross-process
   disk hit, metadata comparator и performance — **not run**: это downstream
   task-01…05 и последующие эпохи, а не результаты этого audit.
8. Сверка документации: все 44 named test anchors audit найдены в test source;
   относительные Markdown-ссылки в девяти output documents ведут в существующие
   файлы. `git diff --check` — pass (только уведомления Git LF→CRLF).
   `git diff --name-only -- '*.cs' '*.csproj' '*.sln' '*.yml'` — пусто.

## Outputs и ownership

- [current-baseline-audit.md](current-baseline-audit.md): автор `/root/baseline_audit`.
- [design.md](design.md) и четыре implementation packets в `design/`:
  автор `/root/execution_design`; финальный analyzer-inventory guard интегрировал
  `/root` после остановки author-сессии по usage limit.
- Карта эпохи и этот evidence report: координатор `/root`.
- Независимый reviewer task-00: `/root/contract_review`; не автор документов
  выше. Это review design-task, не подмена E0/task-06 independent acceptance.

## Verification limits и следующий шаг

Code presence, текущий focused test pass и будущая hydrate equivalence разделены.
Новые assertions из design ещё не выполнены. Task-02 обязан воспроизвести raw
sdk-project-v1 evidence и дополнить findings packet task-03 до его dispatch.
Unknown в обязательной категории не становится supported из-за зелёного baseline.
U-ARB-03 остаётся gate public activation и не блокирует технический spike.

## Review и acceptance

Независимая reviewer-сессия `/root/contract_review` прочитала audit, design,
четыре packets, spec/contract и current paths. Последовательность исправлений:

- Coordinator review: исправлены exact имя flush method, ownership общего
  AdmissionEvidence, описание MSBuild side effects и consumed write declarations.
- Independent review: PathOrRegion/hash заменён точным structured region contract
  с Include/Exclude, empty regions и manifest encoding; добавлен явный analyzer
  revalidation seam и отказ от DLL load при непригодном evidence.
- Final guard: независимо полный compiler analyzer inventory и exact DTO/evidence
  analyzer/dependency sets проверяются до empty-analyzer shortcut. Отрицательные
  witnesses покрывают удаление всех analyzer entries и неполный dependency set.
  Изменение согласовано в design и packets-01/04; declarations сверены отдельно.

Финальный ответ reviewer: **«Must-fix не осталось… Docs-only результат E0/task-00
принимается»**. Последний guard перепроверен той же независимой сессией после
координаторской правки. Reviewer не запускал тесты; runner evidence принадлежит
координатору. Это review task-00, не E0/task-06 и не приёмка будущего production.

Diff состоит из двух изменённых документов (карта и audit) и семи новых
документов (design, четыре packets, два execution evidence files).
Production/test/build diff отсутствует; исходные пользовательские untracked
файлы сохранены. Коммит не создавался.

## Decision authorities

- **Experiment allowed: yes**, в пределах изолированных E0/task-01 и task-02
  по принятым contracts. Это разрешённый следующий маршрут, не утверждение,
  что эксперименты уже выполнены. Следующие task в этой сессии не запускались.
- **Implementation allowed: no для production lifecycle/cache** на основании
  одного task-00. Исследовательский код task-01/02 допустим в пределах их packets;
  production verdict остаётся E0/task-06 после требуемого evidence.
- **Public activation allowed: no**. Эквивалентность, store, cross-process hit,
  workload/budgets и остальные gates ещё не доказаны; U-ARB-03 открыт.
- **Next epoch allowed: no**. Сначала E0/task-01…05 и независимый task-06.
  Task-03 запускается только после accepted task-01/task-02 и дополнения Findings
  автором task-02; task-04/05 сохраняют свои Depends on.
- **Series complete: no**. O1–O8, включая O4/O5, этим docs-only результатом не закрыты.

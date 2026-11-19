# Task-03 — финальная validation и приёмка

Исторический результат приёмки. Дополнительное ревью готового `d78bb2d` 2026-11-18 подтвердило два новых
открытых P2 ([POST-F-01/02, воспроизведение и передача исправлений](task-03-post-acceptance-review-sdk10-2026-11-18.md)).
Они исправлены, task-03 повторно принята 2026-11-18: [исправления и итоговая validation](task-03-post-acceptance-fixes.md).
Приведённые ниже результаты прежних прогонов сохраняются; новые сценарии ими не покрыты.

Дата: **2026-11-18**, Linux x64. Статус: **accepted** координатором после
ACCEPT ревью раунда 2. Scope — внутренний сервис двух снимков выходных сборок
в сессии («до правки» и текущий) с графами обоих, встроенный в публикацию baseline; срок жизни по
[M-10](../README.md#решения-владельца-по-плану). Инструмент `estimate_affected_tests`, его ответ, версия и
CHANGELOG не меняются; task-04 не начата.

## Ход ревью

- Раунд 1 — MUST-FIX: F-01, не проверялась отсечка позднего результата,
  пришедшего во время чтения файла. Необязательные N-01, N-04, N-05, N-06 исправлены тем же исполнителем.
  N-02 и N-03 записаны как ограничения в [отчёте](../task-03-implementation-report.md), N-02 — как
  требование к task-04.
- Раунд 2 — ACCEPT.
- Общее ревью координатора: замечаний к production-коду нет. По необязательному N2-01 координатор
  переставил две строки в `BaselineSession.Publish`: фоновый захват baseline запускается до отмены прежнего
  чтения сборок, чтобы исключение в отмене не оставило `_captureTask` незавершённой. N2-02 (вызывающий
  может отбросить результат `Start`/`Interrupt`) оставлен как есть: все production-вызовы его отменяют.

## Окружение и команды

Baseline `03648ad`, рабочее дерево с окончательным кодом. SDK **10.0.114**, Release, `TMPDIR=$HOME_DIR/testtmp`,
прогоны последовательные, без конкурирующих сборок и тестов. Фильтр main — Linux shard
`.github/workflows/test-suite.yml`.

```bash
dotnet build RoslynMcpServer.sln -c Release --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter 'FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~RoslynMcpServer.Tests.Testing.Snapshots'
ROSLYN_MCP_ANALYZER_LIFECYCLE=1 dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter 'Category=AnalyzerLifecycle'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter 'Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk'
```

## Результаты

- **Release build solution:** 0 ошибок, 3 прежних предупреждения вне задачи: CS8603
  `NuGetFallbackAssemblyResolver.cs:72`, CS8601 `HostSession.cs:1179`, xUnit1031
  `WpfTemporaryProjectCallbackTests.cs:195`.
- **Focused:** **147/147** (SourceStructure 123 + Snapshots 24), 0 skips.
- **AnalyzerLifecycle:** **13/13**, 0 skips. Запускался, потому что задача меняет публикацию baseline.
- **Main (Linux):** **2421/2421**; total = executed = passed = 2421, failed/error/timeout/aborted/notExecuted = 0.
  Начало `2026-11-18T16:55:36+01:00`, конец `17:07:39+01:00`.

Сырые артефакты лежат в игнорируемом `TestResults/task03-final`. SHA256:

```text
focused.trx        DE481B2E7621A1EA6D6E43AAB4203E9EA5EBB5B133302A9680242FEC80C249A8
lifecycle.trx      683081041CB79578864334A5AC871A1BD4077AAA787F5AAB44AB0D5A43E441A7
main.trx           9B27DFD099316243658E386D58A3A329FEE8FA4124700687FEE2C1AB50F18B35
release-build.log  9CCB0C0EA5BF6CF91EAB66ED070E1C12EE495F5A6893A0579BAC952A1458F3D7
```

## Границы

Windows и RequiresPinnedSdk не запускались. Полная загрузка этого репозитория через MSBuild с замером
первого чтения не делалась: время первого чтения измерено на сервисе отдельно (см. отчёт). Время на solution
владельца покажет task-06.

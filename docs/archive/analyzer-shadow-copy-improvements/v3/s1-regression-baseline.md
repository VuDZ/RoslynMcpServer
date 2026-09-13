# S1 — закрепить три воспроизведения

Статус: **выполнено; независимая приёмка
[принята](s1-acceptance.md)** (красный baseline, не фикс). Зависимости: нет.
Результат шага: постоянные regression-тесты V3-R1, V3-R2 и V3-R3.

## Основание

Штатные проверки v2 проходят, но не проверяют продолжение сессии после
ошибки prepare, ошибку capture как отказ semantic publication и конфликт
двух записей одной сессии. Временные тесты ревью выявили все три дефекта
на v1.3.15 через настоящий MSBuildWorkspace в изолированном lifecycle host.

Targets: `RoslynMcpServer.Tests/AnalyzerLifecycle/`, `Epoch1HostOps`,
`GeneratorConsumerFixture`, `LifecycleHostClient`. Существующие host operations
`holdOverlayEdit`, `applyHeld`, `updateDocument`, `injectPrepareFailure`,
`injectCaptureFailure` и `oracle` уже позволяют построить воспроизведения.

## Работа

Добавить три постоянные проверки с assertions целевого безопасного поведения.
Каждый сценарий получает отдельный host-процесс и fixture
`SdkDefaultCorrectPath`. Сначала выполнить успешный build генератора.

| Сценарий | Действия теста | Целевое ожидание |
| --- | --- | --- |
| R1 | Opt-in load; удержать candidate с текстом A; записать текст B; применить удержанный candidate | `PreflightRejected`, нет сохранённых путей операции; диск и опубликованный текст остаются B |
| R2 | Инъекция ошибки prepare; opt-in load; проверить исходную fail-closed публикацию; text edit; semantic oracle | Real reference не появляется; real assembly не загружается; marker отсутствует |
| R3 | Инъекция corrupt capture; opt-in load; проверить статус capture; semantic oracle | Semantic snapshot недоступен; real assembly не загружается; marker отсутствует |

Для R1 проверять байты consumer и `.csproj`, а не только `Ok=false`:
текущий отказ Roslyn случается после записи. Для R2/R3 фиксировать published
reference, process loaded path и exact marker; отсутствие диагностики не
служит доказательством безопасности. До исправления R2/R3 измерен exact V1
из real output, а R1 дал старый текст на диске после reconciliation.

Тесты должны пользоваться production published accessor, не test-only raw
workspace oracle. Вывод ошибки сделать компактным: status, reason, paths,
marker и сравнение текстов, без многокилобайтного JSON, скрывающего результат
в усечённом MCP-ответе.

## Приёмка

- Все три теста действительно запускаются; skip, timeout или ошибка сборки
  не считаются красным baseline.
- На неисправленной базе assertions обнаруживают именно R1–R3. Если код уже
  изменён, зафиксировать новую базу и фактическое поведение, не искусственно
  возвращать дефект.
- Ожидания описывают безопасное поведение. Не закреплять потерю текста и
  загрузку real output как желаемый зелёный результат.
- Тесты сохраняются для S2–S4. Красный baseline этого шага не означает
  готовность выпуска и не должен отдельно выпускаться как исправление.

## Результат

Выполнено 2026-09-12. Это сохранённый красный baseline, не исправление и не выпуск.

### Источники и среда

- **HEAD:** `671d1ae` (`docs: add v3 plan for stale-write and fail-closed holes`)
- **Commit шага:** этот коммит;
  `RoslynMcpServer.Tests/AnalyzerLifecycle/V3RegressionBaselineTests.cs`,
  `publishedDocument` в `LifecycleTestHost/HostSession.cs`, этот файл
  и [s1-acceptance.md](s1-acceptance.md)
- **Версия csproj / MCP:** `1.3.15` (тесты и test-host; production/MCP binary не менялись, bump не делался)
- **OS / host:** Windows, x64 process
- **SDK:** `10.0.204` (`run_dotnet_build` / `run_specific_test`)
- **MCP binary:** `RoslynMcpServer` v1.3.15.0,
  `bin/Release/net10.0/win-x64/publish/RoslynMcpServer.exe`

### Команды

1. `load_workspace` → `RoslynMcpServer.sln`
2. `run_dotnet_build` → `RoslynMcpServer.sln` (затем повторно
   `-t:RoslynMcpServer_Tests` после правки assertions)
3. `run_specific_test` class=`V3RegressionBaselineTests`, `noBuild=true`,
   `timeoutSeconds=600`

### Фактические результаты

Все три теста **запустились** (не skip, не timeout, сборка успешна).
Итог: **3 failed / 0 passed**, ~25 с. Assertions описывают безопасный контракт
и на неисправленной базе v1.3.15 падают именно на свойствах R1–R3.

| ID | Тест | Фактический отказ | Соответствие дефекту |
| --- | --- | --- | --- |
| R1 | `V3_R1_same_session_stale_candidate_is_rejected_before_any_write` | `status=ReconciliationSucceeded reason=try-apply-rejected`; `saved=1:Consumer/MarkerConsumer.cs`; диск и published содержат held A; `.csproj` byte-identical | Да: stale same-session candidate пишет A после B |
| R2 | `V3_R2_prepare_failure_stays_fail_closed_after_text_edit` | После load fail-closed держится (`shadow=False`, `publishedRef=-`). После text edit: `publishedRef=netstandard2.0/Generator.dll`, `realPublished=True`, `realLoaded=True`, `realProcess=True` | Да: edit возвращает real reference и грузит real DLL |
| R3 | `V3_R3_corrupt_capture_does_not_publish_or_execute_real_output` | `capture=Failed`, `shadow=False`, `publishedRef=netstandard2.0/Generator.dll`, `realPublished=True` (ещё до oracle) | Да: corrupt capture оставляет real reference в published snapshot |

Компактные строки отказа (без JSON):

```
R1 apply ok=False err=try-apply-rejected status=ReconciliationSucceeded
reason=try-apply-rejected diskEqExpected=False publishedEqExpected=False
diskHasHeldA=True publishedHasHeldA=True csprojUnchanged=True
saved=1:Consumer/MarkerConsumer.cs

R2 after-edit oracle ok=True status=FullSuccess exec=LoadFailed
publishedRef=netstandard2.0/Generator.dll loaded=netstandard2.0/Generator.dll
marker= oracle=False/no-constant realPublished=True realLoaded=True
realProcess=True realLoader=False

R3 load ok=True capture=Failed shadow=False
publishedRef=netstandard2.0/Generator.dll realPublished=True
realLoaded=False realProcess=False
```

### Самопроверка

- Каждый сценарий: отдельный host-процесс + `SdkDefaultCorrectPath` + успешный
  `build` генератора до load.
- Semantic checks идут через `GetPublishedSolutionAsync` / `oracle` без
  `oracleSource=workspace`. R1 читает published text тем же accessor
  (`publishedDocument`).
- Отказ компактный: status, reason, paths, marker, сравнение текстов.
- Ожидания не закрепляют потерю текста и загрузку real output как зелёный
  результат. Тесты остаются для S2–S4.

### Ограничения

- **R2 marker:** ревью v1.3.15 измеряло exact `V1` из real output. Этот прогон
  подтвердил published real path и process load; `SourceGeneratorOracle`
  вернул `no-constant`, не `V1`. Безопасность по-прежнему опровергается
  путями, не отсутствием маркера.
- **R3 execution:** assertion останавливается на published real reference,
  поэтому exact marker из oracle в этом прогоне не измерялся. Это более раннее
  звено того же дефекта.
- MCP parser повторяет упавшие имена в хвосте (`Showing first 5 failures`);
  уникальных тестов три.
- Красный baseline не выпускается и не означает готовность v3.

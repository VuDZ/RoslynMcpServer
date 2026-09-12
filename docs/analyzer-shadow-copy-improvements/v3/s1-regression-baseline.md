# S1 — закрепить три воспроизведения

Статус: **не выполнено**. Зависимости: нет.
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

Не выполнено.

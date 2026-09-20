# Карточки ответа: `s1-cli-runner-progress-seam.md`

---

ID: S1-01
Verdict: ACCEPT

Критика верна: SDK биндит `IProgress<ProgressNotificationValue>` всегда, поэтому
на живом `run_dotnet_build` без token ветка «без watch» была недостижима, а
формулировка «поведение байт-в-байт как сейчас» — ложной. Рецепт приемлем:
распознать no-op singleton и считать его отсутствием progress.

Требования:
S1 / «Работа»: «Если token нет — поведение байт-в-байт как сейчас»; README
каталога / «Фиксированные решения»: «Если SDK/`RequestId` не даёт progress
token — no-op, build как сейчас». Shipped: `RequestServiceProvider.GetService`
возвращает `TokenProgress` при token и внутренний `NullProgress` без него.

Что менять:
`Tools/McpToolProgressReporter.TryCreate` возвращает `null` для
`ModelContextProtocol.NullProgress` (сравнение по полному имени типа), поэтому
`DotNetBuildProbe` не создаёт `CliProgressWatch` и runner не заводит таймер.
Контракт «no-op → нет таймера» не ослабляем до «нет уведомлений», как
предлагал альтернативный вариант рецепта: canonical MUST про byte-for-byte
выполним за 4 строки.

Последствия:
Совместимость: зависимость от имени внутреннего типа SDK (митигация и
fallback — NEW-D-01 в `summary.md`). Тесты: `TryCreate_treats_the_sdk_no_op_progress_instance_as_no_progress`
берёт реальный singleton через рефлексию, поэтому переименование типа в SDK
уронит тест, а не тихо включит таймер. Поведение build/exit не менялось.

Шире finding:
NEW-D-01.

---

ID: S1-02
Verdict: ACCEPT

Критика верна: `ICliProgressReporter` объявляет non-throwing, но адаптер
пробрасывал исключение, а безопасность держалась на двух `catch` у call site'ов
(`ReportStepStarted`, `ReportHeartbeatAsync`).

Требования:
S1 / «Результат»: `ICliProgressReporter (никогда не бросает)`; README каталога /
«Фиксированные решения»: обрыв клиента не меняет исход сборки.

Что менять:
`Tools/McpToolProgressReporter.Report` оборачивает инкремент и
`_progress.Report(...)` в try/catch — контракт выполняется на адаптере, а не на
каждом новом вызывателе. Локальные `catch` в probe/runner оставлены как
defense-in-depth (в т.ч. для будущего S4 и для fake-reporter'ов).

Последствия:
Надёжность S4: pre-test build можно подключать, не завися от локального
`try/catch`. Тесты: `Report_swallows_a_failing_progress_channel` (канал бросает
`InvalidOperationException`). Публичный контракт не менялся.

Шире finding:
нет.

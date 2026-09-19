ID: S1-01
Severity: Medium
Category: Lifecycle

Target:
s1-cli-runner-progress-seam.md / «Работа» / «Если token нет — поведение
байт-в-байт»; «Результат» / «без watch вызов идёт в прежний RunCoreAsync
без таймера»

Claim:
Нет progress token → нет `CliProgressWatch` → `RunCoreAsync` без
heartbeat-задачи. MCP-вызов без token совпадает с pre-S1 по побочным
эффектам runner (не только по stdout сборки).

Evidence:
SDK на метод тула **всегда** биндит `IProgress<ProgressNotificationValue>`:
либо `TokenProgress`, либо singleton `NullProgress` (`internal`,
`Report` пустой). `McpToolProgressReporter.TryCreate` возвращает `null`
только при `progress is null`. `BuildTools.RunDotNetBuild` всегда делает
`TryCreate(progress)` и передаёт reporter в probe. Probe при ненулевом
reporter **всегда** создаёт `CliProgressWatch` и runner **всегда** зовёт
`StartHeartbeat`. Ветка «без таймера» на живом `run_dotnet_build` не
достижима; её покрывает только прямой вызов runner в тестах.

Failure scenario:
1. Хост без progress token (README: NullProgress no-op).
2. Каждый шаг probe всё равно заводит linked CTS + `Task.Delay(5s)` loop.
3. Приёмка «байт-в-байт без таймера» зелёная по unit-тесту без watch, хотя
   production MCP-путь таймер всегда включает. Исход сборки тот же, но
   S1-результат про отсутствие таймера для no-token — ложь.

Suggested change:
Считать no-op SDK (`NullProgress` по имени типа / известному singleton)
как `watch = null`, либо снять MUST «без таймера» и оставить только «нет
уведомлений + тот же exit». Нельзя одновременно обещать оба.

Confidence:
High

---

ID: S1-02
Severity: Medium
Category: Contract

Target:
s1-cli-runner-progress-seam.md / «Результат» / `ICliProgressReporter
(никогда не бросает)`; `Services/CliProgress.cs` / контракт интерфейса

Claim:
Реализации reporter не бросают: обрыв клиента / хост без token не меняет
исход CLI. Контракт на типе, не на каждом call site.

Evidence:
`ICliProgressReporter` требует non-throwing. `McpToolProgressReporter.Report`
не глотает исключения: инкремент + `_progress.Report`. `TokenProgress.Report`
синхронно стартует `McpSession.NotifyProgressAsync` (discarded `Task`).
Probe (`ReportStepStarted`) и runner (`ReportHeartbeatAsync`) ловят
исключения сами. Адаптер контракт интерфейса не выполняет; безопасность
сейчас случайна из-за двух `catch`. `TestTools` / S4 пока reporter не
подключают.

Failure scenario:
1. S4 копирует шов: передаёт `McpToolProgressReporter` в runner и считает
   интерфейс достаточным.
2. Новый call site (pre-test build) без локального `try/catch` вокруг
   `ReportStepStarted`.
3. `NotifyProgressAsync` бросает синхронно (транспорт disconnected) →
   `run_dotnet_test` падает, хотя CLI уже убит/завершён. S1 «progress не
   меняет исход» ломается на первом новом вызывателе.

Suggested change:
Глотать в `McpToolProgressReporter.Report` (контракт интерфейса), а не
надеяться на каждый `RunStepAsync`. Пока адаптер бросает, MUST «никогда не
бросает» относится только к комментарию, не к коду.

Confidence:
High

# S4 — тот же шов для test/run (опционально)

Статус: **не выполнено**. Зависимость: [S3](s3-docs-and-claims.md).
Результат шага: `run_dotnet_test` / `run_specific_test` / `run_dotnet_run`
репортят progress тем же runner-швом.

## Основание

S5-сессия показала, что class-level lifecycle test занимает минуты, не
десятки. Боль на больших app-репах — `dotnet test` 10+ мин и
`run_dotnet_run`. Делать только после shipped S1–S3, иначе раздувается
первая поставка.

Не смешивать с VSTest parser и pre-test build budget: progress не
удлиняет `timeoutSeconds` и не отменяет split build-then-test.

## Работа

Подключить уже существующий reporter к pre-test build и test process.
Heartbeat реже, чем у build probe (меньше шагов). Не слать каждую
строку VSTest.

## Приёмка

- Parser и `Status: partial` не регрессируют.
- Pre-test timeout по-прежнему общий бюджет `timeoutSeconds`.
- Хост без token — как сейчас.
- Отдельный ряд README, без обещания лечения п.1.

## Результат

Не выполнено.

# Спайк на `bitwarden/server`, 2026-11-15

Шаг 4 [rapid TIA 2](../README.md). Клон `SOURCE_ROOT/bitwarden-server`,
SHA `341acc9d3d8988cec8fe05debfa6d557cf5ab749` (`main`, `v2026.8.1-440`).
Спайк — сборка `5255518`. Сводки без имён:
[measure](il-bitwarden-server-2026-11-15-summary.json),
[coverage](il-bitwarden-server-2026-11-15-coverage-summary.json).
`raw.json` и `coverage-raw.json` остаются в `LOCAL_ROOT/bitwarden-server-2026-11-15`.

## Сборка

Первый `dotnet build bitwarden-server.slnx -c Debug` остановился на восстановлении:
`AppHost` считает критическую уязвимость `Aspire.Dashboard.Sdk.win-x64` 13.3.4 ошибкой (NU1904).
Повтор с `-p:NuGetAudit=false` собрал solution, кроме `util/RustSdk`: `cargo build --release`
завершился кодом 9009, `cargo` на машине нет. Других ошибок сборки нет, предупреждений 488.

Из-за этого на диске нет девяти тестовых сборок, и они не запускались:
`Api.IntegrationTest`, `Billing.IntegrationTest`, `Events.IntegrationTest`,
`Identity.IntegrationTest`, `Notifications.Test`, `Pam.IntegrationTest`,
`Scim.IntegrationTest`, `SeederApi.IntegrationTest`, `Sso.IntegrationTest`.
`SeederApi.IntegrationTest` ссылается на `RustSdk`.

## Тесты

Покрытие — `dotnet-coverage` 18.12.0, файл Cobertura 327 МБ, прогон около 43 с, код выхода 1.
Консоль этого прогона обрезана по размеру. Счётчики ниже — повтор того же `dotnet test --no-build`
с полным журналом; виды падений совпали с первым прогоном.

Запущены 32 сборки: 14631 прошёл, 19 упали, 680 пропущены.

Упали:

- `MailDelivery.Test` — 3 теста SMTP;
- `SharedWeb.Test` — 16 тестов: Testcontainers не достучался до Docker (именованный канал),
  контейнер Azurite не поднялся.

Пропущены:

- `Infrastructure.IntegrationTest` — все 657 тестов с меткой MySQL;
- `Core.Test` — 21 тест (Azure, Stripe, почтовый catcher, relay);
- `Icons.Test` — `GetIconAsync_Success`;
- `MailDelivery.Test` — `SendEmailAsync_Works_NoSsl`.

## Граф и покрытие

`measure`: сумма этапов 1,7 с, пик 169 МБ. 1149 тестовых классов, 11234 тестовых метода,
4437 файлов с уровнем 0. Тип с `Assembly.Load*` один, и ни один тестовый класс до него не доходит.
Слои `referencesWithLoads` и `discoveryWithLoads` поэтому совпадают с `references` и `discovery`.

| Слой | Медиана доли уровня 0 | Файлов без ответа | Пропуск среди 1815 исполняемых |
| --- | --- | --- | --- |
| `references` | 0,5% | 1302 | 2,6% (48 файлов) |
| `discovery` | 1,2% | 1109 | 0% |
| `wholeAssembly` | 16,8% | 1056 | 0% |
| `referencesWithLoads` | 0,5% | 1302 | 2,6% (48 файлов) |
| `discoveryWithLoads` | 1,2% | 1109 | 0% |

Слой `discovery` не пропускает ни одного файла, который этот прогон исполнил.
48 пропусков остаются в `references`. 26 файлов лежат в сборках без тестовой ссылки,
и тесты их не исполняют. Интеграционные сборки, которых не было на диске, в покрытие не вошли:
подтверждённый нуль их не описывает.

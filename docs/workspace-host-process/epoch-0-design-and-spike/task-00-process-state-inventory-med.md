# C/E0/task-00 — Инвентаризация состояния процесса

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — пропущенный элемент станет утечкой состояния между клиентами общего host.
- Необходимые способности: `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **completed, 2026-10-26, docs-only**; принят scope инвентаризации
  состояния и этапов 54 built-in tools после link/diff checks и самопроверки.
  Design, модель процессов и production activation не приняты; код не менялся.
- Актуализация: **completed, 2026-10-28**; после переноса main `4e5e9f9`
  проверено влияние production-базы 1.5.17, инвентарь дополнен до S01–S42;
  [поправки и validation](evidence/task-00-impact-2026-10-28.md).
- Depends on: —.
- Модель, реализовавшая задачу: Codex; точный ID модели недоступен.
- Модели, проводившие ревью: независимое ревью не проводилось; самопроверка исполнителя.
- Количество раундов ревью: 0 независимых.
- Количество исправлений после ревью: 0 по независимому ревью.
- Отчёт о реализации, ревью и validation: [исходная инвентаризация](evidence/task-00-state-inventory.md),
  [актуализация и проверки 2026-10-28](evidence/task-00-impact-2026-10-28.md).

## Цель

Перечислить всё состояние, которое сейчас неявно принадлежит процессу, и для
каждого элемента определить: общее для совместимых клиентов host, состояние
клиентской MCP-сессии, часть идентичности host или конфликт с новой моделью.
Учитывать смену привязки и потерю RAM при падении, а для модели ролей — также
совмещение клиентского фасада и host в первом MCP, promotion/demotion и плановое
освобождение workspace.

## Входы и границы

[Тема](../README.md), [карта эпохи](README.md), [ARCHITECTURE](../../ARCHITECTURE.md).
Поиск — MCP-инструментами по загруженному workspace (`find_symbol_references`,
`find_usages`, `search_code`), не shell grep. Код не меняется.

Обязательные категории:

- static mutable поля и singletons из `RoslynMcpServiceCollectionExtensions`;
- `Environment.CurrentDirectory` и чтение environment, включая `ROSLYN_MCP_WORKSPACE`,
  `ROSLYN_MCP_TOOL_GROUPS` и `MCP_LOG_*`;
- `Console`, stdin и stdout вне транспорта, stderr-диагностика старта;
- регистрация MSBuild (`MsBuildBootstrapper`) и BuildHost;
- `McpRuntimeToolCollection`, `McpToolActivationService`, профили, плагины;
- логирование: `HarnessSessionId`, Serilog sink, `logs/mcp-*.log`;
- `RoslynMcp.jsonc` и место его чтения;
- параметры load (включая пропущенные), sticky analyzer policy, CLI build args
  и build proofs: что нельзя наследовать от другой сессии и что делает host несовместимым;
- `stop_mcp_server`, `reset_workspace`, `manage_agent_scratchpad`;
- таймеры, фоновые задачи, watchers, временные каталоги и shadow-copy;
- старт процесса в `Program.cs` до построения host: `MsBuildBootstrapper.Register`,
  принудительная загрузка сборок Roslyn, `PluginStartup.Run`. Что из этого нужно
  чистому proxy (например, список инструментов plugins для `tools/list`), а что
  допустимо только в роли host;
- инструменты по месту исполнения: нужен workspace / запускает процессы (build,
  test, `execute_dotnet_command`, NuGet) / lifecycle. Зависимость от cwd, сессии
  и build proofs (`SessionBuildState`). Для смешанных инструментов — отдельно
  разрешение проекта/defaults, admission и build proofs, запуск CLI и обработка
  результата; DLL-маршрут тестов сейчас обращается к Roslyn Solution.

Для MCP-состояния отдельно проверить negotiated protocol, идентификаторы
запросов, progress/cancellation, подписки и `enable_tool_group` (`lite` меняется
в runtime, `full` — no-op). Для plugins — требования к DI и состоянию tool host.
Для analyzer lifecycle — restart-required и различие перезапуска прокси и host.
Определить, что остаётся после demotion бывшего host (workspace/watchers,
загруженные CLR assemblies, plugins): освобождение workspace не считать
гарантией полной выгрузки памяти или возможности новой analyzer identity.

## Конкретный результат

`evidence/task-00-state-inventory.md`: элемент → место в коде → текущая
семантика → классификация (общий / сессия / идентичность host / конфликт) →
поведение при смене host и его падении → последствие для U-C-01…U-C-08.
Отдельная таблица инструментов: инструмент/этап → нужен ли workspace → зависимость
от cwd/сессии/build proofs → допустимое место исполнения (host, proxy, оба) →
потеря контекста при смене привязки/поколения или падении host во время CLI.
Отдельный список «неизвестно» с причиной. Механизм маршрутизации и точный
ключ выбирает task-01, инвентаризация их не принимает.

## Проверки

- Каждый элемент имеет ссылку на конкретный символ или файл.
- Классификация «общий» объясняет допустимые общие эффекты для совместимых
  клиентов; reset/stop и изменения на диске не объявляются изолированными.
- Отмечено состояние, которое теряется при падении host, и которое фасад
  должен сохранить при переключении без нового MCP initialize от агента.
- Нет предложений по реализации сверх классификации: дизайн — task-01.

Docs-only: link и diff checks.

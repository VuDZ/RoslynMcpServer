# Реализация статистики использования и каталога пользователя

Дата: **2026-10-30**, Europe/Moscow. Ветка: `usage-statistics`. Текущая версия: **1.5.22**.
Статус: **код реализован и автоматизированно проверен; Codex/Windows и Claude Code/Linux проверены контролируемыми прогонами; D-01/D-02 и мелкие замечания из [Claude Code smoke](claude-code-smoke.md) исправлены в 1.5.22, автоматизированные проверки пройдены; повторный native Linux smoke ещё нужен; Cursor, dsh, OpenCode и длительный сбор впереди**.

Согласованные решения: коммит `7b5dfea`. Требования:
[usage-statistics](README.md), [user-data-directory](../user-data-directory/README.md).
Формат: [schema.md](schema.md). Машиночитаемые результаты: [исходная 1.5.19](validation.json) и [исправления 1.5.20](implementation-review/validation.json), [Codex/Windows 1.5.21](codex-smoke-validation.json), [Claude Code/Linux 1.5.21](claude-code-smoke-validation.json) и [исправления 1.5.22](followup-validation.json).

## Принятый scope реализации

- Постоянные конфигурационные/state-каталоги Windows, Linux и macOS; `ROSLYN_MCP_HOME`
  и отдельный `ROSLYN_MCP_LOG_DIR`. Пути вычисляются без создания каталогов.
- Конфиг exe → user → cwd; drop-in plugins exe → user → configured → environment,
  первый прошедший проверку ID. Прежние overloads сохранены.
- Единый путь логгера, log tools и информации сервера; publish helper показывает
  действительный путь. Старый скрипт измерения load явно задаёт свой каталог логов.
- Локальный JSONL v1: activity, load/cache/lazy/reset, память процесса и BuildHost.
  Producers не делают file/process I/O; bounded queue, межпроцессный writer lease,
  ротация 32 MiB, retention 60 дней и ограничение shutdown 1 с.
- `tool_start`/call ID сохраняют незавершённые вызовы. SDK identity поддерживается
  для legacy initialize и discovery/request metadata ревизии 2026-07-28.
- Канонические фактически удерживаемые ключи; missing/failed/cancelled состояния
  учитываются явно. Неопознанный BuildHost даёт unknown, а не ноль.
- PowerShell-анализатор считает M1–M6/M3r для всех T, учитывает baseline fresh opens,
  gaps и unknown fields. Экспорт содержит K/P-метки вместо raw workspace/process IDs.
  Численные пороги и автоматический выбор стратегии отсутствуют.
- Исходный трёхпараметровый CLR-конструктор `SolutionManager` сохранён;
  дополнительная зависимость collector вводится отдельным overload.

## Делегирование и review

Три исполнителя реализовали каталоги, collector и анализатор в разделённых файлах.
Четвёртая задача проверила настоящий процесс и writer через stdio.
Независимые проверки каталогов, collector и анализатора завершились **ACCEPT** после
исправления находок. Координатор интегрировал Program, документацию, версию,
publish helper, проверил diff и выполнил итоговые suites.

Существенные закрытые случаи: SDK discovery без initialize; unknown BuildHost;
неполные state transitions; unknown lazy/restore semantics; baseline fresh opening
не начисляет дополнительную перезагрузку прежнего ключа; M3r пересчитывает
surviving largest copy после выгрузки.

## Исходная validation 1.5.19

- **Windows SDK 10.0.402:** Release solution build — PASS.
- **Windows main:** `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`,
  **1485/1485 PASS**, без skips.
- **Windows lifecycle:** opt-in `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`,
  `Category=AnalyzerLifecycle`, **13/13 PASS**, без skips.
- **Ubuntu 24.04 WSL, SDK 10.0.113:** isolated restore и Release build — PASS.
- **Linux main:** `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`,
  **1481/1481 PASS**, без skips.
- **Linux lifecycle:** тот же opt-in, **13/13 PASS**, без skips.
- SourceStructure включён в итоговый main на обеих ОС; известный inventory limits
  не ослаблялся. Generated `artifacts/` исключён из Compile/package и соответствующего
  production source scope, чтобы integration fixture не становился кодом сервера.
- **PowerShell 7:** `Test-McpUsage.ps1` — **40 сценариев / 117 assertions PASS**.
- **Publish paths:** **9 checks PASS** как в PowerShell 7, так и Windows PowerShell 5.1.
- **Изолированный self-contained win-x64 publish:** PASS, версия файла 1.5.19.0.
- **Настоящий published EXE + stdio + analyzer:** **822 checks PASS**,
  41 событие, 2 параллельных процесса в одном usage-каталоге, 5 попыток load,
  11 завершённых вызовов. Покрыты lazy/cache/fresh/reset/error/reload/stop,
  opt-out, недоступные usage/HOME, чистый JSON-RPC stdout и отсутствие raw paths/keys
  в JSON/Markdown экспорте.
- Git whitespace и сопоставление SHA256 **33 изменённых C# файлов** с Linux snapshot — PASS.
  Нормализованное содержимое идентично проверенному. 32 C# raw hashes совпадают,
  Program.cs совпадает после LF normalization. Эта проверка сравнивала Windows/Linux
  snapshots, но не кодировку и EOL с `main`: добавленный BOM и смешанные EOL Program.cs
  тогда пропущены. Прежнее утверждение о сохранении исходных EOL было неточным;
  BOM и CRLF исправлены в 1.5.22, см. ниже.

Каталог по-прежнему содержит 54 full / 15 lite tools. Из-за уточнения описания
`read_log_tail` full tools/list стал 45 848 UTF-8 bytes, lite — 19 411,
lite+operations — 21 213; точные release snapshots обновлены по измерению.

## Локальные evidence

- Windows lifecycle: `artifacts/validation/windows-lifecycle/windows-lifecycle-final.trx`.
- Linux: `artifacts/usage-linux/7b4d8b719d4d4e6dbec0bb63ff0f1c1b/results-20261029T223209Z-10480/`;
  там logs, TRX и exit codes. Source manifest SHA256:
  `5e1466c2cbe5b8270f817ed68a610b6a4c4495dbeeb689dc5d09f7166619f682`.
- Published protocol probe: `artifacts/usage-integration/812496cb65504aa9aedc88e85ee1a9c9/`.
- Publication: `artifacts/usage-publish/win-x64/`.

Evidence остаётся в ignored artifacts; это собственные synthetic/integration данные,
а не корпус проектов коллег. Ненулевые и без skips TRX проверены отдельно.
Предыдущие неуспешные прогоны сохранены: исправлялись fixture layout/SDK protocol
assumption, byte snapshots и подготовка Linux Git/NuGet окружения. Успешный Linux
прогон выполнен на неизменном C# snapshot.

## Исправления после implementation-review — 1.5.20

F-001 и F-002 исправлены 2026-10-30; F-003 затем закрыт [уточнением M2](implementation-review/result.md#уточнение-f-003) без изменения кода. Полный scope, регрессии и evidence:
[результат исправлений](implementation-review/result.md#исправления-1520).
Release build и main Windows **1486/1486**, Linux **1482/1482**; lifecycle **13/13**
на обеих ОС, без skips. SourceStructure **123/123** включён в main и пройден отдельно.
PowerShell 7 — **49 сценариев / 161 assertion**. Изолированный published EXE 1.5.20.0
и protocol probe — **822 checks PASS**. Финальные 706 C# hashes совпали с Linux snapshot.
Независимая read-only проверка diff не выявила замечаний. Это проверка исправлений,
а не приёмка пакета; реальные harness и сбор не запускались.

## Живой Codex и исправление 1.5.21

2026-10-30 выполнен контролируемый прогон через настоящий Codex на Windows и двух
сабагентов. Найден и исправлен false failure на `Failed: 0`; после обновления
подключённого EXE все 18 вызовов помечены успешно. Windows main **1487/1487**,
lifecycle **13/13**, сабагенты **5/5**, **3/3**, **2/2** — PASS. SourceStructure входит
в main; 10 вариантов нового regression test пройдены. Снято 85 событий и 37 полных
снимков памяти; интервалы вызовов трёх MCP пересекались, внутри одного MCP наложения
не наблюдалось. Подробности и evidence — [smoke и статистика](codex-smoke.md).
Linux validation выше относится к 1.5.19/1.5.20; Linux 1.5.21 — в следующем разделе.

## Claude Code и Linux 1.5.21

2026-10-30 на Linux-машине владельца (native Ubuntu, не WSL) 1.5.21 опубликован
self-contained linux-x64 и подключён к Claude Code 2.1.314 через launcher владельца.
Release build и main **1483/1483 PASS** вне MCP; `Test-McpUsage.ps1` под pwsh 7.6.8 —
**49 сценариев / 161 assertion PASS**, первый прогон анализатора в Linux PowerShell.
Через MCP: build PASS, main **1483/1483 PASS** после `reset_workspace`; с загруженным
решением — 1397/1483 из-за лимита inotify. Два сабагента — **5/5**, **3/3**, **2/2**.

Сабагенты Claude Code не запускают свой MCP: все вызовы родителя и сабагентов шли
в одном процессе, до 3 одновременно. Найдены D-01 (native Claude Code записан как `cli`),
D-02 (`PluginStartupTests` пишет в настоящий каталог пользователя) и D-03
(загруженное решение занимает 110 из 128 экземпляров inotify; отдельная задача
[disk-watcher-inotify](../disk-watcher-inotify/README.md)), а также три небольших
замечания ревью кода. Подробности: [Claude Code smoke](claude-code-smoke.md).

## Исправления ревью 1.5.22

Дата: **2026-10-30**. Обработан свежий Linux-отчёт из коммита `75fba73`.
Статус: **D-01/D-02 и три мелких замечания исправлены в коде и проверены
автоматизированно; повторный native Linux smoke после обновления ещё нужен**.
Машиночитаемое подтверждение: [followup-validation.json](followup-validation.json).

- **D-01.** `HarnessNameResolver` узнаёт native image `claude/versions/<версия>`;
  произвольные соседние пути и нечисловые имена не считаются Claude. По-прежнему
  учитывается только непосредственный родитель, командная строка не-node не читается.
  Добавлены шесть вариантов регрессии, два воспроизвели ложный `cli` до исправления.
- **D-02.** Настоящий entrypoint в `PluginStartupTests` получает собственный временный
  `ROSLYN_MCP_HOME` после удаления унаследованных overrides. Stdio-тест проверяет
  фактические каталоги логов/usage через server info и текстовый лог в этом HOME.
  Другой запуск entrypoint, `ProgramHostFailureTests`, также задаёт временные HOME/logs;
  специализированные lifecycle/witness hosts не запускают collector.
- **Мелкие замечания.** В Program.cs удалён случайный UTF-8 BOM; окончания строк
  приведены к CRLF, как в `main`. C# правки выполнены через MCP; существующий BOM
  удалён отдельно, поскольку MCP сохраняет исходную кодировку. Regex классификатора
  вынесен в статическое поле без изменения шаблона и параметров. Вторичное исключение
  `StopAsync` больше не скрывает исходное исключение хоста.
- Регрессия shutdown запускает настоящий entrypoint с тестовым плагином: его
  `StartAsync` и `StopAsync` бросают разные исключения. До исправления тест падал,
  после него подтверждены вызов StopAsync и сохранение исходного исключения.

Проверки Windows, SDK 10.0.402, финальный код **1.5.22**:

- Release solution build — **PASS**, новых предупреждений нет.
- Целевые resolver/plugin/startup tests — **39/39 PASS**.
- Main `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` — **1494/1494 PASS**,
  без skips; весь SourceStructure входит в набор, inventory limits не ослаблен.
- Opt-in lifecycle `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` — **13/13 PASS**, без skips.
- Реальный framework-dependent DLL + stdio + analyzer — **822 checks PASS**:
  41 событие, 2 процесса, 5 попыток load, 11 завершённых вызовов.

Первый main при одновременно запущенном protocol probe дал **1492/1494**:
`UsageWorkspaceInstrumentationTests` не получил load-событий,
`UsageStatisticsFiltersTests` не согласовал protocol 2026-07-28. Два класса отдельно
дали **8/8**, затем полный main без параллельного probe — **1494/1494**.
Причина первого сбоя не установлена; исходный результат сохранён, а не заменён PASS.

Локальные evidence: `artifacts/usage-followup-1.5.22/` (build, red/green, целевые тесты,
первый main, повтор и `lifecycle/followup-1522-final-lifecycle.trx`);
protocol probe — `artifacts/usage-integration/e9f0b1283d54462db506f013c0507318/`.
Работающий MCP Codex остаётся 1.5.21; установки и подключения этим исправлением
не переключались. D-03 не менялся: это отдельная задача inotify.

## Границы приёмки и следующий этап

Первоначальная validation 1.5.19 была оспорена точечно. F-001 и F-002 исправлены
и проверены в 1.5.20 — [результат](implementation-review/result.md#исправления-1520).
F-003 закрыт уточнением спецификации после обсуждения измерений и модели M2; код и формат JSONL сохранены. Исправления не объявляют приёмку всего пакета.
Настоящий Codex на Windows проверен в 1.5.21 ([smoke и статистика](codex-smoke.md)).
Claude Code на Linux проверен в 1.5.21 ([smoke](claude-code-smoke.md)); его D-01 и D-02
исправлены в 1.5.22; до сбора нужно повторить native Linux smoke и проверку имени harness.
Cursor и dsh ещё требуют проверки исполнителем; OpenCode — владельцем, как в
спецификации. Наличие CLI в PATH не считается проверкой harness.
Установка MCP Claude Code владельца на Linux переключена на сборку этой ветки
(`ROSLYN_MCP_REPO`); другие установки не заменялись.

Linux C# проверен в WSL (1.5.19/1.5.20) и на native Linux (1.5.21); Linux PowerShell
проверен в 1.5.21. macOS runtime в этих прогонах не проверялся.
В build остаются предупреждения существующего кода и рекомендации xUnit-анализатора;
warning-free build не заявляется.

До длительного сбора нужно подтвердить исправления D-01/D-02 в native Linux smoke и завершить проверки остальных harness. После этого — локальный сбор 2 недели–месяц,
промежуточный прогон через 2–3 дня и выбор стратегии владельцем по результатам.
Реальные данные и нагрузочный профиль анализатора оцениваются в этом прогоне;
экономия памяти и latency будущей выгрузки не объявлены измеренным эффектом.

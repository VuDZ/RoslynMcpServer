# E0/task-01 — независимое ревью выполнения, round 2

Дата: 2026-10-08 (Europe/Moscow). Итог: **accept**.
Task-01 остаётся **planned**. Этот файл статус задачи не меняет.
Рецензент: Grok 4.7. Сессия рецензента раннер и evidence прогона не писала.
Исполнитель: Grok 4.7 (agent `02b91e41-fb75-4b87-9e94-cd59ff85ac41`).
Раунд: 2. T01-R1-001: **closed**. Новых findings: нет.

## Границы

Проверены task-01, benchmark (revision compare context), ограниченная поправка
T01-R1-001, `ordinary_baseline.py` (`compare_material`), `compare-key.json`,
`evaluated-instance.json`, `summary.json`, `task-01-baseline.md` и
`attempt-01.json`. Production C# эта задача не меняла; набор C# не требовался.

## T01-R1-001 — closed

Ограниченная поправка требовала три уровня в объекте ключа, health-строку
только как наблюдение сервера, excluded emit/signing отдельным полем,
переписанные `compare-key.json` и таблицу отчёта, без повторного снятия пяти
таймингов, и evaluated values отдельным наблюдением того же контроля.

`compare_material` (`ordinary_baseline.py`, 1090–1154) кладёт в хеш
`rawRequested`, `loaderGlobalsBeforeOpen` и `evaluatedInstance` отдельно.
Строка health уходит в `serverHealthObservation` с role, что это текст ответа,
а не эти три уровня. `excludedEmitSigning` — своё поле (`excluded`).

`compare-key.json` и `summary.json` `compareKey` совпадают.
`compareKeyId` `85163a2486ed8a50a0a0946a344adf27bebcebca386eb4ee372ae756a270884d`
равен sha256 канонического JSON этого объекта. Таблица в
`task-01-baseline.md` повторяет три уровня, health как наблюдение и excluded
emit/signing.

Медианы не пересняты: load 1413.5, semantic 957.1, useful 2370.6.
Отдельный `dotnet msbuild -getProperty` того же `RoslynMcpPlugin.csproj` без
переданных Configuration/Platform/TargetFramework вернул Configuration `Debug`,
Platform `AnyCPU`, TargetFramework `net10.0`, TargetFrameworks пустую строку,
код 0. Это значения `evaluatedInstance` в ключе и в `evaluated-instance.json`.

Старый `compareKeyId` `732e1edef5bc0f2b393f0ce5740ec527d093afc115cc8b3779607f1f77d3c6c4`
в `attempt-*.json` — не промах спецификации. Поправка велела переписать
`compare-key.json` и таблицу отчёта и не переснимать попытки. As-run ключ в
`attempt-01.json` по-прежнему держит health-строку в
`effectiveConfiguration` / `effectivePlatform` / `effectiveTargetFramework`.
Отчёт и `summary.json` `compareKeyNote` это называют. Все шесть attempt-файлов
делят один as-run id; измеренная группа одна.

## Новые findings

Нет.

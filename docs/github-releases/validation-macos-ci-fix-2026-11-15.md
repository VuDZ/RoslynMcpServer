# Разбор первого macOS CI, 2026-11-15

Статус: **test-fix-accepted; native-ci-complete; release-smoke-pending**.
Объём: тестовая инфраструктура macOS — кеш Roslyn, временные пути, файловая блокировка
и процессная фикстура; код сервера и версия 1.5.25 не меняются.

## Причина

В [CI для 50d0bf9](https://github.com/VuDZ/RoslynMcpServer/actions/runs/43739449780)
Windows и Ubuntu прошли обе ветки. На macOS основной набор упал целиком:
1517/1517 ошибок; lifecycle обнаружил 0 тестов, что корректно заблокировала
проверка `Assert-NoSkippedTests.ps1`.

Общая ошибка — `RoslynIndexCache` в module initializer. Хелпер временно подменял
`HOME`, рассчитывая направить `LocalApplicationData` во временный каталог процесса.
macOS получает этот путь через Foundation, поэтому Roslyn сохранил общий
`/Users/runner/Library/Application Support/Microsoft/VisualStudio/Roslyn/Cache/dotnet-…`.
Проверка изоляции остановила загрузку всей тестовой сборки, не позволив удалить общий кеш.

Основания: [реализация GetFolderPath в .NET](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Environment.GetFolderPathCore.Unix.cs),
[каталог пользователя CoreFoundation](https://github.com/apple-oss-distributions/CF/blob/main/CFPlatform.c).
На Unix `Environment.SetEnvironmentVariable` меняет управляемый словарь,
а не native `getenv` ([исходник .NET](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Environment.Variables.Unix.cs)).

## Исправление

- macOS: native `CFFIXED_USER_HOME` временно указывает на каталог данного процесса
  только во время статического конструктора Roslyn; исходное native значение
  восстанавливается в `finally`, включая случай отсутствующей переменной.
- Linux продолжает использовать управляемый `XDG_DATA_HOME`; ненужная подмена `HOME` убрана.
- Проверка принадлежности кеша тестовому каталогу сохранена; общий кеш не очищается.
- Регрессионный тест проверяет приватный каталог кеша и восстановление пользовательского
  `LocalApplicationData`; прежний тест проверяет запись и повторное чтение реальной SQLite-базы.
- Фильтры CI, запрет пустого прогона и запрет пропусков не ослаблялись.

## Ошибки, открывшиеся после восстановления загрузки сборки

[Повторный CI для 5c9e12a](https://github.com/VuDZ/RoslynMcpServer/actions/runs/43742084620)
подтвердил исправление кеша: macOS main выполнил 1517 тестов, 1511 passed / 6 failed;
lifecycle — 13 тестов, 5 passed / 8 failed. Windows и Ubuntu прошли обе ветки.

- macOS `TMPDIR` содержит `/var/folders/…`, а native cwd и часть MSBuild-путей —
  `/private/var/folders/…`. Из-за этого четыре сравнения путей падали, а lifecycle
  терял соответствие analyzer project/output и иногда дважды восстанавливал один
  проект под разными именами с конфликтом `*.nuget.g.props` / `project.nuget.cache`.
  До создания фикстур тестовый процесс теперь переводит `TMPDIR` в physical path
  через `realpath`; отдельный тест сравнивает его с native `/bin/pwd -P`.
- Тест занятого lease ожидал Linux `EAGAIN=11`; на macOS это `35`.
  Исправлено только распознавание ожидаемой ошибки в тестах.
- macOS shell мог заменить себя последним `sleep`, поэтому тест отмены не находил
  помеченного родителя и ребёнка. Фикстура теперь запускает `sleep` в фоне и ждёт его
  через `wait`, сохраняя дерево процессов для проверки реальной отмены.

Правила идентичности путей production, запреты публикации real analyzer output,
проверки fail-closed и assertions lifecycle не изменялись.

## Валидация

- Focused index storage, JSONL writer и Git helper, Windows Release: **15/15 passed**.
- Release solution build: passed.
- Полный основной набор, Windows Release: **1525/1525 passed**, включая весь SourceStructure.
- `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: **13/13 passed**, 0 skipped.
- [Итоговый CI для `0f5a413`](https://github.com/VuDZ/RoslynMcpServer/actions/runs/43743170150): **success**, все шесть jobs.
- macOS ARM64: **1518/1518 main**, **13/13 lifecycle**.
- Ubuntu: **1518/1518 main**, **13/13 lifecycle**.
- Windows: **1525/1525 main**, **13/13 lifecycle**.
- SourceStructure в основном наборе каждой платформы: **123/123 passed**.
- Все TRX проверены `Assert-NoSkippedTests.ps1`: **4612 executed / 4612 passed**,
  0 failed, 0 skipped; суммарный счётчик включает дочерние тестовые процессы.
- На macOS прошли native PPID/argv, фактическая очистка BuildHost с сохранением
  другого дочернего процесса, обе новые проверки кеша/physical temp path,
  тест удержанного lease и отмена дерева процессов.

2026-11-15: приняты исправления тестовой инфраструктуры и нативный CI для исходников
коммита `0f5a413c963d7cb317b0aad16753b0bbee3306c1`; фильтры и защитные проверки сохранены.
Версия сервера не повышалась, поскольку production не менялся.

Исходные TRX и логи сохранены локально в `artifacts/macos-ci-fix-20261115/`,
итоговые артефакты — в подкаталоге `final-ci/`.
Нативный smoke релизных архивов в этот CI не входит и остаётся отдельной проверкой.

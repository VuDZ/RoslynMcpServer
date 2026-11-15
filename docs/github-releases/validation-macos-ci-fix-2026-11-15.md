# Разбор первого macOS CI, 2026-11-15

Статус: **local-validation-complete; native-ci-pending**.
Объём: только тестовая изоляция кеша Roslyn; код сервера и версия 1.5.25 не меняются.

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

## Валидация

- Focused `RoslynIndexStorageTests`, Windows Release: 3/3 passed.
- Release solution build: passed.
- Полный основной набор, Windows Release: **1524/1524 passed**, включая весь SourceStructure.
- `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: **13/13 passed**, 0 skipped.
- Повторный native macOS CI: ожидается после отправки исправления.

Исходные TRX и логи сохранены локально в `artifacts/macos-ci-fix-20261115/`.
Нативный smoke релизных архивов в этот CI не входит и остаётся отдельной проверкой.

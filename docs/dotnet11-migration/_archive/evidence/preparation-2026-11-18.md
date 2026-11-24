# Проверка подготовки плана, 2026-11-18

Статус: **completed**. Scope результата: ветка от актуальной основной ветки,
[план](../README.md), четыре planned-этапа и запись в родительском индексе.
Реализация миграции, независимое ревью и её приёмка не выполнялись.

## Основание

- `git fetch origin` выполнен перед созданием ветки.
- Основная ветка — `origin/main`; baseline SHA:
  `6f84e897e059cd87b9cc8946212154291e0e2ed7`, продукт 1.5.25.
- Ветка: `dotnet11-migration-plan`; по просьбе пользователя префикса codex нет.
- Создан отдельный управляемый worktree. Исходный checkout оставлен на main,
  его незакоммиченные изменения не переносились и не исправлялись.
- Факты плана сверены с csproj, fork README/UPSTREAM/build settings,
  BuildHost scripts, SDK/environment helpers, тестовыми inventory, CI и release workflow.
  Предыдущая беседа опиралась на dirty checkout 1.5.18; baseline плана — чистый 1.5.25.

## Выполненные проверки документации

- Проверена база ветки и отсутствие посторонних изменений в новом worktree.
- Проверены относительные Markdown file links во всех новых документах
  и в добавленной строке docs/README.md; ссылки на общие разделы сопоставлены с headings.
- Проверены `git diff --check`, состав diff и planned/completed статусы:
  завершена только подготовка документов, все четыре code-этапа planned.
- Команды validation сверены с test-suite.yml: Release, main filters Windows/Linux/macOS,
  opt-in lifecycle, отдельный SourceStructure, отсутствие skip/нулевых наборов.
- Новых C#, project/build settings, package versions и release assets нет.
  Build/main/lifecycle, эксперименты с новым host, performance и native smoke
  не запускались: они являются работой этапов, а не проверкой Markdown.
- Независимых рецензентов и раундов ревью не было; self-check документации
  не записывается как независимое ревью.

Результат self-check: **PASS** — шесть новых Markdown-файлов, четыре planned-задачи,
одна запись родительского индекса, 29 локальных ссылок и четыре якоря.
Markdown fences парные, trailing whitespace отсутствует; staged diff содержит только
семь Markdown-файлов, `git diff --cached --check` завершился с exit code 0.
База ветки подтверждена через `git merge-base HEAD origin/main`.

## Что остаётся проверить при исполнении

Сведения NuGet 5.12.0 о TFM/dependencies/source revision взяты из официальной
[страницы пакета](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0);
скачанный nuspec, package hashes и source/RPC diff здесь не проверялись.
Наличие и результат upstream early-cancel fix не подтверждены.
Совместимость generators/plugins, SQLite identity, SDK 10/11 и runtime BuildHost,
baseline B0/B1/B2, все tests и native gates остаются planned по файлам этапов.

Результат подготовки не объявляет новый runtime/язык поддержанными
и не закрывает старую task-00b или pending release smoke.

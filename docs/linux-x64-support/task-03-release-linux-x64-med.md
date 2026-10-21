# linux-x64/task-03 — Релизная матрица и архивы linux-x64

- Дата постановки: 2026-10-21.
- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — релиз нельзя прогнать локально целиком; ошибка
  в архиве (бит исполнения, состав файлов) видна только пользователю.
- Необходимые способности: `github_actions`, `powershell`, `dotnet_publish`.
- Статус: **in progress, 2026-10-22** — [Release 39680548658](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39680548658) опубликовал [v1.5.6](https://github.com/VuDZ/RoslynMcpServer/releases/tag/v1.5.6): четыре архива, smoke обеих платформ и attestation прошли. Ручная проверка self-contained без runtime не сделана.
- Depends on: [task-02](task-02-ci-linux-matrix-med.md).
- Модель, реализовавшая задачу: Grok 4.7 (workflow, без выпуска).
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

`release.yml` публикует для каждой версии четыре архива одного SHA:
`win-x64` и `linux-x64`, каждый self-contained и framework-dependent.
Релиз появляется только когда готовы все четыре.

## Требования

1. **Матрица платформ.** `win-x64` собирается на `windows-latest`,
   `linux-x64` — на `ubuntu-latest` (ReadyToRun и проверки на той же ОС).
   Добавление `linux-arm64` позже должно сводиться к новой строке матрицы.
2. **Формат архива.** Windows — `.zip`, как сейчас. Linux — `.tar.gz` с тем же
   корнем `RoslynMCP/`, README, LICENSE и без `logs/`. У `RoslynMCP/RoslynMcpServer`
   в архиве сохранён бит исполнения; это проверяется распаковкой в job.
   Имена: `RoslynMcpServer-{version}-linux-x64-self-contained.tar.gz` и
   `…-framework-dependent.tar.gz`.
3. **Проверки публикации по платформе:**
   - версия читается из `RoslynMcpServer.dll`, а не из `.exe`;
   - маркер встроенного runtime: `coreclr.dll` на Windows, `libcoreclr.so`
     на Linux; в framework-dependent его нет.
4. **Метаданные и notes.** `Get-ReleaseMetadata.ps1` отдаёт имена архивов
   для всех платформ матрицы, а не две строки с `win-x64`. Release notes
   перечисляют архивы по платформам и требования framework-dependent для
   каждой (runtime .NET 10 соответствующей ОС и архитектуры).
5. **Публикация одним шагом.** Сборочные job выкладывают архивы как
   artifacts. Отдельный job после всех сборочных: attestation, затем
   создание или публикация draft-релиза со всеми архивами. Падение любой
   платформы — релиза нет.
6. **SDK релиза** закреплён так же, как в task-02
   ([AR-13](../architecture-review/findings.md#ar-13)).
7. **Smoke опубликованного бинаря** на каждой платформе и для обоих вариантов:
   распакованный архив запускается, отвечает на MCP `initialize` и `tools/list`
   по stdio и выполняет `load_workspace` на маленьком проекте из репозитория
   через BuildHost. Это закрывает требование
   [O-03](../github-releases/README.md#o-03-совместимость-и-расширение-платформ)
   для `linux-x64` и одновременно добавляет недостающий smoke для `win-x64`.
8. **Документы.** В [github-releases/README.md](../github-releases/README.md)
   R-02 и O-03 приводятся к решению P-03; раздел «Текущее состояние»
   обновляется.

## Вне задачи

- `linux-arm64`, `linux-musl-x64`, macOS.
- Подпись бинарей и пакеты дистрибутивов (deb/rpm).
- Перевод `install2opencode.ps1` на shell: он работает в `pwsh` на Linux,
  это описывает task-04.

## Приёмка

- Ссылка на успешный прогон Release workflow и на GitHub Release с четырьмя
  архивами и attestation для каждого.
- Лог smoke для четырёх вариантов.
- Ручная проверка: `linux-x64` self-contained распакован на чистой машине
  или в контейнере без установленного runtime .NET 10, но с SDK для
  проекта; сервер подключён к MCP-клиенту и загружает проект.

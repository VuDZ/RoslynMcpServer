# Поддержка linux-x64

Дата: 2026-10-22. Статус: **accepted** — четыре задачи выполнены. Runtime — [ARCHITECTURE](../../ARCHITECTURE.md) и корневой README. Релиз [v1.5.6](https://github.com/VuDZ/RoslynMcpServer/releases/tag/v1.5.6). Self-contained `linux-x64` распакован в WSL: процесс поднял свой `libcoreclr.so` без `dotnet` в `PATH`; `load_workspace` SmokeApp прошёл с `DOTNET_ROOT=/usr/lib/dotnet`.

Основание: [AR-12](../../architecture-review/findings.md#ar-12), решение
[P-03](../../architecture-review/proposals.md#p-03) от 2026-10-21.

## Решение

- Поддерживаемые платформы: `win-x64` и `linux-x64`.
- Обе платформы проходят CI: шарды `unit` и `lifecycle`. Красный job любой
  платформы блокирует релиз.
- Релиз выкладывает архивы обеих платформ, в двух вариантах поставки
  ([R-03](../../github-releases/README.md)).
- Остальные RID из `RoslynMcpServer.csproj` (`win-arm64`, `osx-arm64`,
  `linux-arm64`, `linux-musl-x64`) собираются, но не поддерживаются. Список
  `RuntimeIdentifiers` не сокращается: arm запланирован позже.

## Исходное состояние (2026-10-21)

Сервер на Linux собирается и работает, но ничем не проверяется.
Замеры на Linux (Ubuntu, 2 ядра) — [evidence/baseline-2026-10-21.md](evidence/baseline-2026-10-21.md):

- `AnalyzerLifecycle`: 13/13 passed, 0 skipped. Отдельная архитектура
  shadow-copy для Linux не нужна (обоснование — в evidence).
- Main suite (`Category!=AnalyzerLifecycle`) с SDK 10.0.300, как в CI:
  1313 passed, 23 failed, 2 skipped (WPF). Причины: два production-бага с `\`
  в путях `.sln` (3 теста), регистр путей NuGet в инфраструктуре
  `SourceStructure` (8), Windows-пути и `cmd` в тестовых данных (8), сбои,
  внедряемые через блокировки файлов Windows (3), одно зависание сборки
  фикстуры (1).
- CI (`test-suite.yml`) и релиз (`release.yml`) — только `windows-latest`,
  публикуется только `win-x64`.

## Задачи

| Задача | Суть | Статус | Depends on |
|---|---|---|---|
| [task-01](task-01-linux-green-suite-hi.md) | Main suite и lifecycle зелёные на Linux; Windows-only тесты — категорией с точным перечнем | accepted, 2026-10-21: [validation](evidence/task-01-validation.md) | — |
| [task-02](task-02-ci-linux-matrix-med.md) | Матрица ОС в `test-suite.yml`; Linux блокирует CI и релиз | accepted, 2026-10-21: [CI 39672573581](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39672573581) | task-01 |
| [task-03](task-03-release-linux-x64-med.md) | Релизная матрица: `linux-x64` self-contained и framework-dependent, `.tar.gz`, smoke | accepted, 2026-10-22: [v1.5.6](https://github.com/VuDZ/RoslynMcpServer/releases/tag/v1.5.6), [Release 39680548658](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39680548658); self-contained проверен распаковкой в WSL | task-02 |
| [task-04](task-04-linux-polish-and-docs-low.md) | Поиск SDK, сообщения, README для Linux | accepted, 2026-10-22 | — (до первого Linux-релиза) |

Порядок: task-01 → task-02 → task-03. Task-04 независима, но входит в первый
релиз с Linux. Если включить task-02 раньше task-01, CI станет постоянно красным.

## Критерии готовности темы

- Main suite и `AnalyzerLifecycle` проходят на `ubuntu-latest` и
  `windows-latest` без skip; job любой платформы блокирует релиз.
- Перед тем как сделать Linux-job обязательным, main suite на Linux прогнан
  не меньше 10 раз подряд без падений (проверка на нестабильность).
- Опубликован релиз с архивами `win-x64` и `linux-x64`; оба варианта
  `linux-x64` прошли smoke, включая загрузку проекта через BuildHost
  ([O-03](../../github-releases/README.md#o-03-совместимость-и-расширение-платформ)).
- README описывает поддерживаемые платформы и установку на Linux.

## Риски

- **Нестабильные тесты на Linux блокируют релиз.** Тесты на watcher,
  зависания процессов и heartbeat чувствительны ко времени, а inotify и
  сигналы Unix ведут себя иначе. Смягчение: серия прогонов из критериев выше
  до включения обязательности.
- **Категория Windows-only скрывает Linux-баги.** Смягчение: точный перечень
  Windows-only тестов проверяется отдельным тестом (task-01), как инвентарь
  структурных ограничений в `AGENTS.md`.
- **SDK из пакетов дистрибутива не проверяется.** CI ставит SDK от Microsoft
  через `setup-dotnet`. SDK из пакетов Ubuntu (`/usr/lib/dotnet`, линейка
  10.0.1xx) — best effort; это записывается в README (task-04).

# BuildHost/task-00 — Воспроизводимый форк без изменения поведения

- Дата: 2026-10-22.
- Статус: **in progress, 2026-10-24** — форк собран (`forks/roslyn-buildhost`, вариант A). Независимое
  ревью (GPT-6.1 Sol, Grok 4.7) проведено, находки исправлены ([разбор](evidence/task-00-review/disposition.md)):
  сравнение приведено к `compare.py`, sampler ограничен своими процессами, сценарии проверяют исходы.
  После исправлений identical на `RoslynMcpServer.sln` и Smoke (Linux), отказные сценарии совпадают;
  ранняя отмена оставляет BuildHost у обеих сторон — по решению владельца 2026-10-24 ограничение
  upstream, исправление в [task-00b](task-00b-early-cancel-buildhost-leak-low.md). Roslyn.slnx (Windows) —
  identical и после исправлений (загрузка ×0,992); офис — identical по сравнению до ревью, повтор с
  исправленным spike и сценарии на Windows pending. Принятый объём
  пункта 4 (решение владельца 2026-10-24): Smoke, `RoslynMcpServer.sln`, Roslyn.slnx и офис, без PowerToys.
- Сложность: hi; риск — несовместимость host/client и изменение состава workspace.
- Depends on: —.
- Evidence/validation: [локальная часть](evidence/task-00-fork-baseline.md).
- Контекст: [индекс исследования](README.md).

## Цель и результат

Подготовить минимальный воспроизводимый fork BuildHost, соответствующий
используемому Roslyn 5.9.0, и доказать, что подключение само по себе сохраняет
результат и характеристики обычной загрузки. Оптимизации пока не добавлять.

## Что сделать

1. Зафиксировать upstream URL, точную revision, исходную package version,
   лицензию, SDK/MSBuild, команды сборки и inventory локальных изменений.
   Не выбирать произвольно current main вместо версии серверного пакета.
2. Определить минимальную границу форка: host, общие RPC contracts и при
   необходимости клиент MSBuild loader. Проверить netcore и Windows net472
   пути; неподдержанную среду обозначить явно.
3. Сделать отдельный runner/изолированное подключение без изменения production
   references и defaults. Сохранить возможность выполнить исходный контроль.
4. Сравнить original/fork на Smoke, PowerToys и полном Roslyn с теми же
   SDK/configuration/platform/TFM/global properties. Офисное решение — после
   публичной проверки, отдельным пакетом.
5. Проверить success, missing/unsupported project, multi-TFM, cancellation/failure
   и завершение дочерних процессов. Не превращать провал fork в успешное сравнение
   за счёт незаметного ordinary fallback.

## Приёмка

На тех же fixtures совпадают project variants, documents/generated sources,
options, references/aliases, output paths, diagnostics и analyzer provenance.
Comparator из E0 не ослаблять; недостающие поля вынести как explicit limitation.
Выполнить warmup + минимум три измеренных fresh-process запуска каждой стороны,
сохранить wall, CPU, peak RAM, child inventory и load + first query.
Заметную регрессию подключения исследовать до следующих оптимизаций.

Перед C# правками прочитать полный docs/code-style.md. Focused behavioral checks,
весь SourceStructure после production C#, Release solution build и полный main CI
обязательны перед commit code/tests. При влиянии на load/prepare/publication или
analyzer lifecycle — AnalyzerLifecycle с ROSLYN_MCP_ANALYZER_LIFECYCLE=1.
Изолированный baseline не означает production readiness.

Отчёт: evidence/task-00-fork-baseline.md; здесь и в README записать дату, точный
принятый scope, ограничения, команды/receipts и статус каждой обязательной проверки.

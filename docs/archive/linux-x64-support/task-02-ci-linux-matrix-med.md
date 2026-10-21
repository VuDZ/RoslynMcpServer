# linux-x64/task-02 — Матрица ОС в CI

- Дата постановки: 2026-10-21.
- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: medium.
- Примерная сложность: 2/5; класс `med`.
- Риск ошибки: средний — ошибка в фильтре или в проверке skip может тихо
  выключить часть тестов на одной из платформ.
- Необходимые способности: `github_actions`, `powershell`.
- Статус: **accepted, 2026-10-21** — [CI 39672573581](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39672573581), четыре job зелёные, в каждом TRX `notExecuted=0`.
- Depends on: [task-01](task-01-linux-green-suite-hi.md).
- Модель, реализовавшая задачу: Grok 4.7.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [evidence/task-02-linux-main-ten-runs.md](evidence/task-02-linux-main-ten-runs.md), прогон [39672573581](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39672573581).

## Цель

`test-suite.yml` гоняет шарды `unit` и `lifecycle` на `windows-latest` и
`ubuntu-latest`. Красный job любой платформы валит CI, а через
`release.yml` (job `tests`) — и релиз.

## Требования

1. Матрица `os × shard`. Имена job и артефактов TRX содержат ОС, например
   `unit (ubuntu)` и `trx-unit-ubuntu`; артефакты разных ОС не перезаписывают
   друг друга.
2. Фильтр Linux-шарда `unit` исключает Windows-only категорию из task-01.
   Фильтр Windows не меняется по составу тестов.
3. `Assert-NoSkippedTests.ps1` работает на обеих ОС без изменений смысла:
   любой skip — провал. Пути к скриптам — через `/`, все шаги со скриптами —
   `shell: pwsh`.
4. Установка SDK одинакова на обеих ОС (сейчас `10.0.300` и `10.0.x`).
   Если к этому моменту принят `global.json` по
   [AR-13](../../architecture-review/findings.md#ar-13), использовать его.
5. `lifecycle` на Linux включается той же переменной
   `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
6. Обязательность Linux-job включается только после серии из 10 зелёных
   прогонов main suite на Linux (см. критерии в [README](README.md)). До этого
   допустим `continue-on-error` только для Linux-job, с комментарием и ссылкой
   на эту задачу. Финальное состояние задачи — без `continue-on-error`.

## Вне задачи

- Изменение состава тестов (task-01) и релизного workflow (task-03).
- Настройки branch protection в GitHub. Если required checks заданы по именам
  job, после переименования их обновляет владелец; задача перечисляет новые имена.

## Приёмка

- Ссылка на прогон CI, где все четыре job зелёные и в каждом TRX
  `notExecuted=0`.
- Число тестов в Linux-шарде `unit` равно числу на Windows минус точный
  перечень Windows-only из task-01.
- Ссылки на 10 зелёных прогонов Linux main suite перед снятием
  `continue-on-error`.

## Ход выполнения

### 2026-10-21, Grok 4.7

`test-suite.yml` гоняет `os × shard`. SDK тот же (`10.0.300` и `10.0.x`): [AR-13](../../architecture-review/findings.md#ar-13) всё ещё open, принятого `global.json` нет. `Assert-NoSkippedTests.ps1` не менялся; вызов и каталог результатов идут через `/` и `shell: pwsh`. Фильтр Windows — прежний `Category!=AnalyzerLifecycle`. Фильтр Linux `unit` — `Category!=AnalyzerLifecycle&Category!=RequiresWindows`. `lifecycle` на обеих ОС включает `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.

Linux-job помечены `continue-on-error`, пока нет десяти зелёных прогонов. Снимать флаг и принимать задачу рано.

Имена job внутри workflow и артефакты:

| Job | Артефакт |
|---|---|
| `unit (windows)` | `trx-unit-windows` |
| `lifecycle (windows)` | `trx-lifecycle-windows` |
| `unit (ubuntu)` | `trx-unit-ubuntu` |
| `lifecycle (ubuntu)` | `trx-lifecycle-ubuntu` |

Проверки вызывающего workflow (`ci.yml` и `release.yml`, job `tests`) получают префикс `tests /`. Старые имена `tests / unit` и `tests / lifecycle` пропадают. Если branch protection смотрит на них, владелец обновляет список. `release.yml` эта задача не меняет: job `tests` по-прежнему только вызывает `test-suite.yml`, а `publish` ждёт его целиком.

Серия из десяти прогонов записана в [evidence/task-02-linux-main-ten-runs.md](evidence/task-02-linux-main-ten-runs.md). После неё `continue-on-error` снят.

Прогон [39672573581](https://github.com/VuDZ/RoslynMcpServer/actions/runs/39672573581) на `cbedaaf`: все четыре job зелёные. TRX: `unit (ubuntu)` 1342, `unit (windows)` 1346, оба `lifecycle` по 13. Везде `failed=0` и `notExecuted=0`. Разница unit — четыре теста `RequiresWindows`.

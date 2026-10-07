# Подготовка нового MVP-плана

Дата: **2026-10-07**. Статус: **docs prepared / ready-for-review**.
Независимое ревью планов и реализация не начаты.

## Scope

Подготовлен новый каталог `docs/workspace-state-cache/`:
четыре канонические specs эпох, четыре execution README, **33 задачи**
(8 / 11 / 7 / 7), общий scope/маршрут, execution rules, benchmark protocol
и инструкции ревью. Индексы docs/backlog и отложенная программа ссылаются
на новый маршрут. Суффиксы задач — `low/med/hi/xhi`.

Шапки сохраняют рекомендованные models/reasoning/сложность/риск/способности,
dependencies/status и добавляют фактические модели, рецензентов, review rounds,
fixed findings и evidence. Epoch README раздельно учитывает plan review,
task reviews и implementation acceptance. Непроверенные значения — `—`.

Бенчмарки входят в каждую эпоху: ordinary/isolated round-trip; unchanged disk
hit и miss overhead; edit/build restart; selective project refresh. Один
benchmark/report не заменяет semantic correctness tests или epoch acceptance.

Прежняя программа и всё её текущее содержимое сохранены в backlog коммитом
`d4937e5`; [отчёт переноса](../backlog/workspace-load-cache/relocation-report.md).
Глубокие closure/reuse/metadata/index направления не стали обязательными
prerequisites нового MVP. Production write/admission/lifecycle инварианты сохранены.

## Авторская проверка подготовки

Это consistency check автора, **не независимый review round**. Имя модели автора:
`—` (точный ID не предоставлен контекстом сессии); рецензенты: `—`.

Проверяются локальные links/anchors нового пакета, полнота шапок,
соответствие suffix/class, наличие tasks в epoch README, зависимости и отсутствие
циклов, counts/states и связь каждой эпохи с benchmark.

Результаты author consistency check:

- **49 Markdown файлов**: новый пакет и связанные индексы/docs backlinks.
- **417 локальных links** и **18 anchors**: все проверенные targets существуют.
- **33 task headers**: обязательные поля есть, фактическое execution/review evidence
  остаётся `—`; suffix/class и planned state согласованы.
- **46 dependency edges**: все targets — существующие tasks; граф ацикличен.
- Counts **8 / 11 / 7 / 7** совпадают с картами эпох и корневым README.
- Каждая эпоха содержит отдельные plan/task/implementation statistics и benchmark link.
- `git diff --check` для изменённых docs: exit 0. Та же проверка применяется
  к окончательно staged пакету перед commit.

Проверка не анализирует будущий C# код и не заменяет reviewer verdict. Исторические
устаревшие code anchors внутри legacy `_archive/` не относятся к новым 417 links;
их точный scope записан в отчёте переноса.

## Открытые вопросы для независимого ревью

- Достаточно ли мал E1 host/preparation integration scope и можно ли получить
  real positive capture с известными public API representability ограничениями.
- Корректны ли practical input profile и обнаружение новых files/import/config,
  без обещания полноты произвольных MSBuild targets.
- Startup scan/watch/publication handshake, fresh generator/session binding
  и hydrated write support требуют review текущих production seams.
- Binary threshold 16 MiB и edge chunks 64 KiB — proposed defaults для проверки
  стоимости; blind spot эвристики сохранён явно.
- Selective reopen E3 остаётся условным направлением с narrow spike/no-go,
  не условием готовности E1/E2.

Рецензентам не требуется принять эти предположения по авторскому отчёту.
Проверять specs/tasks/current code и оставлять concrete findings по
[инструкциям](review/README.md). Review history и статистика сохраняются в каталоге.

## Пределы validation

Production C#, test code и build/test configuration в подготовке плана не менялись.
Release build, main suite и AnalyzerLifecycle **not-run / not required** для
этого docs-only пакета. Runtime disk hits, semantic equivalence, скорость и
работоспособность будущих adapters пока не проверены; это outputs будущих задач.
Existing пользовательские C# правки не включаются в docs commits.

# Эпоха 6 — Аудит контракта и документации

Статус: **планируется; только документационный аудит**. Зависимости: фактические
результаты или явно отложенные части эпох 1–5. Runtime-работа не переносится сюда.

## E6-S1. Ранняя задача DOC-EARLY

До ожидания реализации эпох 1–5 исправить ложное описание recompute-on-read:

- `docs/ARCHITECTURE.md`, описание workspace lifecycle;
- текущие архитектурные утверждения `docs/analyzer-shadow-copy/README.md`;
- remarks `ShadowCopyInSolutionAnalyzerReferencesAsync`.

Требуемое содержание: «В v1.3.5 `GetCurrentSolution()` возвращает сохранённый
`_solution` с fallback на `workspace.CurrentSolution`. Подготовка и публикация
overlay выполняются в отдельных lifecycle-точках, включая повторную подготовку
после изменения документов; чтение getter само overlay не пересчитывает».

Исторические описания сохранить как историю, снабдив точной текущей оговоркой.
DOC-EARLY — отдельная ранняя задача реализации плана, не ожидание итогового аудита.
Требуемое содержание для shipped v1.3.6 (уточнение относительно абзаца выше):
подготовка — `load_workspace` / enable / явный artifact refresh; edit, watcher flush и
post-apply **reapply mapping без analyzer I/O**, а не повторная подготовка файлов.
Чтение getter overlay не пересчитывает.

DOC-EARLY targets обновлены 2026-09-11 (`docs/ARCHITECTURE.md`,
`docs/analyzer-shadow-copy/README.md`, remarks `SolutionManager`). Полный аудит E6-S2–S4
этим не закрыт.

## E6-S2. Сверка единого контракта

Сверить с реализацией все строки [LIFECYCLE-v2.md](LIFECYCLE-v2.md), включая G/S/M/A/L/D/R:
cache hit/miss, graph-stale reopen, flag transitions/reset, preparation failure и
partial mapping, text edit, overlay apply, under-lock calls, FSW delivery/flush,
fallback/reconciliation и clear. Не заменять эту матрицу словами «load/reload».
Инвентаризация semantic entry points проверяет целостность, flush freshness и одну
базу операции отдельно; `_solution` assignment не описывается как atomic save.

Сохранённый overlay строится на контролируемой публикации. Mapping reuse реализуется
и принимается в эпохах 2/4; подготовка, rewrite, lazy load и execution — разные стадии.
Новый content path не обещает свежую CLR assembly без доказанного режима эпохи 3.
Состояние mapping другой solution не переносится; process loader имеет иной lifetime.

## E6-S3. Документы, миграции и инструкция пользователю

При выпуске фактических изменений актуализировать ARCHITECTURE: lifecycle,
write boundary, immutable generations, зависимости loader, результаты и ограничения
ресурсов. Исторический README ссылается на новую серию, исторические эпохи остаются
историей. «Pure function» означает преобразование с готовым mapping, а не copier.

Release/upgrade note обязан описать наблюдаемую смену recopy-on-edit на reuse:
в v1.3.5 edit мог случайно подхватить новые bytes; после эпох 2/4 он не обновляет
генератор. Порядок build→opt-in load и последующий build→поддержанный artifact refresh
либо process restart приведён в LC-S3. Reset не выгружает CLR; false/omitted sticky
сохраняется до U-ARB-05, при последующем выборе потребуется отдельное описание
совместимости. Новые MCP параметры, номер релиза и сроки не назначаются здесь.

Отдельно отразить изменение сохранения CodeAction/rename: неподдержанный analyzer
diff теперь отклоняется до записей, вместо молчаливого стирания отличий списка.
При частичном сохранении клиент получает точный исход и известные сохранённые пути;
это не полный успех всего запроса. Эти изменения принимаются в эпохе 4.

Описать отдельные namespaces main-only/dependency-set и отказ от in-place миграции
timestamp directories. Старые поколения не удаляются автоматически при clear;
операторская очистка — по E2-S4 после остановки владельцев. Автоочистка исторической
порчи project files не добавляется. Сохранить независимость задач path resolution
и anti-lock: исправление первой не означает автоматическое удаление workaround.
Ограничение поиска generated declarations по имени не смешивать с отсутствием
работоспособной генерации/semantic model.

## E6-S4. Приёмка документации и статус серии

Проверить ссылки, имена параметров, фактические точки lifecycle и matching tests;
указать версии окружения, команды повторения и результаты поддержанной матрицы.
Для выпущенного поведения указать фактическую версию/commit. Исследовательский
failure не выдаётся за release gate, deferred — за implemented.

Аудит может завершиться с точно документированным реализованным режимом эпохи 3
(включая выбранный restart-required) или с явно deferred результатом. Статусы
«исследование завершено», «реализация принята», «аудит завершён» и «серия завершена»
различаются. Незавершённая runtime-приёмка эпох 2/3/4/5 сохраняет серию незавершённой,
даже если документация полностью согласована. Эпоха 6 не впервые реализует mapping,
workflow записи, matcher или loader.

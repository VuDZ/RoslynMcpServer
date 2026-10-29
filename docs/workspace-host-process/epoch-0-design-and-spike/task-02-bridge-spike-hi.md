# C/E0/task-02 — Интеграционный spike прокси и host

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Grok 4.7.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — разделение session/workspace и восстановление могут нарушить публикацию, lifecycle analyzers или изоляцию проектов.
- Необходимые способности: `согласованность_компонентов`, `проектирование_проверок`, `анализ_производительности`.
- Статус: planned; план обновлён 2026-10-29 по решению владельца O-05; реализация не начата.
- Depends on: [task-01](task-01-architecture-decision-hi.md) с оставшимися решениями владельца после [task-06](task-06-environment-and-deployment-preflight-med.md); O-02…O-06 выбраны, O-01 открыт; [D/E0/task-01](../../workspace-load-performance/epoch-0-stage-measurement/task-01-load-stage-timings-med.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Показать с настоящим Roslyn: совместимые прокси разделяют один workspace,
переход одного клиента на другой проект сохраняет работу остальных, падение
host даёт одну согласованную холодную загрузку. Обычный режим без флага прежний.

## Входы и границы

`design.md` в корне темы (результат task-01) и выбор владельца. Перед C# правкой — полный
[code style](../../code-style.md). Режим включается только экспериментальным
флагом, выбранным в design; default не меняется. Production rollout,
документация для пользователей и обработка дополнительных вариантов отказа — E1.
Основные сценарии переключения, конкуренции и crash recovery входят уже в этот
spike и не откладываются до rollout. Транспорт, фасад, координация запуска,
поколения и схема bootstrap берутся из [task-03](task-03-pipe-relay-spike-med.md) /
[task-04](task-04-proxy-routing-and-recovery-spike-hi.md) в принятом design виде.
Workspace и тяжёлые данные создаются только при роли host, которая может быть
совмещена с фасадом своего чата. Proxy — обычный серверный процесс без workspace
([решение владельца 2026-10-28](../README.md#сравниваемые-модели-и-проверки-применимости-2026-10-23)): проверить, что он не загружает
workspace; проверить изоляцию tool activation. Реальная поддержка plugins отложена
владельцем в O-04; API/audits plugins не являются целью первого spike.
Уточнение плана 2026-10-29 по этому уже принятому решению: наличие Roslyn/MSBuild/plugin
assemblies в proxy допустимо; проверяется отсутствие загруженного workspace и изоляция
activation/plugin state. [Проект design task-01](../design.md) пока не принят владельцем.
Сценарии выполняются для выбранной shared-модели. При выборе нулевого варианта
задача не запускается и отмечается deferred со ссылкой на решение владельца.
Handoff/promotion/demotion проверяются только для модели ролей; общие гарантии
схлопывания, переключения и recovery обязательны для обеих shared-моделей.

## TDD

До интеграционной правки добавить тест нужного сценария с настоящим workspace,
подтвердить red по его поведенческому контракту, затем получить green минимальной
правкой. Переиспользовать контрпримеры task-04, а не копировать fake load вместо
Roslyn. Команды и результаты до/после — в evidence. Замеры памяти и задержек
выполняются отдельно; зелёный тест не доказывает экономию RAM.

## Сценарии

До окончательного выбора модели go/no-go среды/storage выполняет
[task-06](task-06-environment-and-deployment-preflight-med.md). Здесь повторить
его контроль на реализации. Решением владельца 2026-10-29 прототипы механизмов task-06 —
receipt created/reused, private fresh, отказы storage, сценарии 24–25 — перенесены сюда и
выполняются здесь впервые. Prelaunch proof по решению владельца O-06 не нужен: смешанную
сборку под живым proxy исключают O-03 и snapshot launcher. Проверить in-memory identity
proxy/host (протокол, каталог/schemas, MVID загруженной сборки сервера): расхождение даёт
отказ attach с указанием перезапустить proxy, disk hash после старта не используется.
Reuse launcher snapshot — no-go по аудиту
[task-06](evidence/task-06-environment-and-deployment-preflight.md#7-аудит-launcher). Проверить: совместимые реальные пары Codex/Windows и Claude Code/Linux
одного checkout, WorkspaceKey path/Configuration/Platform/TFM и отдельный namespace/
image admission. Разные session environments не дробят WorkspaceKey. Host сохраняет
контекст инициатора; CLI proxy — собственную среду. Reused load показывает actual
context и инструкцию fresh; создатель нового host дополнительный блок не получает.
No-go возвращается владельцу; synthetic N=10 не заменяет отрицательную применимость.

Контроль workspace — sample project и второй небольшой отличимый проект; runtime
plugins=none. SamplePlugin можно использовать отдельно как лёгкий контроль
регистрации/маршрутизации, без обязательной поддержки реальных plugins. По возможности
повторить на большом решении коллег. Наличие большого решения и результаты
замера фиксируются; недоступность не заменяется вымышленным pass.

**RAM reuse и свежесть:**

1. Сессия 1: `load_workspace` и `find_symbol_definition`; клиент отключается, пока другой контрольный proxy остаётся подключён к host.
2. Правка `.cs` в загруженном проекте при живом host.
3. Сессия 2: тот же `load_workspace` — запись `workspace_load_stages` с
   `cached=true`; `find_symbol_definition` видит правку из шага 2.
4. Сохранённые `.cs` видны остальным клиентам того же host; существующие правила
   graph-stale, generated files и write boundary не ослаблены.

**Схлопывание и переключение:**

1. Десять MCP-клиентов одновременно загружают одинаковый workspace с совместимыми
   настройками: одна физическая ordinary-загрузка, общий workspace. В модели
   ролей один из MCP — host, остальные — proxy без workspace; в модели отдельного host
   все десять MCP — proxy, которые согласованно запускают один host.
2. Чат1/чат2 работают с A/проект1. Чат2 загружает проект2: выбирается готовый B
   либо создаётся B. Ответы и символы чат1 остаются от проекта1, чат2 — от проекта2;
   MCP-сессии не переинициализируются, cwd A и его descriptor не меняются.
3. Отказ загрузки проекта2 сохраняет прежнюю привязку чат2 к живому A. Проверить
   переход с незавершённым запросом по правилам design, а также несовместимые
   Configuration/Platform/TFM и session defaults/build args. Иная analyzer policy
   не расширяет WorkspaceKey: проверить явный context-mismatch/fresh без изменения A.
   Тот же переход и отказ проверить через optional workspacePath обоих
   `list_projects` и `get_project_graph`: read-часть не вызывает LoadAsync чужого
   path в A. Для своего key проверить cache hit/graph-stale refresh, для omitted
   path у unbound list/graph — отсутствие новой lazy load.
4. **Только модель ролей.** Чат1 запускает `mcp1`, который сам держит проект1
   и обслуживает `mcp2`/`mcp3` из чат2/чат3. Чат1 загружает проект2: `mcp1`
   сообщает release проекта1,
   согласованно освобождает роль, один из оставшихся MCP (например `mcp2`)
   становится новым host проекта1. Чат3 подключается к нему. Проверить одну
   новую ordinary-загрузку проекта1, паузу/ошибки по design и отсутствие ответов
   проекта2 для чат2/чат3. Отдельно — потерянное уведомление и отказ преемника.
5. **Только отдельный host.** Чат1 первым инициирует запуск host проекта1,
   затем переключается на проект2. Host проекта1 продолжает обслуживать чат2/чат3
   без handoff и новой ordinary-загрузки; привязки и поколения не подменяются.

**Private fresh и ответ load:** реализовать сценарии 22–23 design. CLI --force_fresh
и tool argument forceFresh создают host, не workspace внутри proxy. Shared сосед
не затрагивается; private не discoverable другим proxy и не становится общим.
Проверить repeat load, explicit новый fresh, failure/cancel, private recovery/EOF,
created/reused race и отсутствие reuse block у создателя. Reused cached/brief load
содержит actual context/инструкцию; отсутствующие global properties и env secrets
не превращаются в выдуманные значения или raw dumps.
Сценарий 24: tool-only private intent переживает reset defaults/unbind, retire
и cleanup host отдельно от binding. Explicit/config lazy load того же resolved key
не возвращается в shared, не восстанавливая при этом сброшенные load defaults.
Сценарий 25: старый creator ушёл, shared host crashed, surviving proxy имеют разные
contexts. Новый startup winner задаёт новый контекст, остальные проверяют policy
receipt до исполнения; created/reused и последующий load output отражают новый
generation. Не объявлять новую среду восстановленной старой и не replay-ить calls.

**Падение и восстановление:**

1. Завершить A принудительно при десяти клиентах. Прерванные вызовы получают
   явный отказ; выжившие клиенты восстанавливают доступ к одному новому поколению A
   с одной физической загрузкой. B и его клиенты продолжают работать.
2. В модели ролей убить MCP-кандидата после получения роли и до readiness;
   в модели отдельного host проверить смерть запускающего MCP и самого host
   до readiness. Доказать исключительное владение до MSBuild open; отсутствие
   второго тяжёлого host и дублирующей загрузки при восстановлении.
3. Отказ загрузки/crash loop завершается по бюджету, без автономных workspace
   в каждом прокси и повторной полной загрузки от каждого ожидающего клиента.
4. Отмена одного ожидающего load не мешает остальным. Поколения, session tool
   activation, подписки/progress/cancellation и отсутствие replay после
   неопределённого исхода проверяются вместе с настоящими инструментами.
5. Reset/stop и restart-required после обновления analyzer проходят по design;
   перезапуск прокси не объявляется обновлением загруженного в host analyzer.
   O-02 выбран владельцем 2026-10-29: shared reset — global retire host
   ([решение](../evidence/2026-10-29-task-01-reset-decision.md)). Проверить при
   двух клиентах: чистая read-стадия поиска соседа отменяется с просьбой повторить;
   отдельно reset во время explicit load у readOnly list_projects/get_project_graph
   и lazy load/prepare у semantic read. Metadata readOnly не заменяет фазовую
   классификацию: MSBuild/tasks/generators/binlog могут иметь эффекты. Проверить
   drain budget и фактический исход/явную неопределённость при forced stop, отсутствие
   ложного «побочных эффектов нет» и replay, admission закрыт до смены generation.
   Уже начатая запись завершается;
   proxy CLI не прерывается и не получает proof; следующий вызов соседа ждёт одну
   загрузку нового generation и один раз сообщает о перезапуске. При N=10 reset→load
   даёт одну physical attempt; replay нет.
6. Add/remove package и применённый rename при двух клиентах меняют общую
   WorkspaceEpoch и запрещают прежние tickets/proof state всех сессий, даже при
   потерянном notification. Старый proxy CLI может завершиться без подтверждения
   нового graph; конкурентный reload одного key — одна attempt. Dry-run rename
   не инвалидирует epoch; partial write/clear failure остаются fail-closed.

**Build/test и CLI:** проверить размещение этапов по design: разрешение проекта
и session defaults, admission/build proofs, запуск CLI и обработку результата,
включая DLL-маршрут тестов. Смена привязки или поколения и падение host до,
во время и после CLI не дают proof для неверного workspace/поколения. Если CLI
запущен в proxy, потеря host сама по себе не обрывает его; результат CLI и
отказ подтверждения build proof различаются, автоматического replay нет.

Для доказательства одной загрузки нужны наблюдаемые счётчики/события MSBuild open,
а не только один PID в конце. Для гонок — управляемые барьеры, не только задержки.

**Git и graph heuristic:** `get_changed_files` сохраняет Suggested test projects
по исходному snapshot и результату git, включая switch во время git; без graph
список пуст. Kill host перед вычислением heuristic не скрывается под видом пустого
списка. Детальные контракты и проверки — сценарии 17–19 раздела 11 design.

**Память и задержка:** сравнить N=1 и N=10 совместимых клиентов в обычном и общем
режимах на одинаковых контроле и нагрузке. Зафиксировать память всех прокси и
host, метод и моменты измерения (после load и одинакового semantic query),
число процессов/загрузок. Проверить отсутствие workspace у proxy; список загруженных
сборок сохраняется как диагностика, присутствие MSBuild, Roslyn и plugins допустимо;
остаточную память/CLR lifetime у бывшего host после demotion — только в модели ролей.
Не обещать конкретный процент экономии до замера; указать также цену второго
workspace и короткой паузы перед завершением host без клиентов. Сравнить результат
с оценкой экономии из [task-05](task-05-applicability-checks-med.md).

**Конкуренция клиентов:** задержка одинакового semantic query одного клиента,
пока остальные N−1 выполняют тяжёлые запросы, в сравнении с N=1 и с обычным режимом.

**Deployment:** отдельно проверить RendezvousRoot/StorageRoot, tmpfs/noexec/space и
ENOSPC; никаких images/logs в `/tmp`. На Windows корень создаётся с защищённым DACL
только владельца, без наследования; существующий корень с чужим ACE, включая унаследованный
ACE песочницы Codex, отклоняется. Замерить image copy/hash/verify и cached attach;
time-to-first-useful отсчитывается от запуска процесса. Stat stamps не заменяют content
identity образа host. Snapshot launcher как образ host не используется (аудит task-06).

## Конкретный результат

- Минимальные host и MCP-фасад за флагом; тесты общих сценариев и сценариев
  выбранной модели выше. Неприменимые сценарии другой модели перечислены с причиной.
- `evidence/task-02-spike.md`: time-to-first-useful сессии 1 и сессии 2
  (n не меньше пяти для сессии 2), записи стадий, счётчики load, память N=1/N=10,
  события смены привязки/поколений, recovery и ограничения по платформам/контролям.

## Проверки

- Без флага: полный main suite без изменений ожиданий; запуск по stdio прежний.
- С флагом: сценарии выше проходят; успешные результаты анализа совпадают с
  in-process режимом на том же контроле и effective host context. Для O-05 отдельно
  показать проект, зависящий от environment: shared context первого создателя может
  отличаться от ordinary второго proxy, а forceFresh использует контекст инициатора.
  Полная environment parity не является критерием допуска reuse.
  Ошибки recovery/lifecycle соответствуют
  design и не скрываются сравнением только успешных ответов.
- Канал недоступен другому пользователю ОС (проверка на доступной платформе;
  непроверенные платформы перечислены).
- Validation: SourceStructure, Release build, полный актуальный main filter
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` (на Linux также `&Category!=RequiresWindows`);
  AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, поскольку интеграция меняет
  время жизни workspace/анализаторов и используемые пути load/prepare/publication.
  Patch bump, строка `CHANGELOG.md` с пометкой experimental.

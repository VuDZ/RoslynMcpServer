# Общий pull-контракт состояния проектных входов

Дата: 2026-09-30. Статус: **план; runtime API не реализован**.
Канон общего состояния для watcher, WPF-фильтра, build freshness и будущей
workspace-load-cache Epoch 3. Конкретные C# имена/API оформляются в эпохах;
набор данных и инварианты ниже обязательны.

## Один владелец сессии

Один владелец создаёт `generation` для всей группы watchers загруженного
workspace, снимка `Project.FilePath`, карты ролей и журнала revisions.
Каждый watcher захватывает этот token; отдельные счётчики WPF, input watching
и build freshness не вводятся. Watcher identity внутри группы может быть
отдельной, но не является ещё одной session generation.

Stop/reset/reload/смена load key закрывает сессию. Новая получает новый token;
поздний callback и build result старой generation не изменяют новую.
Повторный cache-hit load той же сессии не меняет generation. Повторная оценка
membership внутри действующей сессии меняет revision карты, а не создаёт
второй независимый session counter. Полная замена группы watchers создаёт
новую сессию и обесценивает старые подтверждения.

Место хранения может быть внутренним состоянием `SolutionManager` или общим
сервисом с его lifecycle. Запрещена вторая независимо обновляемая карта
membership/revisions. Build-state хранит собственные подтверждения результатов
и cursors общего снимка; это не копия источника истины проектных входов.

## Данные неизменяемого снимка

Pull возвращает согласованное состояние одной generation:

- `generation`: единый token watcher-сессии;
- `snapshotRevision`: монотонный revision публикуемого состояния;
- `membershipRevision`: revision оценённой карты, источник evidence и фактически
  загруженные project instances/TFM, оценённые зависимости и их completeness;
- `loadedProjectPaths`: нормализованный снимок путей для WPF-проверки;
- пути: canonical path, revision значимых input changes, все owners-project
  instances, роли/производители, present/missing и pending/unresolved event state;
- membership regions: границы, известные/потенциальные inputs, revision оценки
  и evidence исключений/полноты, включая imports и absent inputs;
- coverage: complete/unknown для supported scope, причины unknown и affected
  owners/regions; global unknown при неопределённом scope или неподтверждённом TFM;
- отдельные изменения outputs/generated results, не выдаваемые за input edits
  производящего build.

У фактически загруженного instance должна быть идентичность внутри generation,
а не только имя проекта или путь `.csproj`. Роли и completeness относятся
к подтверждённому context; отсутствие документа не делает путь irrelevant.

## Чтение и сохранение изменений

Consumer запрашивает текущий snapshot и сравнивает его с сохранённым cursor/
подтверждением. Вызов не потребляет события и не запускает build/reload.
Публикация snapshot не включает вызовы consumer на watcher-потоке. Изнутри
manager lock не допускается повторный acquire того же semaphore.

Значимые C# и не-C# изменения записываются в session state независимо от
наличия consumer. До первого запроса сохраняются revisions, роли/owners и
unknown reasons. Semantic flush не очищает их; собственная успешная persistence
тоже попадает сюда независимо от watcher echo.

Не требуется бесконечный список каждого callback: допустимо агрегировать
последние revisions путей/regions. При потере информации о затронутом пути,
неопределённой новой membership или недостаточной истории сохранить unknown
с причиной и scope, а не выдавать пустую ленту как отсутствие изменений.

Callback регистрирует событие/pending state своей generation до того, как
consumer может получить разрешение на reuse. Если содержимое/роль ещё не
проверены, snapshot обозначает pending/unresolved; старое подтверждение не
считается достаточным. Точная content/revision reconciliation выполняется
по [протоколу own writes](epoch-3-change-feed-and-recovery.md).

Согласованный снимок не является атомарным filesystem snapshot и не доказывает
отсутствие потерянных FSW-событий. Build freshness отдельно валидирует inputs/
membership/outputs перед skip; неизвестное coverage никогда не становится
complete от успешного build или очищения очереди.

## Потребители и порядок

- WPF-исправление может идти первым: минимальный общий session context с token
  и `loadedProjectPaths` не требует готового input tracker. Coverage остальных
  полей остаётся unknown; будущие эпохи расширяют тот же источник.
- Watcher Epoch 1 задаёт модель и evidence; Epoch 3 предоставляет production
  pull provider и сохранение revisions. Классификатор имени `_wpftmp` общий,
  не дублируется и не требует статуса shipped соседней серии для переиспользования.
- Build freshness Epoch 1 проектирует подтверждения поверх этого контракта.
  При отсутствии provider/evidence всегда выполняется предварительный build;
  consumer не создаёт собственный FSW feed, input index или session counter.
- Workspace load cache Epoch 3 использует ту же модель. Её нерешённые freshness
  policies остаются самостоятельными; этот документ их не закрывает и не
  вводит долговременный store.

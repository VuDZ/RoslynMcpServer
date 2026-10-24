# Независимое ревью BuildHost/task-00, раунд 2

- Модель: **GPT-6 / Codex**; точный вариант backend в контексте сессии не указан.
- Дата: **2026-10-24**.
- Проверенный HEAD: **`47725bc4eedae74111416a0077f91d98c07e93c1`**, ветка `research/workspace-load-performance`.
- Запрос: [request-round-2.md](request-round-2.md). База сравнения: `a72fbbc`; отдельно рассмотрен `7a5d41d`.
- Изменён только этот отчёт. Исправления, статусы задач, исходные ревью и disposition не редактировались.

## 1. Вердикт

**Task-00 пока не принимать.** Большинство находок первого раунда закрыто; исправленный spike действительно
собирается повторно и показывает identical на локальном решении. Но условие публикации текущего дерева
не выполнено (R2-01), а проверка kill-сценария допускает ошибки, не объясняемые согласованной
недетерминированностью upstream (R2-02). Перед приёмкой также надо согласовать execution status
README/task-файла с новым офисным evidence (R2-07).

**Часть B требует исправления R2-03:** категория исключает три строки theory, которым SDK 10.0.300
не требуется для прохождения и проверки отказа в допуске. Само закрепление release SDK сделано правильно.
AR-18 подтверждён запуском для явного `latestFeature`, но его описание ошибается в политике по умолчанию
(R2-08).

Решения владельца не пересматриваются: PowerToys исключён; одинаковая утечка ранней отмены допустима
в task-00 и вынесена в task-00b; история research-ветки не переписывается, перенос в main — без её истории.
Последнее решение записано в запросе раунда 2, не в виде исполненной Git-операции.

Фактической регрессии самой сборки BuildHost в выполненных success/cancel/kill-прогонах не обнаружено.
Находки о проверяющем коде ниже — воспроизведённые способы получить необоснованный зелёный результат,
а не утверждение, что опубликованные офисные замеры уже содержат такую регрессию.

## 2. Находки

### R2-01 — major: текущие tracked-файлы всё ещё содержат имена офисных проектов

**Места:** `docs/workspace-load-performance/epoch-0-stage-measurement/evidence/office-first-search-2026-10-23.md:43,48,55,108,113,138,143,158,162,194`;
`docs/workspace-load-performance/epoch-0-stage-measurement/evidence/first-search-investigation-2026-10-22.md:1,15,23,45`;
`docs/workspace-load-performance/epoch-0-stage-measurement/task-08-source-search-and-office-results-med.md:36,38`;
имя файла `docs/workspace-load-performance/epoch-0-stage-measurement/task-09-first-search-io-and-private-project-profile-hi.md`
и его заголовок, строки 8, 16, 46, 71. Есть и ссылки/упоминания в других E0-документах.

**Проблема и сценарий.** Удаление имени решения из нескольких документов не удалило имена внутренних
проектов из текущего дерева. В частности, офисный отчёт прямо связывает их с измерениями офисного
решения. При публикации или переносе текущего дерева в main эти имена останутся доступны независимо
от отказа от переноса истории. Это нарушает пункт A.7 данного запроса.

**Доказательство.** Проверены tracked-совпадения `git grep`, затем контекст документов, а не ignored
логи или история. Конкретные внутренние значения здесь повторно не публикуются; приведены точные
места. Например, строки 43–55 офисного отчёта содержат разбиение по именованным офисным проектам.

Есть остаток F-06 и в генераторе: `Invoke-BuildHostForkScenarios.ps1:224–230` скрывает сообщение
внешнего решения только при наличии `/` или `\`. Вызов настоящего `Format-Outcome` со значением
`failed: System.InvalidOperationException: ConfidentialProject failed` сохраняет `ConfidentialProject`.
Это синтетическое имя. Отсутствие разделителя пути не гарантирует отсутствия внутреннего имени.

**Исправление.** Обезличить имена проектов в текущих tracked-документах, включая имена файлов и ссылки,
сохранив агрегаты. Для внешнего решения выводить только тип исключения независимо от наличия пути.
Историю не переписывать. После этого повторить проверку текущего tracked-дерева.

**Происхождение:** незакрытая часть F-06 в генераторе и новая находка по расширенному условию публикации A.7.
В первом раунде F-06 не утверждала, что было проверено всё дерево ветки на офисные имена.

### R2-02 — major: kill-сценарий принимает произвольную ошибку и потерю workspace

**Места:** `spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1:132,152–158,209`.

**Проблема.** Для `killed` достаточно любого `failed:*`; для `completed` достаточно двух замеченных
host. Флаг `$CompareOutcome = $false` отключает одновременно сравнение исхода и числа проектов/документов.
Диагностики kill-запусков тоже не сравниваются. Допуск двух нормальных upstream-исходов превратился
в допуск произвольного исключения или произвольного результата успешно завершившейся загрузки.

**Воспроизведение.** Из AST скрипта извлечены и исполнены его настоящие `Test-Run` и `Test-Pair`.
У обоих синтетических запусков: правильный ExpectedHost/hosts, `unidentified=0`,
`RemainingBuildHosts=0`, ненулевой `DisturbedAtMs`, `Expect=killed`, `Mode=kill-host`.
Оригинал имеет `completed`, 5 проектов и 100 документов. Проверены два варианта форка:

- `completed`, 2 host, **0 проектов / 0 документов**: `Problems.Count = 0`;
- `failed: System.NullReferenceException: Object reference not set to an instance of an object.`:
  `Problems.Count = 0`.

В обоих случаях вызвано `Test-Pair $original $fork $false $false`, как в строке 209.
Это проверка предикатов скрипта; реальный форк для получения таких результатов не изменялся.

**Почему это сверх необходимого.** Разные connection errors и разные исходы двух запусков ожидаемы;
`NullReferenceException` или потеря всех проектов этим не объясняются. Равенство двух нестабильных
исходов возвращать не нужно.

**Исправление.** Проверять каждый исход независимо: ошибки классифицировать по структурированной
цепочке исключений/известным транспортным причинам, сохраняя несколько допустимых сообщений;
успешное восстановление сравнивать с недистурбированной загрузкой той же стороны по observation,
ответу поиска и диагностике. Добавить отрицательные самопроверки на два примера выше. Для устойчивого
покрытия обеих ветвей можно отдельно организовать убийство между запросами и во время запроса,
а случайный сценарий оставить дополнительным контролем.

**Происхождение:** новая слабость проверки при исправлении F-03. Основание отказа от равенства
upstream-исходов верно; текущая реализация отбрасывает больше проверок, чем это основание требует.

### R2-03 — major: RequiresPinnedSdk исключает три строки theory, проходящие без закреплённого SDK

**Места:** `RoslynMcpServer.Tests/WorkspaceLoadCache/DependencyEvidenceRunnerTests.cs:21–36,47–51`;
`RoslynMcpServer.Tests/WorkspaceLoadCache/PinnedSdkTestInventoryTests.cs:25`;
`.github/workflows/test-suite.yml:29,41`.

**Проблема.** Trait стоит на всей theory из 12 строк. Для `multi-target`, `missing-reference` и
`non-sdk` проверка успешного restore/build специально не выполняется. Эти строки проверяют
`unknown`/`unsupported`, отсутствие разрешения overlay и неполную closure, даже когда SDK недоступен.
Категория скрывает их вместе с девятью строками, которым SDK действительно нужен.

**Воспроизведение на неизменённом tracked HEAD.** Сделана отдельная раскладка dotnet: копия
`dotnet.exe`, ссылки на установленные host/shared/packs и единственная папка SDK **10.0.402**.
`--list-sdks` показывает только её. Сам test runner запущен этим изолированным `dotnet.exe`, PATH и
DOTNET_ROOT переданы детям. Установленный SDK 10.0.300 не удалялся и не изменялся.

Команда тестирования: `dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~Real_build_and_dtb_observations_do_not_authorize_unproven_closure`.
Результат: **9 failed / 3 passed / 0 skipped**. Девять ошибок содержат запрос установить SDK 10.0.300;
все три passed — ровно перечисленные выше строки. TRX:
`spikes/BuildHostFork/results/review-round2-without-pin/without-pin-isolated-host.trx` (ignored).

Первый контроль с обычным внешним dotnet дал 12 passed: Windows находила обычный dotnet из каталога
запускающего host раньше PATH. Поэтому он не считается доказательством отсутствия SDK; вывод основан
на повторе с изолированным host и явными SDK-not-found в девяти строках.

**Исправление.** Разделить theory: девять строк оставить в RequiresPinnedSdk, три отрицательных контроля
сохранить в main-наборе; обновить inventory. Если `multi-target` и `missing-reference` должны проверять
именно состоявшиеся MSBuild-наблюдения, сначала усилить их assertions, чтобы отсутствие SDK не давало
pass, и только затем обосновывать их исключение. Для `non-sdk` отказ происходит до SDK evaluation.

**Происхождение:** новая находка, часть B. Inventory правильно закрепляет список методов, но не доказывает
необходимость SDK для каждой строки theory.

### R2-04 — minor: старый JSON получает неподтверждённую зелёную проверку процессов

**Места:** `spikes/BuildHostFork/ObservationComparer.cs:129–146,184–185`.

**Проблема и сценарий.** Отсутствующие `RemainingBuildHosts` и `unidentified` пропускаются, а отсутствие
ExpectedHost или hosts завершает проверку. Но сводка всё равно пишет
`BuildHost processes (own folder, none left 10 s after the load): ok` и `result: equivalent`.
Старый sampler не доказывал ни происхождение host, ни завершение процессов. Новый comparer
не может восстановить эти сведения из старых файлов.

**Доказательство.** В синтетических равных раундах обеих сторон удалены эти четыре поля. Реальный
`compare` вывел обе указанные зелёные строки и вернул **0**. Другой контроль с существующим
`unidentified=1` корректно вернул 1: проблема именно в отсутствии данных.

**Исправление.** Оставить возможность сравнить старые project/search observations, но помечать
процессные проверки `not recorded / not checked`; не выдавать полную эквивалентность по критериям
нового task-00 при отсутствующих обязательных полях. Добавить self-check старого формата.

**Происхождение:** G-03 закрыта для нового формата, но не для заявленного пересчёта старых результатов.
Новые прогоны с полным форматом этим замечанием не опровергаются.

### R2-05 — minor: естественное появление проекта скрывает любые отличия его полей

**Места:** `spikes/BuildHostFork/ObservationComparer.cs:76–79,111–117`;
`docs/workspace-load-performance/research-buildhost-fork/evidence/task-00-fork-baseline.md:86–106`.

**Проблема и сценарий.** Если проекта нет в reference original round 1, его появление описывается
только парой `ProjectKeys: absent -> present`; `continue` пропускает поля. Если проект появился в
original round 2, любой проект с тем же ключом у форка становится natural, включая не встречавшиеся
у оригинала options/references/documents.

**Доказательство.** Original round 1: `Projects={}`; original rounds 0, 2, 3:
`Projects={P:{Field:'"A"'}}`; все fork rounds: `Projects={P:{Field:'"FORK-ONLY"'}}`.
Реальный comparer: natural 3, unexplained 0, **equivalent**, exit **0**.

Это поведение есть и в `compare.py`: несовместимости с эталонным алгоритмом здесь нет. Но это
дополнительная граница доказательства, отсутствующая в списке ограничений. В опубликованных новых
сводках natural variance равна нулю, поэтому этот случай не опровергает именно их результаты.

**Исправление.** Как минимум явно записать ограничение и добавить отрицательный self-check.
Для дальнейшего использования как correctness gate не принимать переменный состав проектов без
проверки полей появившегося проекта по round 2 либо отдельного результата `not shown equivalent`.
Это усиление эталона, а не восстановление утраченного поведения compare.py.

**Происхождение:** выявленное ограничение G-01/F-04; базовое правило G-01 восстановлено правильно.

### R2-06 — minor: второе -match стирает тип исключения при redaction

**Место:** `spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1:226–227`.

**Проблема.** Первый `-match` сохраняет тип в `$Matches[1]`. Второй успешный `-match '[\\/]'`
перезаписывает `$Matches`, не создавая группу 1. Возвращаемая строка теряет тип.

**Доказательство.** Настоящие функции Format-Redacted/Format-Outcome извлечены из AST и исполнены
под StrictMode 2.0. Для внешнего решения, redaction-корня `E:\Private` и исхода
`failed: System.InvalidOperationException: e:/PRIVATE/ConfidentialProject/Secret.csproj failed`
получено **`failed:  (message with paths not shown)`**.

**Сценарий.** Любая внешняя ошибка с путём попадает в ветку, где сводка должна оставить тип, но
теперь не оставляет даже его. Это ухудшает диагностику и противоречит описанию F-06.

**Исправление.** Сохранить тип сразу после первого match; проверять наличие разделителя через
`[regex]::IsMatch`, не изменяющий `$Matches`, либо убрать условие о пути при реализации R2-01.

**Происхождение:** новая ошибка исправления F-06. Сами case/separator redactions работают.

### R2-07 — minor: task-файл и индекс не отражают завершённый офисный повтор

**Места:** `docs/workspace-load-performance/research-buildhost-fork/README.md:29`;
`docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md:10–11`.

**Проблема.** Оба статуса всё ещё говорят «офис — identical по сравнению до ревью» и «повтор pending».
В evidence на строках 186–217 уже описан новый прогон, а строка 204 прямо говорит, что он заменяет
пересчёт старых JSON. Завершающий список «Не выполнено» также помечает повтор выполненным.

**Сценарий.** Следующий исполнитель получает разные execution-status summaries и повторяет уже
сделанный прогон либо ошибочно считает, что evidence отсутствует. Это нарушение Task status bookkeeping.

**Исправление.** Согласовать дату, выполненный scope и ссылку на новый evidence в task-файле и индексе.
До устранения замечаний ревью оставить `in progress`, отдельно указав pending acceptance/review fixes.
Не объявлять task accepted только на основании офисного identical.

**Происхождение:** новое расхождение после `1afd494`; первоначальная F-10 про «реализация не начата» исправлена.

### R2-08 — minor: AR-18 ошибочно называет latestPatch политикой по умолчанию

**Место:** `docs/architecture-review/findings.md:367–368` (абзац «Последствие» AR-18).

**Проблема.** Без явного rollForward используется **patch**, не latestPatch. При установленной точной
версии patch оставляет её; latestPatch выбирает более новый подходящий patch. Поэтому нельзя переносить
воспроизведённый эффект явного `latestFeature` на обычный global.json с установленным точным SDK.

**Доказательство.** На этой машине `{sdk:{version:"10.0.300"}}` дал `dotnet --version = 10.0.300`;
тот же version с `rollForward:"latestFeature"` дал **10.0.402**. Правило по умолчанию подтверждено
[официальными matching rules](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#matching-rules).

**Исправление.** Исправить название default policy и сузить соответствующую формулировку последствий.
Основную AR-18 оставить открытой: явный разрешающий rollForward действительно обрабатывается неверно,
что воспроизведено ниже. Отложить permissive корневой global.json до исправления AR-18 разумно;
строгий pin с disable технически возможен и этой ошибкой сам по себе не запрещён.

**Происхождение:** новая неточность документации части B; не опровержение основной AR-18.

## 3. Проверено и в порядке

### Закрытие disposition первого раунда

- **G-01:** reference — первый измеренный раунд, natural — только разница первых двух измеренных
  оригиналов; поздние оригиналы проверяются, warmup не участвует в project/diagnostic admission.
  Синтетический late-only C отклоняется. Симметрическая разность диагностик и ключ `(Key, Category)`
  реализованы. Отдельная граница при появлении проекта — R2-05.
- **F-07:** естественное отличие диагностик теперь действительно даёт `natural variance only`.
- **F-01:** exclusions `results/**`, `bin-fork/**`, `bin-fork-broken/**` работают. Два последовательных
  сценарных запуска пересобрали spike после генерации фикстуры.
- **F-02:** чужие процессы больше не включаются только по имени; нужен descendant и подходящее время
  старта. Нечитаемый дочерний dotnet становится unidentified и не попадает в словарь целей Kill.
  Сверки StartTime есть и при ожидании, и при Kill. Ограничения идентичности описаны ниже.
- **F-03:** выбор задержки, проверка состоявшегося воздействия, исхода cancel, missing host и
  процессных остатков добавлены. Ранний выход до воздействия теперь отмечается inconclusive и даёт
  failure. Недостаточность kill admission — R2-02.
- **G-02:** client CPU/peak memory читаются сразу после LoadAsync, до search и ожидания выхода.
  Это накопленные метрики процесса к концу загрузки, включая его запуск/регистрацию, не CPU-delta
  только тела OpenSolutionAsync. Прежние значения после поиска с ними правильно не смешиваются.
- **G-03:** новый JSON проверяется на свою netcore-папку, ненулевой RemainingBuildHosts и unidentified.
  Старый формат — R2-04.
- **G-04:** полный fault exception сохраняется в Error; outcome содержит тип и первую строку сообщения.
  Для completed-load пары сравниваются; произвольные fault-исходы kill слишком широко допускаются.
- **F-04:** ограничения содержимого документов, generated sources, опций, analyzer identity и workspace
  diagnostics перечислены верно по SolutionObservation. Дополнения — R2-05 и процессные пределы ниже.
- **F-06:** имя внешнего решения из заголовка scenarios удалено; case/separator замены исправлены.
  Полное закрытие отсутствует: R2-01 и R2-06.
- **G-05:** Features=strict и WarningLevel=9999 присутствуют; остальные проверенные настройки не потеряны.
- **G-06:** имена проектов совпадают с именами сборок. В фактическом fork deps.json три libraries:
  BuildHost/5.9.0, Locator/1.10.2, Contracts/5.9.0; Contracts.Reference отсутствует. Contracts FileVersion
  **5.9.0.0**. У original две сборки имеют пакетную версию 5.9.0-1.26357.3; ресурсное отличие описано.
- **F-09:** git attribute text для upstream unset (`-text`); SHA-256 **109/109** файлов из чистого
  HEAD archive совпали. В давно существующей рабочей копии **105/109** всё ещё CRLF и не совпадают:
  новое правило не переписывает автоматически уже materialized файлы. Это не расхождение Git blobs
  и не основание объявлять фиксацию attributes неработающей; такой checkout требует обновления файлов.
- **F-05:** новые сводки Smoke/local/Roslyn/office записаны; PowerToys явно исключён решением владельца;
  прежний неуспешный Linux main-run не переименован в pass, последующий новый фильтр отмечен отдельно.
  Сырые historical observations недоступны, их числа проверены только на согласованность текста.
- **G-07:** SDK-style net472 и fallback Framework→NetCore объяснены правильно. Два офисных host не
  объявлены доказательством конкретного kind второго процесса; эта неопределённость честно сохранена.
- **F-08:** нет прежнего доказательства «не замедляет» из близких медиан. CPU назван необъяснённым
  наблюдением; первый поиск +2,6/+3,4% явно записан. Числа load 40844/41189 и 42035/42055 согласуются
  с ×0,992 и ×1,000; search 21571/21024 и 36995/35769 — с +2,6% и +3,4%.
- **F-10:** общий статус исправлен на in progress; новый частный status drift — R2-07.
- **F-11:** ProjectMerger.cs назван по типу. ProcessQuery.cs также соответствует типу.
- **G-08:** сравнение и форматирование разделены; счётчик именуется числом различий, не полей.
- **G-09, отклонение обосновано:** Invoke-Native не перенаправляет stderr. Настоящий helper под
  Windows PowerShell 5.1, StrictMode 2.0, ErrorActionPreference=Stop успешно выполнил cmd с текстом
  в stderr и exit 0. Полный повтор сценариев под 5.1 тоже прошёл. Ошибку redirected stderr
  Invoke-Spike/Get-GitOutput обрабатывают отдельно.
- **G-10, отклонение обосновано в текущем scope:** корневого global.json нет; проекции обеих сторон
  работают с одной фикстурой и клиентом. Гипотетическое добавление ancestor global.json может поменять
  доступный SDK/успешность подготовки, но не является доказанной асимметрией форка сейчас.

### Процессы, upstream и границы сценариев

- `/proc/<pid>/stat` разбирается после **последней** закрывающей скобки: пробелы/скобки в comm не
  сдвигают state/ppid. Это проверка кода; Linux-выполнение в этой сессии не проводилось.
- Windows class 0 и шестислотовая раскладка ProcessBasicInformation дают нужные смещения ppid:
  x86 размер 24/offset 20, x64 и ARM64 размер 48/offset 40. ExitStatus/BasePriority в native —
  32-битные значения с padding на 64-битной ABI; IntPtr-поля сохраняют смещения последующих членов.
  Сверено с [описанием PROCESS_BASIC_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntqueryinformationprocess).
  Фраза комментария «every member is pointer-sized» буквально неточна, но используемый layout верен.
- Stop использует Timer.Dispose(waitHandle) и ждёт текущие callbacks до заключительного Sample.
  Защита словарей lock присутствует. Непрерывное дерево до трёх уровней поддержано; при уже умершем
  промежуточном родителе ancestry теряется. На Linux возможен reparenting. Для direct child BuildHost
  это не мешает: его родитель — живой spike. Обобщать sampler на произвольное дерево процессов нельзя.
- Процесс между polls может быть полностью пропущен. StartTime с допуском 1 с — приближённая, не
  строгая защита от PID reuse; reuse быстрее допуска не исключён. На Windows целесообразна точная
  сверка, на Linux — стабильный `/proc` starttime для строгой идентичности. Воспроизведения PID reuse
  в этой сессии нет; это предел гарантии, не доказанный случай убийства чужого процесса.
- RemainingBuildHosts в observe проверяется **после первого поиска**: Program.cs сначала выполняет
  SearchMeasurement, затем WaitForExitAsync(10 s). Поэтому надпись «10 s after the load» не задаёт
  точный deadline от окончания load. При поиске дольше 10 с поздний самостоятельный выход host
  может остаться незамеченным. Для точного временного утверждения ждать выход параллельно поиску
  либо записывать deadline от load; текущие нулевые значения не доказывают этот строгий deadline.
- Половина первого недистурбированного времени может оказаться слишком большой для следующего
  быстрого запуска. Скрипт такой случай отмечает, поскольку DisturbedAtMs остаётся null, и возвращает
  failure. Она также не гарантирует попадание внутрь конкретной RPC-операции. Это ограничение
  случайного сценария, а не доказательство нагрузки на заданный участок загрузчика.
- Равный остаток host принимается как отсутствие отличия сторон по решению владельца; это допустимо
  для task-00 и не означает отсутствие утечки. Термин «upstream» в общем случае был бы слишком
  сильным, но конкретная ранняя отмена подтверждена исходниками и воспроизведена у original.
- Причина утечки проверена в [manager точной ревизии, строки 89–153](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs#L89):
  ConfigureGlobalState/FindBestMSBuild идут до регистрации в словаре менеджера. Patch
  [2624f5c156a7c2edeed0e0330b6528b32e37e6a5](https://github.com/dotnet/roslyn/commit/2624f5c156a7c2edeed0e0330b6528b32e37e6a5)
  прочитан через GitHub API: commit от **2026-08-04**, добавляет DisposeAsync в catch инициализации
  **клиента**. Он новее ревизии 5.9.0. На дату проверки официальный
  [NuGet versions API](https://api.nuget.org/v3-flatcontainer/microsoft.codeanalysis.workspaces.msbuild/index.json)
  вернул последнюю stable **5.9.0**. Task-00b корректно вынесен отдельно.
- Объяснение kill-недетерминированности соответствует удалению disconnected host из manager cache и
  последовательному запросу следующего проекта в
  [Worker.LoadAsync, строки 87–107](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.Worker.cs#L87).
  Эта возможность не оправдывает произвольные успешные данные/исключения, см. R2-02.

### Comparer: дополнительные выполненные контроли

Помимо восьми штатных self-checks исполнен настоящий compare на синтетических JSON:

- поле только у original: exit 1; проект только у original при стабильном составе: exit 1;
- JSON-serialized пустая строка (`\"\"` как содержимое поля) против serialized null (`null`): exit 1.
  Capture хранит сериализованное значение внутри строки; поэтому настоящие nullable значения
  observation не превращаются здесь в пустую строку. Raw JSON null нарушал бы этот формат;
- совпадение ключа проекта с текстом диагностики не смешало категории: новое значение Field
  осталось unexplained, exit 1;
- один measured round с равными данными: exit 0, natural inventory отсутствует; с разными: exit 1;
- unidentified=1: exit 1; оставшийся host и чужая папка покрыты штатными self-checks;
- natural-появление проекта и старый процессный формат дали ложную полноту доказательства,
  воспроизведения R2-04/R2-05.

Штатных восьми self-checks недостаточно для перечисленных отрицательных случаев. Минимальные полезные
добавления: пропущенные process fields, переменный project presence с новыми полями, missing field,
serialized empty/null, collision ключа с диагностикой и Runs=1. В текущих проверенных положительных
сводках все измеренные project keys стабильны.

### Часть B: что подтверждено

- PinnedSdk.GlobalJson побайтно соответствует прежним строковым константам. Razor/Oracle по-прежнему
  добавляют `\n`; остальные четыре изменённые записи остаются без него. Кодировки не менялись.
- Изменённый Windows inventory использует тот же набор BindingFlags, FactAttribute с inherit=true
  и те же method/type TraitAttribute constructor arguments. Перед GetTypes сохранён Register.
  Windows inventory и PinnedSdk inventory прошли в полном main-наборе.
- Остальные отмеченные методы действительно вызывают build, OpenFresh или Razor build с exact pin;
  в HydrateHost Fixture.CreateAsync сам по себе только создаёт файлы/Adhoc-данные. Поэтому не
  помечать все тесты этого класса было правильно. Ошибка с тремя theory-строками — R2-03.
- Unit-фильтры обеих ОС правильно исключают AnalyzerLifecycle и RequiresPinnedSdk, Linux дополнительно
  RequiresWindows. Lifecycle не использует exact pin 10.0.300; по чтению fixtures ему нужен обычный SDK.
  Локальный lifecycle с SDK 10.0.402 прошёл. Удаление обязательной установки 10.0.300 для него
  не противоречит коду; фактический runner без этой версии отдельно для lifecycle не моделировался.
- В pack job release global.json создаётся после checkout, до setup-dotnet; далее restore/publish
  выполняются из корня checkout. Setup читает этот файл, а disable заставляет SDK host выбрать
  именно 10.0.402 даже при наличии более нового SDK. Правило подтверждено
  [официальным описанием disable](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#rollforward).
  Локальная проверка этого global.json вернула 10.0.402. Linux runner и новая версия выше 10.0.402
  в этой сессии не запускались; вывод об их выборе — по workflow и контракту host.
- **AR-18 воспроизведён, не только прочитан.** В отдельном каталоге записан
  `{"sdk":{"version":"10.0.300","rollForward":"latestFeature"}}`.
  CLI вернул **10.0.402**; настоящая собранная
  `DotNetSdkEnvironment.TryGetPin` — `C:\Program Files\dotnet\sdk\10.0.300`;
  настоящая `SdkMismatchDiagnostics.CreateErrors(directory, "", "10.0.402")` вернула error
  **MCP_MSBUILD_SDK_MISMATCH** с текстом «dotnet --version reported 10.0.402 but global.json pins 10.0.300».
  Вызовы сделаны из PowerShell через загруженную Release-сборку сервера. Полный MCP build-запрос
  для этого примера не выполнялся.
- GlobalJsonSdkReader читает номер, игнорирует policy, сначала выбирает exact directory; его
  fallback-префикс для полного номера не находит обычную другую feature band. DotNetSdkEnvironment
  задаёт resolver/MSBuild overrides из этого результата; MsBuildBootstrapper использует тот же reader.
  Следовательно, основание AR-18 и откладывания permissive root pin подтверждено.
- `docs/code-style.md` прочитан полностью. Проверен только новый/изменённый C# указанных коммитов;
  обязательных нарушений в нём не обнаружено. Untouched-код и upstream под стиль репозитория не оценивались.

### Запуски и среда

Windows x64; .NET SDK 10.0.402; PowerShell 7.6.5 и Windows PowerShell 5.1.
Полный инвентарь установленных SDK: 9.0.314, 10.0.201, 10.0.300, 10.0.401, 10.0.402.

В исходном workspace были посторонние untracked C# и документы. Tracked-файлы исходно чистые.
Для итоговых solution build/main/lifecycle использован отдельный **git archive HEAD**, распакованный
в ignored `spikes/BuildHostFork/results/review-round2-head`; посторонние файлы туда не вошли.
Исходный spike и fork проверялись на tracked-исходниках текущего workspace. Измерения локального
решения не являются новым сравнительным performance-стендом: параллельно шли другие проверки.

1. Через **run_dotnet_build**, Release: spike — success; исходное решение — success; чистое HEAD-решение — success.
   Solution build выдал три предупреждения: CS8603 в NuGetFallbackAssemblyResolver:72, CS8601 в
   LifecycleTestHost/HostSession:1177, xUnit1031 в WpfTemporaryProjectCallbackTests:195.
2. `dotnet spikes/BuildHostFork/bin/Release/net10.0/BuildHostFork.dll check`: **8/8 passed**.
3. `pwsh -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1`: **exit 0,
   all checks passed**. Фикстура identical, 6 проектов / 5 одинаковых Failure; обычное решение 5 проектов;
   cancel canceled, kill connection failure; early cancel — **1/1 host left**.
4. Повтор той же команды через `powershell -NoProfile -ExecutionPolicy Bypass -File ...`:
   **exit 0, all checks passed**, повторная сборка после прежнего results успешна. Early cancel — снова 1/1.
5. `pwsh -NoProfile -File spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1 -Solution RoslynMcpServer.sln -Runs 3`:
   **exit 0, identical/equivalent**, warmup + 3 раунда обеих сторон, 5 проектов, natural 0,
   один hash поиска, процессы из своих папок, RemainingBuildHosts=0.
6. На чистом HEAD: `dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk"`:
   **1379 passed, 0 failed, 0 skipped**, 52 с. Включает **123 SourceStructure** теста; отдельно повторять его
   после отсутствовавших production-правок не требовалось. Windows имеет четыре Windows-only теста
   сверх записанных Linux 1375, так что разница чисел согласована.
7. На том же HEAD с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, фильтр `Category=AnalyzerLifecycle`:
   **13 passed, 0 failed, 0 skipped**, 1 мин 3 с.
8. Контроль theory без SDK 10.0.300: **9 ожидаемо failed / 3 passed**, описан в R2-03; это намеренно
   отрицательный эксперимент, не зелёный прогон всей категории.
9. Контроли настоящих PS-функций и comparer на синтетических данных, проверки hashes/deps и AR-18:
   результаты перечислены выше.

Первые попытки в sandbox не считаются pass: сценарии остановились на UnauthorizedAccessException
именованного канала original BuildHost; VSTest не подключился к testhost за 90 с и aborted.
Успешные полные прогоны выполнены вне sandbox. Начальная fork restore/build также имела NU1900
из-за недоступности NuGet vulnerability feed; ошибок компиляции не было. Последующие сценарные
сборки прошли. Пробный вызов PS 5.1 helper без process execution-policy override был запрещён;
повтор с `-ExecutionPolicy Bypass` прошёл, постоянная policy не менялась.

Локальные receipts лежат в ignored results, не являются новыми tracked deliverables:
`review-round2-head-tests/round2-head-main.trx`, `review-round2-head-tests/round2-head-lifecycle.trx`,
`review-round2-scenarios.log`, `review-round2-scenarios-ps51.log`, `review-round2-baseline.log`,
`review-round2-probes/*`, `review-round2-without-pin/without-pin-isolated-host.trx`.
Все эти пути относительны `spikes/BuildHostFork/results/`.

## 4. Не проверено

- Linux в этой сессии, исполнение x86/ARM64 и живые `/proc`-edge cases. Для них выполнено чтение кода/ABI,
  а не аппаратный прогон. PID reuse, процесс целиком между polls и цепочка с умершим родителем не
  воспроизводились; ограничения нельзя считать покрытыми обычным successful baseline.
- Собственные новые прогоны полного Roslyn, Smoke и частного офисного решения. Исторические raw JSON
  недоступны; опубликованные сводки и арифметика проверены, полнота/правдивость исходных данных
  независимо не переустанавливались. Второй офисный host по kind остаётся не установлен.
- GitHub Actions Linux/Windows release pack/smoke, runner с SDK новее 10.0.402 и реальная публикация
  релиза. Рассмотрены workflow и правила SDK host; никакие releases/tags/ветки не создавались.
- Новая версия Roslyn с исправлением утечки не устанавливалась. Проверен upstream patch, но не
  проведена приёмка task-00b и не оценена достижимость ранней отмены в production сервере.
- Полное повторное ревью 109 upstream-исходников и повторная проверка всех RPC-контрактов не проводились:
  это исключено scope раунда 2. Проверены hashes, изменённая сборка и относящиеся к находкам client paths.
- Статусы task-00/индекса не исправлялись: запрос прямо разрешает менять только отчёт. Существующий
  `in progress` сохранён; автоматическая приёмка или разрешение production activation не выдаётся.

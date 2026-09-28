# Эпоха 1. Результаты: исправление ложного падения sanitizer-теста

Статус: **выполнена** (2026-09-28). План: [epoch-1-sanitizer-test-fix.md](epoch-1-sanitizer-test-fix.md).
Следующий шаг: [эпоха 2](epoch-2-general-solution.md).

Целевое ложное падение устранено. Новых падений и пропусков нет: unit-набор 680 из 680,
`Skipped: 0`. Production-код не менялся.

Первая сдача была закрыта преждевременно: обязательная `this.`-квалификация требуемого вызова не
распознавалась, хотя отчёт утверждал обратное. Дефект найден на ревью, исправлен, покрытие
добавлено — [§9](#9-дефект-приёмки-найденный-на-ревью).

## 1. Что изменено

| Файл | Изменение |
|---|---|
| `RoslynMcpServer.Tests/SourceStructure/SourceTarget.cs` | новый тип: исходник + путь + namespace + имя типа как одна цель проверки |
| `RoslynMcpServer.Tests/SourceStructure/SourceMethodLocator.cs` | новый: разбор через `CSharpSyntaxTree`, поиск типа и метода, обход узлов непосредственного тела |
| `RoslynMcpServer.Tests/SourceStructure/SanitizedEntryChecks.cs` | новый: проверки контракта sanitized- и raw-входов, возвращают список нарушений |
| `RoslynMcpServer.Tests/SourceStructure/SanitizedEntryChecksTests.cs` | новый: 25 регрессионных примеров для самого кода проверок |
| `RoslynMcpServer.Tests/WorkspaceAnalyzerSanitizerTests.cs` | два срезовых теста переведены на проверки; добавлено чтение цели `ReadSolutionManagerTarget()` |

Имена обоих тестов сохранены, поэтому ссылки в отчётах и документации не устарели.

## 2. Baseline до изменения

Зафиксирован до первой правки кода; историческое число 649 из отчёта эпохи 7 не подтвердилось.

| Что | Значение |
|---|---|
| Commit | `2c53b10774d56ac7c60310fd26d2d5dfbfdefcec` (2026-09-28 01:16:08 +0300) |
| Локальные изменения | `M docs/README.md`, `?? docs/eval-proto/`, `?? docs/test-improvements/`, `?? scripts/` — код не менялся |
| SDK | `dotnet` 10.0.204, `net10.0` |
| Пакеты тестов | `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.0, `Microsoft.NET.Test.Sdk` 17.14.1 |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` 5.9.0; версия сервера 1.4.17 |
| Сборка | `dotnet build RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -v:minimal --no-incremental` — успех, 0 ошибок |
| Предупреждения сборки | `CS8603` ×2, `CS8601` ×1, `xUnit1030` ×7 — все прежние, ни одного из новых файлов |
| Целевой класс | `--filter "FullyQualifiedName~WorkspaceAnalyzerSanitizerTests"` → 8 всего, 7 passed, **1 failed** |
| Падение | `Assert.DoesNotContain() Failure: Sub-string found ↓ (pos 22670) … Found: ".Result"`, строка 99 |
| Unit-набор | `--filter "Category!=AnalyzerLifecycle"` → `Failed! - Failed: 1, Passed: 654, Skipped: 0, Total: 655, Duration: 49 s`, exit 1 |
| Единственность падения | проверена по логу: красный ровно `GetSanitizedPublishedSolution_takes_workspace_lock_and_async_does_not_call_sync` |

## 3. Как устроена проверка

`SanitizedEntryChecks.VerifySanitizedEntries` и `VerifyRawPublishedEntry` возвращают список нарушений;
пустой список — контракт выполнен, поэтому тест читается как `Assert.Empty(violations)`, а текст
падения сразу называет метод и нарушение, например:

```text
SolutionManager.GetSanitizedPublishedSolution: нет вызова _workspaceLock.Release() в finally защищённого try
SolutionManager.GetSanitizedPublishedSolutionAsync: ссылка на публичный синхронный вход GetSanitizedPublishedSolution в строке 381: повторный вход в нерекурсивный _workspaceLock — дедлок
```

Тело берётся из дерева (`MethodDeclarationSyntax.Body`), а не срезом текста. Обход
`SourceMethodLocator.DirectNodes` не заходит внутрь локальных функций и лямбд, поэтому вызов в
невызванной функции не удовлетворяет требование к непосредственному телу. Сравнение имён идёт по
полному идентификатору, поэтому `prepared.Results` не считается ссылкой на `Result`.

Приёмка структуры:

| Исходное утверждение | Новая проверка |
|---|---|
| async: `Contains("GetOrCreateSanitizedPublishedSolution()")` | в непосредственном теле async-входа есть self-вызов `GetOrCreateSanitizedPublishedSolution` |
| async: `DoesNotContain("return GetSanitizedPublishedSolution()")`, `DoesNotContain("GetSanitizedPublishedSolution();")` | в теле async-входа нет ссылки на идентификатор `GetSanitizedPublishedSolution` — ни вызова, ни `this.`-формы, ни method group |
| sync: `Contains("_workspaceLock.Wait(")` | есть вызов `Wait` именно у receiver `_workspaceLock` (допустима форма `this._workspaceLock`) |
| sync: `Contains("_workspaceLock.Release()")` | есть вызов `Release` у `_workspaceLock` **в `finally` защищённого `try`** |
| sync: `Contains("GetOrCreateSanitizedPublishedSolution()")` | helper вызывается **внутри защищённого `try`** |
| sync: `DoesNotContain(".Result")` | нет ссылки на член с точным именем `Result` |
| sync: `DoesNotContain("GetAwaiter()")` | нет ссылки на член с точным именем `GetAwaiter` |
| raw: `Contains("return _solution;")` | в теле есть `return _solution;` или `return this._solution;` |
| raw: `DoesNotContain("GetOrCreateSanitizedPublishedSolution")` | нет ссылки на `GetOrCreateSanitizedPublishedSolution` |
| raw: `DoesNotContain("RemoveUnresolvedAnalyzers")` | нет ссылки на `RemoveUnresolvedAnalyzers` |
| raw: `DoesNotContain("SetPublishedSolution")` | нет ссылки на `SetPublishedSolution` |

Добавлено к прежнему контракту (не ослабление, а расширение): положение захвата перед защищённым
`try`, обращение к снимку внутри него и освобождение в его `finally`; исключение содержимого
локальных функций и лямбд; диагностика вместо пустого успеха при ошибке разбора, отсутствии типа,
отсутствии метода и перегрузке.

Отдельные тесты на каждое утверждение не понадобились: проверки возвращают список нарушений, и
каждое из них называет исходный метод (`SolutionManager.GetSanitizedPublishedSolution: …`), поэтому
`Assert.Empty` печатает точный адрес отказа. Имена обоих тестов сохранены, чтобы не ломать ссылки
на них в отчётах и документации.

## 4. Приёмка: корпус мутаций в памяти

`SanitizedEntryChecksTests` — 25 фактов на демонстрационном исходнике; проверки вызываются те же, что
и для production-файла. Примеры, объявленные исполняемыми мутациями, компилируются, и это
проверяется в самом тесте (`CompilationErrors`), поэтому отказ проверки на некомпилируемом
исходнике не выдаётся за обнаружение.

| Пример | Ожидание | Факт |
|---|---|---|
| Базовый исходник | компилируется, контракт выполнен | passed |
| Перестановка методов (raw после helper) | успех | passed |
| Чужой метод с `prepared.Results` + комментарии и строки с запрещёнными именами | успех | passed |
| `this.`-квалификация self-вызова и лока во всех входах | успех | passed (после исправления §9) |
| `return this._solution;` в raw-входе | успех | passed |
| Прямой вызов публичного sync-входа из async-входа | отказ | passed (нарушение названо) |
| То же в форме `this.GetSanitizedPublishedSolution()` | отказ | passed |
| Удалён `_workspaceLock.Wait()` | отказ | passed |
| Удалён `_workspaceLock.Release()` | отказ | passed |
| Receiver заменён на другой лок того же типа | отказ | passed |
| `Release` вынесен из `finally` | отказ | passed |
| Снимок прочитан до захвата лока | отказ | passed |
| `Task.FromResult(0).Result` в sync-входе | отказ | passed |
| `GetAwaiter().GetResult()` в sync-входе | отказ | passed |
| Вызов helper только внутри локальной функции | отказ | passed |
| Вызов helper только внутри лямбды | отказ | passed |
| raw-вход возвращает sanitized-снимок | отказ | passed |
| Целевой метод переименован | диагностика | passed |
| Добавлена перегрузка целевого метода | диагностика | passed |
| `partial`-тип (два объявления `DemoManager`) | диагностика | passed |
| Метод без тела (`abstract`) | диагностика | passed |
| Источник обрезан (синтаксическая ошибка) | диагностика | passed |
| Тип ищется в чужом namespace | диагностика | passed |
| `_workspaceLock.WaitAsync().Result` | **синтаксический пример**: не компилируется, поэтому исполняемой мутацией не считается; проверяется только обнаружение `.Result` | passed (некомпилируемость зафиксирована утверждением) |

## 5. Приёмка: мутации настоящего production-файла

Отдельным временным пробником проверено, что проверки срабатывают на реальной форме
`Services/SolutionManager.cs`, а не только на демонстрационном исходнике. Мутации применялись к
**копии текста в памяти**, файл на диске не изменялся; пробник после прогона удалён.

| Мутация реального исходника | Ожидание | Факт |
|---|---|---|
| Без мутаций | нарушений нет | passed |
| В async-входе `return GetSanitizedPublishedSolution();` | отказ (дедлок) | passed |
| В sync-входе удалён `_workspaceLock.Wait()` | отказ | passed |
| В sync-входе `Task.FromResult(0).Result` | отказ | passed |
| `this.`-квалификация входов (`this.Helper()`, `this._workspaceLock.*`, `return this._solution;`) | нарушений нет | passed (после исправления §9) |
| В async-входе `return this.GetSanitizedPublishedSolution();` | отказ | passed |

Постоянного теста из пробника не сделано: он дублировал бы демо-корпус, а его якоря зависели бы от
форматирования production-файла. Защита самого инварианта обеспечивается целевым тестом,
невакуумность — демо-корпусом.

## 6. Прогоны после изменения

```powershell
dotnet build RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -v:minimal --no-incremental
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~WorkspaceAnalyzerSanitizerTests"
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~SanitizedEntryChecksTests"
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj --no-build --no-restore --filter "Category!=AnalyzerLifecycle"
```

| Прогон | Результат |
|---|---|
| Сборка | успех, 0 ошибок, список предупреждений совпадает с baseline |
| `WorkspaceAnalyzerSanitizerTests` | 8 всего, **8 passed**, 0 failed (было 7 passed / 1 failed) |
| `SanitizedEntryChecksTests` | 25 всего, **25 passed**, 0 failed |
| Unit-набор | `Passed! - Failed: 0, Passed: 680, Skipped: 0, Total: 680`, exit 0 (baseline: 655 / 654 / 1 / 0) |

Посторонних сбоев в этих прогонах нет: прирост набора — ровно 25 новых тестов, skips не появились.

### Посторонний сбой в полном прогоне

В одном из промежуточных полных прогонов (тот же бинарник, что и в четырёх зелёных) упал
`DiagnosticReportStoreTests.Over_cap_report_is_prefix_plus_explicit_marker` — тест, не связанный
с эпохой 1.

- Тест в одиночном прогоне зелёный: 3 прогона по фильтру класса — 3 passed.
- Механика: `DiagnosticReportStore` — процессный `static` с `MaxEntries = 4` и FIFO-вытеснением,
  а пишут в него production-пути `BuildTools`, `RunTools`, `TestTools` (сохранение усечённого
  отчёта). Классы xUnit выполняются параллельно, поэтому параллельная запись может вытеснить
  запись этого теста между итерациями чтения большого отчёта, и `TryTakeChunk` вернёт
  «unknown or expired» на строке 97.
- Точный источник вытеснения в том прогоне не установлен; вытеснение объясняется общей
  статической ёмкостью на 4 записи, а не изменениями эпохи 1. Файлы эпохи 1 стор не трогают
  и в тестах его не вызывают.
- Сбой отнесён к посторонним и не выдаётся за успех: в отчёте он зафиксирован, отдельного
  исправления в объём эпохи 1 не входит.

## 7. Границы проверки

Честные ограничения эпохи 1; часть из них — предмет эпохи 2.

- **Имена, а не символы.** Receiver сверяется по тексту `_workspaceLock`; тип `SemaphoreSlim` не
  проверяется. Пользовательское свойство или метод с точным именем `Result` в теле дадут ложное
  срабатывание, а поле с другим именем и тем же локом — нет.
- **Нет транзитивности.** Проверяется непосредственное тело. Цепочка `async → приватная обёртка →
  sync` не обнаруживается, хотя дедлок через неё возможен. Делегаты, `virtual` и рефлексия вне модели.
- **Локальные функции и лямбды не анализируются вовсе** — ни вызванные, ни невызванные. Это
  гарантирует, что невызванная функция не удовлетворит требование, но и вызов внутри вызванной
  функции не будет засчитан.
- **Диагностика вместо разбора формы.** Две декларации `partial`-типа, перегрузка целевого метода и
  метод без тела (`abstract`/`extern`) приводят к нарушению-диагностике, а не к молчаливому выбору
  одного объявления. `expression body` даёт scope, поэтому такой метод проверяется обычными
  утверждениями (например, отсутствие захвата лока будет нарушением).
- **Квалификация именем типа** (`SolutionManager.GetOrCreateSanitizedPublishedSolution()`) не
  считается self-вызовом. Форма `this.` поддержана и покрыта положительными примерами; до
  исправления §9 она давала ложное нарушение.
- **Проверяется рабочая копия**, а не собранная сборка; неактивные ветки `#if` не разбираются.
- **Поведение не проверяется.** Отсутствие дедлока рантайм-проверкой не подтверждается: это задача
  эпохи 2 с изоляцией в отдельном процессе.

## 8. Что эпоха 1 намеренно не сделала

- Не меняла `Services/SolutionManager.cs` и порядок его членов.
- Не вводила общий помощник-граф вызовов и не переносила остальные текстовые проверки (§1.5 README).
- Не добавляла рантайм-страж дедлока и не переключала раннер тестов.
- Не трогала `WorkspaceWriteBoundaryTests` и `Epoch1SemanticInventoryTests` — они в области эпохи 3.

## 9. Дефект приёмки, найденный на ревью

Первая сдача эпохи была закрыта преждевременно: обязательная форма `this.Method()` для требуемого
вызова не распознавалась, хотя §3 и §7 утверждали обратное.

**Причина.** `ReceiverText` снимал у receiver префикс `this.`, но у `this.Helper()` узлом receiver
является `ThisExpression` с текстом `this` — без точки. Префикс не снимался, `ReceiverText`
возвращал `"this"`, `IsSelfCall` отвечал «вызов не свой», и оба обязательных вызова хелпера давали
ложное нарушение: в async-входе и внутри защищённого `try` синхронного входа. Формы
`this._workspaceLock.Wait()` и `return this._solution;` работали: там receiver —
`MemberAccessExpressionSyntax` (`this._workspaceLock`), и префикс снимался корректно.

**Почему тесты не поймали.** Из 21 факта форму `this.` покрывал только запрет
(`This_qualified_public_sync_call_from_async_entry_fails`): он ищет любое имя и от receiver не
зависит. Положительной пары «эквивалентная форма сохраняет успех» для `this.`-self-вызова не было,
поэтому дыра не проявлялась.

**Воспроизведение.** Добавленный положительный пример `This_qualified_self_call_and_lock_keep_success`
упал на дефектном коде с двумя сообщениями:

```text
DemoManager.GetSanitizedPublishedSolutionAsync: нет непосредственного вызова GetOrCreateSanitizedPublishedSolution() в теле метода
DemoManager.GetSanitizedPublishedSolution: обращение к снимку (GetOrCreateSanitizedPublishedSolution()) должно быть внутри защищённого try
```

**Исправление.** `TrimThis` заменён на `NormalizeReceiver`: одиночный `this` нормализуется в `null`
наравне с префиксом `this.`, поэтому для проверки `Helper()` и `this.Helper()` неразличимы.

**Добавленное покрытие.** Корпус 21 → 25 фактов: положительные пары для `this.`-квалификации
self-вызова и лока во всех трёх входах и для `return this._solution;`, плюс диагностики на
`partial`-тип с двумя декларациями и на метод без тела (`abstract`).

**Проверка на настоящем файле.** Временный пробник применял `this.`-квалификацию к копии текста
`Services/SolutionManager.cs` в памяти: `VerifySanitizedEntries` и `VerifyRawPublishedEntry` — без
нарушений, а `return this.GetSanitizedPublishedSolution();` в том же файле по-прежнему
обнаруживается. Файл на диске не изменялся, пробник после прогона удалён.

**Прогоны после исправления:** `SanitizedEntryChecksTests` — 25 passed, `WorkspaceAnalyzerSanitizerTests` —
8 passed, unit-набор — 680 passed / 0 failed / 0 skipped.

**Вывод для серии.** Запрет не доказывает поддержку эквивалентной формы. На каждую форму, которую
проверка объявляет поддержанной, нужна положительная пара «форма сохранена — успех» рядом с
отрицательной; это же требование эпоха 2 ставит для корпуса прототипа. До этого исправления отчёт
утверждал поддержку `this.` без такого примера: утверждение и покрытие должны появляться вместе.

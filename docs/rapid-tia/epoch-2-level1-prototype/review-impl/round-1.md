# Ревью реализации прототипа task-01, раунд 1

- Дата: **2026-11-09**.
- Рецензент: `claude-opus-5-5`, сессия с владельцем.
- Проверенная версия: **`3616de9`** (`spikes/AffectedTestsProbe/`).
- Scope: код прототипа против [плана task-01](../task-01-forward-reverse-probe-hi.md). Замеры не делались.
- Итог: **changes required**. 2 Blocker, 2 High, 3 Low. Исправляет Grok; повторная проверка — по
  [R-18](../../README.md#решения-владельца-2026-10-29), не больше трёх раундов.

## Что проверено

- `dotnet test spikes/AffectedTestsProbe/AffectedTestsProbe.Tests/AffectedTestsProbe.Tests.csproj -c Release` —
  12/12 PASS, Linux, SDK 10.0.114.
- Сквозной прогон `oracle` на маленьком solution вне репозитория:
  - `Prod/Calc.cs`: `public class Calc { public int Add(int a, int b) { return a + b; } }`;
  - xUnit v2 `Tests/CalcTests.cs`: `Adds` (`[Fact]`, вызывает `Add`), `Rows` (`[Theory]`, две `InlineData`, вызывает
    `Add`), `Unrelated` (`[Fact]`, не вызывает);
  - команда: `oracle --solution E2E.slnx --member "M:Prod.Calc.Add(System.Int32,System.Int32)" --test-project Tests --clone-ack`.

  Прямой граф и обратный поиск верны: F-CHA = F-RTA = R-CHA = {`Adds`, `Rows`}, L0 = все три. Эталон не работает:
  `controlExecuted: 4`, `unmappedExecuted: 4`, `oracle: []`, `invalid: "build failed"`, при этом `judgment: "Valid"`.
  Ожидалось: O = {`Adds`, `Rows`}, пара годна.

## Замечания

### I-01, Blocker — текст мутации не компилируется

`Oracle/MemberMutator.cs:126-142`. `ThrowStatement()`, `ConditionalThrow()` и `ExceptionCreation()` собраны через
`SyntaxFactory` без trivia. `ToFullString()` даёт:

```
int Add(int a){ thrownewSystem.InvalidOperationException("tia-probe");return a; }
true?thrownewSystem.InvalidOperationException("tia-probe"):(1)
```

Синтаксически это вызов метода `thrownewSystem.InvalidOperationException(...)`, сборка падает с CS0103. Каждая пара —
`build failed`, эталона нет ни для одного члена.

Тесты этого не ловят: `MemberMutatorTests.AssertNoErrors` (`AffectedTestsProbe.Tests/MemberMutatorTests.cs:88`)
компилирует объект `mutation.Root.SyntaxTree`, а в файл пишется `mutation.Root.ToFullString()`.

Нужно:

- строить вставку разбором текста (`SyntaxFactory.ParseStatement`/`ParseExpression`) или нормализовать только
  вставленные узлы (`NormalizeWhitespace()` на них, не на всём файле — иначе diff файла раздувается);
- в тестах мутатора компилировать **записанный текст**: `CSharpSyntaxTree.ParseText(mutation.Root.ToFullString())`;
- покрыть тестами все формы: тело метода, конструктор с `: base(...)`, accessor, expression-bodied метод и свойство,
  инициализатор поля и автосвойства.

### I-02, Blocker — discovery не читается из прогона, все cases неотображённые

`Oracle/DiscoveryLog.cs:9-24`. Код ищет в `--diag` сообщение `TestDiscovery.Completed`. Обычный `dotnet test` (не
`--list-tests`) его не пишет. Проверено на `AffectedTestsProbe.Tests` с `--diag`: в основном и host-логе есть только
`TestExecution.Initialize`, `StartWithSources`, `StatsChange`, `Completed`, `TestSession.Message`, `ProtocolVersion`.
`Read` возвращает пустой список, `RunJoin` не находит ни одного `testId`, все исполненные cases — неотображённые.
Порог 5% это ловит (`publishCompleteness: false`), но эталона нет. В task-06 этот путь работал, потому что discovery
был отдельным запуском.

Второй дефект того же места: `LastDiscoveredTests` — только последняя пачка discovery. На больших проектах предыдущие
пачки приходят в `TestDiscovery.TestFound`, и даже при отдельном discovery часть cases пропадёт.

Нужно выбрать один источник «`testId` → FQN» внутри того же прогона и проверить его на живом прогоне:

- `TestExecution.StatsChange` → `NewTestResults[].TestCase` (`Id`, `FullyQualifiedName`), все сообщения, не одно; или
- TRX: `TestDefinitions/UnitTest[@id]/TestMethod` (`className`, `name`) — проверить формы NUnit с аргументами,
  вложенные классы и наследников по [task-06](../../epoch-1-project-level/evidence/task-06-adapter-names.md).

Приёмочная проверка — сквозной тест прототипа на маленьком solution вроде описанного выше: реальный `dotnet test`,
O = {`Adds`, `Rows`}, `Unrelated` вне O, пара годна, `unmappedExecuted: 0`. Без такого теста I-01 и I-02 не были бы
видны до первого прогона на Roslyn.

### I-03, High — предсказание строится по одному TFM, эталон — по всем

`Commands/OracleCommand.cs:199-215` (`LocateAsync`) и `Commands/ReverseCommand.cs:110-128` (`FindAsync`) берут первый
проект, где `GetFirstSymbolForDeclarationId` нашёл символ, и один `MemberNode`. F-CHA, F-RTA и R-CHA считаются только
от этого узла (`OracleCommand.cs:67-71`). `LevelZero.Split` и эталон при этом берут все проекты, куда входит файл.
Кроме того, символ ищется во всех проектах, включая потребителей: найденный узел — тот TFM, на который ссылается первый
по порядку проект, а не обязательно тот, против которого собраны тесты scope.

Сценарий: production-проект `netstandard2.0;net10.0`, тесты scope — `net10.0`. Найден узел `netstandard2.0`, до него
тесты не доходят, F и R пусты, эталон непуст. Полнота ложно низкая при верных рёбрах. Это расходится с планом
(«Ключи проектов», F-002): предсказание — объединение по всем ключам члена в scope.

Нужно: собрать все узлы члена — все `ProjectKey`, в компиляции которых есть файл с телом, — и объединять F-\* и R-CHA
по ним; ключи вне scope — в `outsideScope`. То же в `reverse` и `reverse-batch`. Тест: член в multi-TFM проекте, тест
только против второго TFM, предсказание непусто.

### I-04, High — multi-TFM тестовый проект: все cases неотображённые, TRX перезаписывается

`Commands/OracleCommand.cs:86`: `units` фильтруются по пути проекта без TFM. У multi-TFM тестового проекта каждый
тестовый метод есть дважды (по ключу на TFM), `FqnMapper.Map` (`Oracle/FqnMapper.cs:20`) видит два совпадения и
возвращает `null`. Все cases такого проекта неотображённые.

`Commands/DotNetTest.cs:25-28`: `dotnet test` на multi-TFM проекте гоняет все TFM с одним `--diag` и одним
`LogFileName`, результаты TFM перетирают друг друга.

Нужно: запускать каждый TFM отдельно (`-f <tfm>`, свои `diag` и `trx`) и отображать cases только на единицы этого
проекта и TFM. Набор TFM у контроля и мутации — один и тот же (план, «Годность пары прогонов»).

### I-05, Low — противоречивый результат

`Commands/OracleCommand.cs:145`: `judgment` берётся из `PairValidity` и при `invalid: "build failed"` печатает `Valid`.
`PairValidity` в этом случае считался по пустым спискам. Не вычислять `judgment` и не печатать `Valid`, если пара уже
негодна по сборке, таймауту или артефактам.

### I-06, Low — мутационный прогон: ноль выполненных и падение testhost

`Commands/OracleCommand.cs:186`: «выполнено больше нуля» проверяется только у контроля. Падение testhost в мутации
отдельной причиной не помечается и уходит в `missing results`. План требует обе проверки для каждого процесса и
причину `crash`. Таймаут считается на весь прогон (`OracleCommand.cs:110`), а план задаёт 3× контроля **этого
проекта**, но не меньше 5 мин.

### I-07, Low — учёт

В шапке task-01 «Модель, реализовавшая задачу: —», хотя прототип записан. Указать модель и дату, сюда же —
ссылку на это ревью.

## Что не проверялось

- Каскад `SymbolFinder.FindReferencesAsync` по иерархии (включает ли он ссылки на соседние реализации интерфейса) —
  для согласованности F-CHA ↔ R-CHA стоит проверить в precheck, но дефектом здесь не названо.
- Прогоны на Roslyn, NUnit/MSTest, Windows.

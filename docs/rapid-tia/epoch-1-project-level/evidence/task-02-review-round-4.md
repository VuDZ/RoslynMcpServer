# task-02 — ревью приёмки, раунд 4

- Дата: 2026-11-03.
- Рецензент: Claude Opus 5.5 (`claude-opus-5-5`), надзор приёмки по поручению владельца.
- Версия: `9362f1c` (исправления R2-001…R2-004, [ACCEPT раунда 3](task-02-review-round-3.md)).
- Scope: builder, detector, модели, `BaselineNarrowingSafetyTests`, контракт task-02, R-17
  программы, потребители признаков — task-05 и план запуска task-07.
- Вердикт: **MUST-FIX** — R4-001, R4-002.

## Что подтверждено

- R2-001…R2-004 закрыты: read-only probe рецензента повторил все строки таблиц
  [раунда 2](task-02-review-round-2.md) на версии `9362f1c`; ложных признаков в них нет.
- Запрет сужения сохраняется для наследника вида `D(int x) : B(x)`, `global::B`, для
  record и для наследника, удалённого после загрузки.
- TDD: [RED](task-02-review-round-2-red.md) 133 выполнено, 37 ожидаемых падений;
  [GREEN](task-02-review-round-2-green.md) 133/133.
- Validation координатора (Windows, Release): build, 307/307, main suite 1616/1616.
  Независимый прогон рецензента на Linux (.NET SDK 10.0.114, Release):
  `FullyQualifiedName~BaselineDocumentSnapshotBuilderTests|FullyQualifiedName~BaselineDocumentChangeDetectorTests|FullyQualifiedName~BaselineNarrowingSafetyTests|FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure` —
  **307/307**.

## Почему это дефекты

Условие исключения в R-17: изменённый член исполняют лишь тесты этого класса. В обоих
случаях ниже его исполняют и другие тесты, а признак `IsOwnTestClassRoot` гасит проектное
расширение: тесты теряются молча. Результаты — из read-only probe рецензента (исходники
builder, detector и моделей версии `9362f1c` в отдельном file-based приложении,
Microsoft.CodeAnalysis.CSharp 5.9.0).

## R4-001 — новый корень в классе, не бывшем тестовым при загрузке (MUST-FIX)

`BaselineDocumentChangeDetector.AddMemberChanges`: условие `baseline == null ||` считает
корнем любой добавленный член, а роль типа берётся только из текущего снимка.

| До | После | Результат | Что теряется |
|---|---|---|---|
| `class H { public int M() => 1; }` | `class H { public H() { Work(); } public int M() => 1; [Fact] public void T() {} }` | `.ctor`: `root=True` | тесты других классов, вызывающих `new H()` |
| то же | `class H { int v = Work(); public int M() => v; [Fact] public void T() {} }` | `v`: `root=True` | то же |
| то же | `class H { [SetUp] public void S() { Work(); } public int M() => 1; [Fact] public void T() {} }` | `S`: `root=True` | вызывающие `S` |
| `class X {}` | `class X {} [Collection] partial class C { C() {} [Fact] void T() {} }` | запись типа `C` и `.ctor`: `root=True` | другие части `C` могут лежать в другом файле, и `C` может быть вспомогательным классом |

Формулировка [R2-003](task-02-review-round-2.md#r2-003--роль-берётся-только-из-текущего-снимка-исправить-в-этом-раунде)
покрывала только изменённые члены; это пробел рецензента, а не отступление исполнителя.

Требуется:

- добавленный член получает `IsOwnTestClassRoot`, только если его тип в baseline этого
  документа уже был тестовым либо типа в baseline не было и он объявлен без `partial`;
- добавленная запись типа с `partial` — обычный член: другие части сравнение не видит;
- добавленный тестовый метод сохраняет `IsTestMethod` и запрета сужения от этого правила
  не получает: новый метод никто не вызывал.

## R4-002 — вложенные типы видят частные члены внешнего (MUST-FIX)

`BaselineDocumentSnapshotBuilder.TestTypeFactsByPath`: вложенный тип не учитывается.
Частные члены внешнего типа доступны вложенным, а его тесты не входят в «тесты класса»:
свёртка [программы](../../README.md) `FullyQualifiedName~Namespace.Class.` не совпадает с
`Namespace.Class+Inner.U`.

| Документ | Правка | Результат | Что теряется |
|---|---|---|---|
| `[TestFixture] class O { static readonly int V = 1; [TestFixture] class I { [Test] public void U() => Use(V); } }` | значение `V` | `V`: `root=True` | `U`; у `O` своих тестов нет, поэтому не запускается ничего |
| `class O { static readonly int V = 1; [Fact] void T() {} class I { [Fact] void U() => Use(V); } }` | значение `V` | `V`: `root=True` | `U` |
| `class O { O() { Work(1); } [Fact] void T() {} class I { [Fact] void U() => new O(); } }` | тело `O()` | `.ctor`: `root=True` | `U` |

R-17 такой случай не перечисляла; владелец дополнил решение: тип с вложенными типами не
сужает ответ, как абстрактный ([R-17](../../README.md#решения-владельца-2026-10-29)).

Требуется:

- тип, у которого в этом документе на любой части есть вложенный `class`, `struct`,
  `record` или `interface`, запрещает сужение так же, как `abstract`: члены не получают
  `IsOwnTestClassRoot`, запись нового такого типа — тоже, тестовый метод сохраняет
  `IsTestMethod` и несёт `BlocksNarrowing`;
- вложенный `enum` и `delegate` запрета не дают: кода, читающего частные члены, в них нет;
- собственные роли самого вложенного типа не меняются.

## Регрессии

Все случаи R4-001 и R4-002 пишутся первыми и красные до правки production-кода.
Guard: `Added_instance_roots_in_an_existing_eligible_type_can_narrow` и новый тестовый
класс без `partial` остаются корнями; вложенный тестовый класс сохраняет свои корни;
внешний класс только с вложенным `enum` или `delegate` сохраняет корни;
`class Outer { Outer() { … } class Nested { [Fact] … } }` остаётся без корня, как раньше.
Существующие ожидания эти правки не отменяют.

## Известные ограничения

Записаны в [программу](../../README.md), дефектами не считаются: без графа они не
проверяются, а узкого синтаксического признака для них нет.

- `private static` данные могут читаться другими классами через нечастный член того же
  класса, например `MemberData` из другого класса;
- вложенный тип, объявленный только в `partial`-части из другого документа, не виден;
- наследник через alias не распознаётся (записано в R-17 раньше).

## Изменения плана

По решению владельца рецензент внёс в этот же коммит: дополнение R-17, правила «Без
графа» и каталога в [программе](../../README.md), требования и проверки
[task-02](../task-02-change-detection-med.md), уточнение и проверки
[task-05](../task-05-response-composition-med.md).

## Передача

R4-001 и R4-002 передаются исполнителю task-02. До подтверждения в следующем раунде
task-02 не считается принятой, и task-05 на неё не опирается. Task-01 менять не
требуется. R2-005/R2-006 остаются в эпохе 3. Рецензент не менял C#-файлы и не повторял
main suite.

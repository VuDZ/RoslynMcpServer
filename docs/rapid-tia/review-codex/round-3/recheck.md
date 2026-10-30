# Полный третий раунд Codex

Дата: **2026-10-30**. Проверенная версия: **185d3e1bcaa1c876a5f5ce3964ee218ced542da0**.

Повторно прочитан весь нормативный план, а не только diff или ответы на F-005/F-006. Ответы автора и решения владельца использованы как явно разрешённый контекст. Чужие review-grok* не открывались.

## Статусы семи первоначальных замечаний

| ID | Статус после раунда 3 | Основание в проверенной версии README |
|----|----------------------|--------------------------------------|
| [F-001](../findings/F-001.md) | Закрыто; подтверждение раунда 2 сохраняется | 91–104: unknown baseline при ранней записи и незавершённом снимке; 279–300: граница снимка, поколение публикации и принятое R-15 ослабление reset. |
| [F-002](../findings/F-002.md) | Закрыто; подтверждение раунда 2 сохраняется | 114–116 и 134–145: новый файл до reload обрабатывается проектно, текущие test roots включаются до pruning. |
| [F-003](../findings/F-003.md) | Закрыто; подтверждение раунда 2 сохраняется | 67–85, 234–235 и 324–330: отдельный контекст файла, using/global using expansion и инвалидация сохранённых фактов. |
| [F-004](../findings/F-004.md) | Закрыто; подтверждение раунда 2 сохраняется | 70–82 и 248–259: arity, partial, неоднозначность синтаксического ключа, стабильный project owner и отдельные memberships. |
| [F-005](../round-2/findings/F-005.md) | Исправлено в раунде 3; закрыто | 226–230: добавление нового extension всегда даёт проект и зависимые тестовые проекты, независимо от владельца старой цели. Трёхпроектный fixture закреплён в 588–591. |
| [F-006](../round-2/findings/F-006.md) | Исправлено в раунде 3; закрыто | 346–350: equality проектов явно добавлено к transitive dependencies; 376–378 переиспользуют это правило после edits. Оба размещения фабрики/реализации перечислены в 593–596. |
| [F-007](../findings/F-007.md) | Закрыто; подтверждение раунда 2 сохраняется | 522–527: contains-superset явно разрешён; 600–603 и 614–619 задают согласованную проверку фильтра. |

Всего по собственному первоначальному scope подтверждено **7 исправлений**: 5 в раунде 2 и ещё 2 в раунде 3. Это число не складывается автоматически со статистикой другого рецензента: дедупликация чужих IDs здесь не выполнялась.

Новые F-008/F-009/F-010 перечислены в [result.md](result.md). F-008 относится к отдельному правилу добавления обычного метода, а не к уже исправленному правилу нового extension из F-005.

## Семантическая проба F-008

Среда: PowerShell 7.6.5; локальные Microsoft.CodeAnalysis.Common и CSharp **5.9.0**, lib/net10.0; отдельный collectible AssemblyLoadContext.

Созданы компиляции с OutputKind.DynamicallyLinkedLibrary:

- App.cs: namespace Shared, partial Widget с явным конструктором.
- Widget.More.cs: та же partial Widget с Existing; во второй конфигурации в этот файл добавляется обычный instance Render.
- Tests ссылается на App через CompilationReference. В Tests объявлен ConsumerExtensions.Render(this Widget). Неизменный Check вызывает new Widget().Render(); CheckExisting отдельно вызывает Widget.Existing().
- До/после изменения App действительно содержит два syntax trees. GetDiagnostics всех компиляций обеих конфигураций дал 0 ошибок.
- Через SemanticModel.GetOperation прочитаны TargetMethod и ContainingAssembly одного и того же Render invocation. Проверки ожидаемых владельцев Tests/App прошли.

Вывод:

    App source trees: App.cs, Widget.More.cs
    before: Tests / M:ConsumerExtensions.Render(Shared.Widget)
    Compilation errors: 0
    App source trees: App.cs, Widget.More.cs
    after: App / M:Shared.Widget.Render
    Compilation errors: 0

Это API-проверка реального связывания. Будущий TIA не запускался; его пропуск следует из ограничения visibility в README:224–225 и из размещения baseline-узлов по разным файлам.

## Проверка F-009 по существующему runner

Проверена последовательность в текущем source:

1. RunTestByFilter передаёт FilterTestOmitSkipsRebuild == true.
2. TestBuildRoute.Resolve на обычном project route при noBuild == null выбирает этот default.
3. DotNetTestArguments.BuildPlan при NoBuild == true не создаёт pre-test build и запускает test с --no-build.
4. Существующий BuildPlan_noBuild_true_skips_pre_test_build прямо закрепляет это поведение.

Новый run ещё не написан. Поэтому вывод ограничен **отсутствующим выбором build policy** и риском наследования существующего default. Он не выдаётся за runtime-failure уже shipped-инструмента.

## Проверка F-010 по MCP и закреплённому SDK

Использованы первичные [правила cancellation MCP](https://modelcontextprotocol.io/specification/2025-11-25/basic/utilities/cancellation) и [McpSessionHandler SDK v2.2.0](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/v2.2.0/src/ModelContextProtocol.Core/McpSessionHandler.cs).

Протокол не гарантирует доставку позднего ответа. В SDK обработчик ответа использует request token; отправка проверяет его отмену. Клиент прекращает ожидание и удаляет pending request. Возврат строки tool handler не восстанавливает этот канал. Runtime-проба MCP-клиента в этом раунде не выполнялась.

## Принятые границы и решения

- R-01/R-02/R-10: приблизительность, metadata leaves и отсутствие специального блока SourceStructure не предъявляются как дефекты.
- R-13: один MCP-вызов, MTP process-per-class и пять явных условий whole-project приняты как решения владельца; пороги не заменялись предпочтениями reviewer.
- Резерв C остаётся выключенным до отдельной пробы флагов xUnit и изменения соседнего runner-контракта.
- R-14: консервативное расширение допустимо; точечность не требуется ценой пропусков.
- R-15: временное удержание отменяемого Solution прямо разрешено, при запрете публикации старого поколения.
- O-03/O-06/O-07/O-08 остаются на предусмотренной стадии измерений/выбора; неподтверждённые performance-оценки не выданы за измеренный результат.

Закрытие замечаний означает достаточное исправление specification. Оно не означает runtime-приёмку фичи, завершение эпох или разрешение production activation.

# Полный четвёртый раунд Codex

Дата: **2026-10-30**. Проверенная версия: **29d76fea27fc77a5c2d90261f2723dafedda63c8**.

Прочитан весь README, 764 строки, и ответы автора на собственный раунд 3. Чужие review-grok* не открывались; их статусы не использовались для принятия решений.

## Перепроверка F-008–F-010

| ID | Итог | Подтверждение в текущем README |
|----|------|-------------------------------|
| [F-008](../round-3/findings/F-008.md) | Исправлено; закрыто в scope specification | 231–237: помечаются все одноимённые source extensions solution, включая потребителя; metadata extension leaf даёт проектное расширение. Случай с Tests и другим partial-файлом закреплён в 634–635. |
| [F-009](../round-3/findings/F-009.md) | Исправлено; закрыто в scope specification | 445–460: первый запуск со сборкой, последующие с noBuild; исторический default не наследуется; whole-project идёт без фильтра, build failure блокирует тесты этого проекта. Случай старых binaries предусмотрен в 667–668. |
| [F-010](../round-3/findings/F-010.md) | Исправлено; закрыто в scope specification | 513–524 разделяют process timeout и MCP cancellation; итог отмены сохраняется в логе, доставка ответа best effort. Интеграция 668–670 требует MCP client и не требует доставки отменённого ответа. |

Ранее закрытые F-001…F-007 повторно сопоставлены с полным планом; подтверждения сохраняются. Всего закрыто **10 собственных замечаний**: 5 в раунде 2, 2 в раунде 3 и 3 в раунде 4. Эти числа не являются результатом сведения чужих каталогов.

Новое замечание [F-011](findings/F-011.md) относится к изменению существующего assembly-контекста. Оно не предъявляет повторно уже исправленные правила нового instance/extension-метода.

## Что проверено в полном проходе

- Requirements R-01…R-15 и принятые решения владельца; отложенные O-03/O-06/O-07/O-08.
- Фиксация baseline, unknown paths, прямое включение новых test roots, удаление и reload.
- Source-only facts, owner identity, partial keys, RTA/CHA, fixtures и ограничения.
- Все правила binding changes, file/project expansion и их связь с baseline-битсетами.
- Stale-cache fallback с self, полная/частичная актуализация, generation/reset и атомарная запись.
- VSTest/MTP planning, пять whole-project условий, первая сборка, следующие noBuild и процессные исходы.
- Предупреждение в ответе, contains-superset, лимиты вывода/фильтра, тестовая матрица и зависимости эпох.

## API-проба F-011

Среда: PowerShell 7.6.5; локальные Microsoft.CodeAnalysis.Common и CSharp **5.9.0**, lib/net10.0; collectible AssemblyLoadContext.

Созданы unsigned CSharpCompilation App и Tests с OutputKind.DynamicallyLinkedLibrary, соединённые через CompilationReference:

- App.cs в обеих конфигурациях полностью одинаков: public Widget с явным конструктором, internal Render(), возвращающим 2, и статическим Existing().
- Второй syntax tree App — **AssemblyInfo.cs**. До edit в нём только комментарий; после edit — assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Tests"). В обоих вариантах в нём нет объявлений членов типов.
- Tests.cs не меняется: ConsumerExtensions.Render(this Widget) возвращает 1; Check вызывает new Widget().Render(); CheckExisting вызывает Widget.Existing().
- GetDiagnostics всех компиляций обеих конфигураций дал 0 ошибок. Через SemanticModel.GetOperation проверены TargetMethod и ContainingAssembly неизменного invocation.

Получено:

    App source trees: App.cs, AssemblyInfo.cs
    before: Tests / M:ConsumerExtensions.Render(Shared.Widget)
    Compilation errors: 0
    App source trees: App.cs, AssemblyInfo.cs
    after: App / M:Shared.Widget.Render
    Compilation errors: 0

Проверки ожидаемых владельцев Tests/App прошли. Это compiler API check, не запуск будущего TIA. Его пропуск выведен из описанного file expansion для существующего AssemblyInfo.cs с известным baseline.

## Проверка исправленной последовательности сборки

Сверены Tools/TestTools.cs:35–42, 550–557, 584–610 и Services/Build/DotNetTestArguments.cs:81–120:

- Переиспользуемый runner действительно позволяет выбрать pre-test build явным noBuild: false.
- Subsequent noBuild: true соответствует плану; новый run теперь обязан задавать этот выбор, а не наследовать FilterTestOmitSkipsRebuild.
- Существующий path не запускает test при неуспешном pre-build, включая timeout. Это согласуется с новым запретом продолжать тесты проекта после failed build; отдельная находка по этому сценарию не требуется.

Прямой запуск tests/runtime или MCP cancellation в этом раунде не выполнялся: закрытие замечаний относится к specification и planned integration acceptance.

## Принятые ограничения

Приблизительность TIA, metadata leaves, reflection/динамический DI, отсутствие специального блока SourceStructure, выбранный владельцем MTP process-per-class и reset без ожидания компиляции не переоткрывались. Численные пороги и source-generator scope остаются на предусмотренной стадии измерений.

Неопределённости будущей реализации без самостоятельного failure scenario не превращались в findings. Новых настроек, абстракций, публичных инструментов и переписывания плана reviewer не предлагает.

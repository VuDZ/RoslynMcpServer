# Эпоха 2. Результаты: общий механизм, пилотная миграция и рантайм-проверка дедлока

Статус: **выполнена** (2026-09-28, локальные прогоны; CI не запускался).
Решение: [epoch-2-decision.md](epoch-2-decision.md). План: [epoch-2-general-solution.md](epoch-2-general-solution.md).

Корпус эпохи 1, красный в CI на пяти фактах из-за CRLF-разметки checkout, закрыт пилотной
миграцией: локальный прогон даёт 776 из 776 в unit-наборе, 0 падений и 0 пропусков,
`SanitizedEntryChecksTests` — 74 из 74. Мигрированы оба целевых теста, помощники
`SourceTarget`/`SourceMethodLocator`/`SanitizedEntryChecks`, корпус корпуса и второй контракт
(единственная граница `TryApplyChanges`). Добавлена изолированная рантайм-проверка дедлока
в отдельном процессе. `Epoch1SemanticInventoryTests` не мигрирован — это эпоха 3.
Двенадцать фактов добавлены вторым независимым ревью (§3.1), девять — третьим (§3.2), ещё
девять — следующим (§3.3), десять — следующим (§3.4), поэтому числа этого раздела — текущие,
а не только миграционные.

## 1. Что изменено

| Файл | Изменение |
|---|---|
| `RoslynMcpServer.Tests/SourceStructure/SourceFile.cs` | новый: исходник + путь, единственная точка нормализации переводов строк в `\n` |
| `RoslynMcpServer.Tests/SourceStructure/SourceSetScope.cs` | новый: объявленная область (файлы, ссылки, язык, `OutputKind`, условные символы), фабрики `Demo` и `ProductionRepository` |
| `RoslynMcpServer.Tests/SourceStructure/SourceSetAnalysis.cs` | новый: `CSharpCompilation` из исходников, гейт входа, поиск типа/метода/ссылок по символам |
| `RoslynMcpServer.Tests/SourceStructure/SourceReference.cs` | новый: сайт ссылки с символом, кандидатами и причиной |
| `RoslynMcpServer.Tests/SourceStructure/MethodBodyScope.cs` | новый: непосредственное тело метода, связанные вызовы и ссылки, диагностика неразрешённых вызовов |
| `RoslynMcpServer.Tests/SourceStructure/ReachabilityAnalysis.cs`, `ReachabilityResult.cs`, `SourceCallStep.cs`, `ReachabilityLimit.cs`, `ReachabilityLimitKind.cs` | новые: обход исполняемых вызовов до цели с путём, завершением на циклах и записанными ограничениями; вызов делегата разбирается по получателю во всех формах — короткая `_seam()`, явные `_seam.Invoke()`, `_seam?.Invoke()`, `_seam!.Invoke()`, `BeginInvoke`/`EndInvoke`/`DynamicInvoke` (последний опознаётся по объявляющему типу `System.Delegate`, а не по имени) и метод-группа с члена-вызова другого делегата (`Func<T> copy = _seam.Invoke;`); получатель, который не является делегат-членом области (`GetSeam().Invoke()`, `_seams[0].Invoke()`, `GetSeam()()`), даёт ограничение, а не ребро на метаданный `Invoke`; цели делегатов прослеживаются по присваиваниям и инициализаторам внутри области, а значение, полученное вызовом члена или локальной функции области, — по их `return`-выражениям и телу-выражению (один вызов вглубь, §3.4); у разбора делегатов своя граница (`MaxVisitedDelegateMembers` = 256); ограничение — `ReachabilityLimit` с видом `ReachabilityLimitKind` («цель вне области» против «вызываемый делегат или его значение не разрешены»), и любое из них делает обход нерешённым (`IsDecided` требует пустых `Diagnostics` и `Limits`) |
| `RoslynMcpServer.Tests/SourceStructure/BlockingMemberRules.cs` | новый: правила sync-over-async по символам плюс сохранённый запрет имени `GetAwaiter` |
| `RoslynMcpServer.Tests/SourceStructure/MetadataReferenceSets.cs`, `RestoreAssets.cs`, `RepositoryRoot.cs`, `ProductionAnalysis.cs` | новые: ссылки из `TRUSTED_PLATFORM_ASSEMBLIES` и compile-ассетов восстановления, кэш анализа production-исходников |
| `RoslynMcpServer.Tests/SourceStructure/SourceTarget.cs` | изменён: держит нормализованный `SourceFile`, а не текст; namespace и имя типа сохранены |
| `RoslynMcpServer.Tests/SourceStructure/SourceMethodLocator.cs` | удалён: поиск по именам заменён символьными запросами и проверкой формы по связанным узлам |
| `RoslynMcpServer.Tests/SourceStructure/SanitizedEntryChecks.cs` | переписан на семантике: входы принимают `(SourceSetAnalysis, SourceTarget)`, лок — символ поля, добавлены достижимость до sync-входа, правила блокирующих членов и отказ «reachability is undecided» на ограничении вида `DelegateReceiverNotInScope` (§3.3, §3.4) |
| `RoslynMcpServer.Tests/SourceStructure/SanitizedEntryChecksTests.cs` | корпус расширен с 25 до 57 фактов при миграции, затем до 61 вторым ревью (§3.1), до 69 третьим (§3.2), до 71 следующим (§3.3) и до 74 после правки значения делегата (§3.4): сохранены имена 23 фактов, два факта переименованы, добавлены обязательные пары прототипа, CRLF/LF, диагностики, правила async-входа, формы вызова делегата, отказ на вызов делегата через вычисляемого получателя и отказ на значение делегата, полученное вызовом |
| `RoslynMcpServer.Tests/SourceStructure/SourceSetAnalysisTests.cs` | новый: корпус самого механизма (область, слияние partial, namespace, ошибки входа, достижимость, лимиты, формы вызова делегата, вычисляемый получатель, значение делегата из вызова, завершение разбора делегатов и граница обхода) |
| `RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecks.cs` | новый: контракт единственной границы `Workspace.TryApplyChanges` по всей области |
| `RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecksTests.cs` | новый: корпус границы записи (10 фактов) |
| `RoslynMcpServer.Tests/SourceStructure/SanitizedEntryDemoSource.cs` | новый: общий демо-источник контракта и компилируемая мутация для статической и рантайм-проверки |
| `RoslynMcpServer.Tests/SourceStructure/WitnessAssembly.cs`, `WitnessOutcome.cs`, `WitnessStatus.cs`, `DeadlockWitnessClient.cs`, `DeadlockWitnessTests.cs` | новые: компиляция изолированной копии, запуск host-процесса, внешний таймаут, завершение дерева процесса |
| `RoslynMcpServer.Tests/SourceStructure/DeadlockWitnessHost/` (`DeadlockWitnessHost.csproj`, `Program.cs`) | новый консольный проект: протокол `ready pid=` → команда из stdin → `result:`/`error:` |
| `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj` | ссылка на witness host и исключение его исходников из компиляции тестовой сборки |
| `RoslynMcpServer.sln` | проект `DeadlockWitnessHost` добавлен в решение |
| `RoslynMcpServer.Tests/WorkspaceAnalyzerSanitizerTests.cs` | оба целевых теста переведены на семантическую проверку в кэшированном production-анализе |
| `RoslynMcpServer.Tests/WorkspaceWriteBoundaryTests.cs` | `IndexOf`-счёт заменён символьной проверкой границы; неиспользуемый `FindRepoRoot` удалён |

Production-код, порядок его членов, раннер и набор CI-фильтров не менялись. Коммит не делался.

## 2. Среда и baseline

| Что | Значение |
|---|---|
| HEAD | `edc3a4a` + незакоммиченные изменения этой эпохи |
| ОС и SDK | Windows; `dotnet --version` 10.0.300, SDK 10.0.300, TFM `net10.0` |
| Пакеты тестов | xUnit 2.9.3, `xunit.runner.visualstudio` 3.1.0, `Microsoft.NET.Test.Sdk` 17.14.1 |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` 5.9.0; версия сервера 1.4.17 |
| Разметка рабочей копии | CRLF (`core.autocrlf=true`, `.gitattributes` в репозитории нет). Файлы этой эпохи записывались инструментами с LF и перед финальной проверкой переведены в CRLF, чтобы рабочая копия совпадала с checkout на `windows-latest`; итоговый прогон набора выполнен именно на CRLF-копии. Правки по замечаниям ревью внесены в ту же CRLF-копию (проверено: в изменённых файлах нет одиночных LF). В индексе хранится LF (`autocrlf=true`), поэтому перевод рабочей копии не меняет состав изменения |
| Baseline родителя | 693 всего / 674 passed / 6 failed / 13 skipped |
| Уточнение baseline | этот прогон был без действующего фильтра: 789 − 96 новых фактов = 693 (56 фактов миграции, 12 фактов второго ревью, 9 фактов третьего, 9 фактов следующего и 10 фактов последнего, §3.4). Шестое падение — `CliProgressTests.Test_orchestration_without_pre_test_reports_only_dotnet_test`, не связанное с эпохой; в одиночном прогоне и в полном наборе оно зелёное (проверено), поэтому зафиксировано как прежний сбой окружения и не исправлялось |

## 3. Проверки прототипа (корпус контрпримеров)

Корпус механизма — `SourceSetAnalysisTests` (35 фактов), корпус контракта — `SanitizedEntryChecksTests`
(74 факта), корпус границы записи — `WriteBoundaryChecksTests` (10 фактов). Обязательные
положительные и отрицательные пары:

| Требуемая пара | Проверка | Ожидание | Факт |
|---|---|---|---|
| `Results` и пользовательский `Result` против `Task<T>.Result` | `Foreign_member_with_Results_and_forbidden_names_in_comments_keep_success`, `User_defined_Result_property_is_not_a_task_result`, `Task_result_in_sync_entry_fails`, `ValueTask_result_in_sync_entry_fails` | успех / отказ | passed |
| нужный лок против другого `SemaphoreSlim`; правильный `finally` против неправильного; захват лока до защищённого `try` против захвата внутри него | `Foreign_lock_receiver_fails`, `Missing_lock_release_fails`, `Release_outside_finally_fails`, `Release_in_the_finally_of_another_try_fails`, `Lock_field_of_another_type_is_diagnosed`, `Lock_wait_inside_the_protected_try_fails` | успех в baseline, отказ в мутациях | passed |
| прямой вызов и приватная цепочка `async → wrapper → sync` | `Direct_public_sync_call_from_async_entry_fails`, `Transitive_async_wrapper_sync_path_fails` | отказ, во втором случае с путём `ReadThroughWrapper -> DemoManager.GetSanitizedPublishedSolution` | passed |
| одноимённый метод другого receiver и безопасная перегрузка | `Same_named_method_on_another_receiver_keeps_success`, `Safe_overload_of_the_helper_keeps_success`, `Ambiguous_target_method_is_a_diagnostic_error` | успех / успех / диагностика | passed |
| вызванная и невызванная локальная функция или лямбда | `Called_local_function_body_is_analyzed`, `Uncalled_local_function_body_is_not_executed`, `Uncalled_private_method_body_is_not_executed`, `Called_lambda_body_is_analyzed`, `Discarded_lambda_body_is_traversed_fail_closed` | отказ / успех / успех / отказ / отказ (over-приближение задокументировано) | passed |
| generic-вызов, conditional access, `this.`, expression body, partial-тип | `Generic_call_with_a_method_group_argument_fails`, `Conditional_access_call_from_async_entry_fails`, `Extension_method_path_to_the_sync_entry_fails`, `This_qualified_self_call_and_lock_keep_success`, `This_qualified_published_solution_return_keeps_success`, `Expression_body_helper_keeps_success`, `Expression_body_target_reports_missing_lock`, `Partial_type_parts_are_merged_into_one_target`, `Violation_in_the_second_partial_declaration_is_detected` | см. имена | passed |
| переупорядочивание членов, форматирование, комментарии и строки с запрещёнными именами | `Method_reordering_keeps_success`, `Formatting_change_keeps_success`, `Foreign_member_with_Results_and_forbidden_names_in_comments_keep_success` | успех | passed |
| тот же исходник с `\r\n` против `\n` | `Crlf_and_lf_inputs_give_the_same_verdict`, `Crlf_input_is_normalized_before_the_anchors_are_applied`, `Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers`, `Crlf_and_lf_inputs_give_the_same_verdict` (граница записи) | одинаковый вердикт, включая локализованное нарушение | passed (обе стороны каждого факта строятся явно из нормализованного текста, а не из разметки файла; при отключении `SourceFile.Normalize` краснеют все четыре — проверено пробой) |
| неразрешённый символ, неподдерживаемая конструкция, рекурсивная цепочка | `Unresolved_call_is_a_diagnostic_instead_of_a_green_result`, `Unsupported_dynamic_call_is_a_diagnostic`, `Conditional_directive_is_outside_the_declared_scope`, `Recursive_chain_terminates_without_target`, `Target_reached_through_recursive_chain_fails` | диагностика / диагностика / диагностика / успех / отказ | passed |
| делегат-член без присваиваний (контроль) против самоссылки и взаимного цикла | `Invoked_delegate_member_without_assignments_is_a_limit` (лимит, обход заканчивается), `Delegate_self_read_terminates_and_the_assigned_target_is_reached` (цель достигнута), `Mutually_assigned_delegate_members_end_the_expansion` (решённое «не достигнуто» без лимитов) | обход завершается во всех трёх; лимит делает результат нерешённым (`IsDecided == false`), решённое «не достигнуто» остаётся там, где лимитов нет | passed (первые два факта на прежнем коде не завершались — проба, §3.1) |
| присваивание делегату внутри области: метод-группа и лямбда | `Delegate_assignment_to_a_method_group_is_followed`, `Delegate_assignment_to_a_lambda_body_is_followed`, `Delegate_assigned_inside_the_scope_to_the_sync_entry_fails` (контракт, полный набор из двух нарушений) | отказ / отказ / отказ с путём | passed (на прежнем коде целей не было вовсе, а текст ограничения утверждал «присваиваний нет») |
| значение делегата, полученное вызовом: член области против неразрешимого значения | `Delegate_value_returned_by_an_accessor_is_followed`, `Delegate_value_returned_by_an_accessor_is_followed_in_the_explicit_forms` (`local.Invoke()`, `GetSeam()!`), `Delegate_value_returned_by_a_block_bodied_accessor_is_followed`, `Delegate_value_returned_by_an_accessor_is_followed_into_a_field`, `Delegate_value_returned_by_a_local_function_is_followed`, контроль `Delegate_value_returned_by_an_accessor_without_a_scope_target_is_a_limit`, `Delegate_value_computed_by_an_accessor_is_a_limit` (механизм) и `Delegate_value_produced_by_an_accessor_is_a_reachability_violation`, `Delegate_value_produced_by_an_accessor_is_a_violation_in_the_explicit_invoke_form`, `Delegate_value_computed_by_an_accessor_is_an_undecidable_refusal` (контракт) | путь до цели в пяти положительных фактах, лимит `TargetOutsideTheScope` в контроле, лимит `DelegateReceiverNotInScope` на вычисленном значении; на контракте — отказ с путём (два факта) и отказ «reachability is undecided» (один) | passed (на прежнем коде все десять давали решённое «не достигнуто»: `reachable=False decided=True diagnostics=0 limits=0` на механизме и 0 нарушений на контракте — проба, §3.4) |
| форма вызова делегата: короткая против явной | `Delegate_registered_in_another_method_is_followed` (короткая `_seam()`, контроль), `Explicit_delegate_invoke_registered_in_another_method_fails`, `Conditional_explicit_delegate_invoke_registered_in_another_method_fails`, `Null_forgiving_this_qualified_explicit_delegate_invoke_fails`, `Legacy_begin_and_end_invoke_forms_are_followed` (`BeginInvoke`/`EndInvoke`), `Cleared_delegate_keeps_success_for_both_invocation_forms` (отрицательная сторона) | отказ с путём в каждой положительной паре / успех на очищенном делегате | passed (на прежнем коде явная форма не давала ни ребра, ни лимита, ни диагностики: четыре положительных факта краснели, контроль и отрицательный факт оставались зелёными — проба, §3.2) |
| явная форма вызова делегата без цели в области | `Explicit_delegate_invocation_without_a_target_in_the_scope_is_a_limit` (механизм), `Delegate_invocation_without_a_target_in_the_scope_is_recorded_as_a_limit` (короткая форма, контроль) | лимит с именем члена и `IsDecided == false`, ноль диагностик | passed (на прежнем коде явная форма давала решённое «не достигнуто» без лимитов — проба, §3.2) |
| граница разбора делегатов: цепочка короче и длиннее предела | `Delegate_chain_within_the_bound_reaches_the_target`, `Delegate_chain_longer_than_the_bound_is_an_undecidable_walk` | цель достигнута / `IsDecided == false` с диагностикой «обход остановлен» | passed |
| вызов делегата через вычисляемого получателя: явная форма, короткая форма, элемент коллекции, метод-группа | `Delegate_invocation_through_a_computed_receiver_is_a_limit`, `Bare_delegate_invocation_of_a_computed_receiver_is_a_limit`, `Delegate_invocation_through_an_element_access_is_a_limit`, `Delegate_invoke_method_group_of_a_computed_receiver_is_a_limit`, `Delegate_invocation_through_a_computed_receiver_is_an_undecidable_refusal` и `Bare_delegate_invocation_of_a_computed_receiver_is_an_undecidable_refusal` (контракт) | лимит с видом «вызываемый делегат не член области» и `IsDecided == false` на механизме; отказ, а не зелёный вердикт, на контракте | passed (на прежнем коде все шесть давали не тот вердикт: механизменные — лимитов 0, из них три решённое «не достигнуто», а короткая форма элемента — ложную диагностику «does not resolve» вместо лимита; контрактные — 0 нарушений; проба, §3.3) |
| метод-группа с члена-вызова делегата: получатель-член области против вычисляемого | `Delegate_invoke_method_group_of_a_scope_member_is_followed` (цель достигнута), `Delegate_invoke_method_group_of_a_computed_receiver_is_a_limit` (лимит) | ребро на цель / лимит | passed (на прежнем коде оба давали ребро на метаданный `Invoke` без цели и без лимита — проба, §3.3) |
| `DynamicInvoke` на члене области и на вычисляемом получателе; рефлексия как отрицательный контроль | `Delegate_dynamic_invoke_forms_are_followed_or_limited`, `Reflection_invoke_stays_an_ordinary_decided_call` | цель достигнута и лимит соответственно; `MethodBase.Invoke` — обычное ребро с решённым «не достигнуто» | passed (признак — объявляющий тип: `System.Delegate` против класса `MethodBase`; правило по имени ломает второй факт) |
| production-область: ограничение обхода | `Production_reachability_records_the_test_seam_as_the_single_limit` | ровно одно ограничение, называющее тестовый шов, ноль диагностик и `IsDecided == false` | passed |
| sync-over-async в async-входе | `Safe_await_in_async_entry_keeps_success` (обычный `await` — успех), `Task_result_in_async_entry_fails` (1 нарушение), `GetAwaiter_get_result_in_async_entry_fails` (2 нарушения), `Configured_awaiter_get_result_fails` (8 нарушений на четыре формы) | успех / отказ / отказ / отказ | passed (удаление правил для async-входа роняет ровно эти три отрицательных факта — проба, §3.1 и §3.2) |
| правила `Task.WaitAny` и `Configured*`-awaiter-ов | `Task_wait_any_in_sync_entry_fails`, `Configured_awaiter_get_result_fails` | отказ по символу / 8 нарушений на четыре вложенных типа | passed (удаление `WaitAny` роняет ровно первый факт, удаление любой `Configured*`-записи — ровно второй — проба, §3.2) |

`Lock_wait_inside_the_protected_try_fails` добавлен при ревью эпохи: ветка «`_workspaceLock.Wait()`
обязан быть взят до защищённого `try`» достижима, но собственного факта не имела —
`Snapshot_read_before_lock_fails` падает по другой причине (чтение снимка вне защищённого `try`).
Новый факт читает снимок внутри `try`, берёт лок там же и утверждает ровно одно нарушение — порядок
захвата.

Три дефекта корпуса, найденные независимым ревью, исправлены и закреплены проверяемыми фактами:

1. **Пара «вызванная и невызванная локальная функция» не содержала локальных функций.**
   `Called_local_function_body_is_analyzed` и `Uncalled_local_function_body_is_not_executed` объявляли
   приватный метод-член (`MethodDeclarationSyntax`), поэтому ветка
   `LocalFunctionStatementSyntax` в `ReachabilityAnalysis.TryGetBodyScope` не проверялась ничем.
   Теперь оба факта объявляют локальную функцию внутри тела async-входа: вызванная даёт отказ,
   невызванная — успех. Проба (временное удаление ветки) роняет ровно
   `Called_local_function_body_is_analyzed` (0 нарушений вместо одного); остальные 68 фактов того
   состава (класс на момент пробы, до фактов третьего и следующих ревью) и
   `Helper_call_only_inside_local_function_fails` остаются зелёными. В нынешнем составе класса —
   74 факта (§3); число 68 относится к составу пробы, а не к нему. Форма метода-члена сохранена
   отдельным фактом `Uncalled_private_method_body_is_not_executed`, а обработку локальных функций самим
   `SanitizedEntryChecks` (тело локальной функции не считается телом метода) закрепляет
   `Helper_call_only_inside_local_function_fails`, который утверждает полный набор нарушений.
2. **Факт CRLF/LF в корпусе границы записи был инертным и зависел от разметки checkout.**
   `Baseline` — raw-литерал, на CRLF-копии он содержит CRLF, поэтому `lf.Replace("\n", "\r\n")` давал
   `\r\r\n`; сравнивались при этом два пустых списка нарушений, а вердикт с `file:line` не проверялся.
   Теперь CRLF-сторона строится из `SourceFile.Normalize(Baseline)`, LF-сторона возвращается через
   `SourceFile.Create`, сравниваются локализованные наборы нарушений (мутация выносит запись из
   обёртки, вердикт называет `path:line`). Тот же разбор проведён по остальным трём фактам «CRLF
   против LF»: `Crlf_and_lf_inputs_give_the_same_verdict`, `Crlf_input_is_normalized_before_the_anchors_are_applied`,
   `Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers`; каждый теперь строится из явного
   текста и краснеет при отключении нормализации (проба: 4 из 4 красных).
3. **`this.`- и `?.`-квалифицированный прямой вызов классифицировался как упоминание.**
   `SanitizedEntryChecks.ReportMentions` пропускал только случай `reference.Node.Parent is
   InvocationExpressionSyntax`, поэтому у `this.GetSanitizedPublishedSolution()` и
   `_neighbor?.GetSanitizedPublishedSolution()` родитель имени — member access или member binding, и
   настоящее вызовное ребро сообщалось второй раз как «method group … deadlock». Форма ссылки теперь
   определяется по вызову, которому принадлежит имя: `IsInvokedMember` разбирает простое имя, member
   access с `this.`/`x.` и member binding `x?.`. Закреплено полными наборами нарушений:
   `This_qualified_public_sync_call_from_async_entry_fails` и
   `Conditional_access_call_from_async_entry_fails` утверждают `Assert.Single` (ровно одно нарушение —
   достижимость) вместо фрагмента. Проба с прежним условием даёт 2 нарушения в обоих фактах.

### 3.1. Три дефекта, найденные вторым независимым ревью

1. **Разбор делегатов не завершался на легальном входе.** `DelegateTargets` не имел ни множества
   посещённых членов, ни границы очереди, а `AssignedValue` для чтения члена возвращал сам
   сканируемый узел: `var copy = _callback;` и `_other = _callback;` возвращали в разбор тот же
   член, который разбирался, поэтому очередь `pending` росла бесконечно. Проба faithful-копией
   прежней логики (временный проект вне репозитория, сторож на 5 с) на источнике факта
   `Delegate_self_read_terminates_and_the_assigned_target_is_reached` печатает
   `HANG: the old expansion was still running after 5 s (delegate '_callback' at Demo/Demo.cs:12)`,
   на источнике `Mutually_assigned_delegate_members_end_the_expansion` — то же для `_first`, а
   контрольный источник `Invoked_delegate_member_without_assignments_is_a_limit` завершается за
   5 мс с записанным ограничением. У xunit-факта нет таймаута, поэтому такая регрессия вешала бы
   unit-набор до лимита CI (20 минут) вместо падения. Теперь у разбора есть множество посещённых
   членов (самоссылка и взаимный цикл заканчивают разбор), счётчик разобранных членов с границей
   `MaxVisitedDelegateMembers` = 256 и диагностика «обход остановлен» при её достижении; граница
   закреплена парой фактов про цепочку делегатов.
2. **Объявленный поиск целей по присваиваниям не был реализован, а текст ограничения утверждал
   обратное.** §7 решения обещал поиск целей по присваиваниям внутри области, но `AssignedValue`
   возвращал `null` для левой части присваивания, поэтому присваивание внутри области не
   засчитывалось: проба на источнике с `_callback = ReadTarget;` и с `_callback = () => Wanted();`
   давала пустой список целей и ограничение «`_callback` invoked at Demo/Demo.cs:10 has no
   assignment inside the declared scope» — утверждение, ложное для области, в которой такое
   присваивание есть. Реализован вариант (a): цели ищутся по правой части присваиваний, цель
   которых — сам член, и по инициализатору его объявления (иначе
   `var boundarySeam = AfterPhysicalLoadBeforePrepareAsync;` невидимо: имя локальной переменной в
   объявлении не является сайтом ссылки). Лямбда становится исполняемой областью, метод — ребром
   вызова, чтение члена целью не является (именно оно и зацикливало разбор), `null`/`default` —
   легальная очистка делегата без диагностики. Якорь поведения —
   `Delegate_assigned_inside_the_scope_to_the_sync_entry_fails`: полный набор из двух нарушений
   (method group на сайте присваивания и путь через делегат). Ещё одно следствие: прежняя ветка
   `AssignedValue` для `EqualsValueClause` давала ложную цель — вложенный вызов в инициализаторе
   (`var copy = Wrap(_callback);`) объявлялся целью делегата, то есть ребром вызова делегата к
   `Wrap` (проба: `method targets=[method:Wrap]`, ограничений и диагностик нет); теперь чтение
   цели не даёт, а сам `Wrap(...)` остаётся исполняемым вызовом тела, как и раньше. Ничего
   реального при этом не теряется: вызов `Wrap` в исполняемом теле и так даёт ребро обычным обходом.
3. **Правило «блокирующие члены проверяются и в async-входе» было заявлено, но не закреплено.**
   Все пять фактов про блокирующие члены мутировали только синхронный вход, поэтому удаление
   вызова правил для async-входа оставляло корпус зелёным. Добавлена пара: обычный `await` в
   async-входе остаётся зелёным (`Safe_await_in_async_entry_keeps_success`), а
   `Task.FromResult(0).Result` и `GetAwaiter().GetResult()` в async-входе дают одно и два нарушения
   (`Task_result_in_async_entry_fails`, `GetAwaiter_get_result_in_async_entry_fails`). Проба
   (временное удаление вызова для async-входа) на составе из 61 факта роняла ровно эти два факта и
   не роняла остальные 59 — свойство проверено, а не заявлено; третьим ревью проба повторена на
   выросшем корпусе: краснеют эти два факта и добавленный `Configured_awaiter_get_result_fails`,
   остальные 66 остаются зелёными (§3.2). Правило работает и на реальном коде:
   `SolutionManager.GetSanitizedPublishedSolutionAsync` в production-области нарушений не даёт,
   потому что не содержит блокирующих членов.

### 3.2. Два дефекта, найденные третьим независимым ревью

1. **Явная форма вызова делегата молча проглатывалась.** `_seam.Invoke()`, `_seam?.Invoke()` и
   `_seam!.Invoke()` связываются компилятором с членом `Invoke` типа-делегата, а не с самим
   членом-делегатом, поэтому в `ReachabilityAnalysis.ExecutedCalls` срабатывала ветка
   `symbol is IMethodSymbol`, а ветка разбора делегатов не достигалась: ни ребра, ни лимита, ни
   диагностики — обход возвращал `IsDecided == true` с пустым списком `Limits`, то есть выдавал
   **решённое «не достигнуто»** там, где цель зарегистрирована в области и нарушение есть. Проба
   ревьюера: источник, в котором `_seam = GetSanitizedPublishedSolution;` стоит в отдельном методе
   той же объявленной области, а async-вход исполняет `_ = _seam!.Invoke();`, давал
   `VerifySanitizedEntries == 0` (контракт зелёный), тогда как побайтово тот же источник с
   `_ = _seam();` давал нарушение достижимости с путём. Форма «регистрация в другом методе» — это
   ровно форма production-шва: `boundarySeam` получает значение в `LoadAsync` из свойства
   `AfterPhysicalLoadBeforePrepareAsync`, которое выставляется вне области.

   Реализован выбранный вариант: явная форма даёт ребро делегата на получателе, как и короткая.
   Ниже правило приведено в том виде, в каком оно внесено этим раундом, — это состояние §3.2, а не
   текущее правило; текущее правило шире и описано в §3.3. Классификация того раунда принимала
   вызов только тогда, когда связанный член объявлен на **типе-делегате** (`TypeKind.Delegate`) и
   называется `Invoke`, `BeginInvoke` или `EndInvoke`, а получатель (`_seam`, `this._seam!`, `_seam`
   из `_seam?.Invoke()`, в том числе в скобках) сам связывается с делегат-членом области; дальше
   вызов идёт существующим разбором делегатов — тот же поиск целей по присваиваниям, то же множество
   посещённых членов, та же граница и те же лимиты. Член `Invoke` обычного типа
   (`MethodInfo.Invoke(...)` в `Services/RoslynCodeFixBridge.cs`) под правило не попадает: он объявлен
   на классе `MethodBase`, а не на типе-делегате, поэтому остаётся обычным ребром и ложного
   срабатывания не даёт. §3.3 заменил эту классификацию единой `IsDelegateCall`: она принимает ещё и
   значение типа-делегата (ветка `GetTypeInfo(...).Type is { TypeKind: TypeKind.Delegate }` —
   `GetSeam()()`, `_seams[0]()`) и `DynamicInvoke`, а вызов делегата, получатель которого не является
   делегат-членом области, не отбрасывает, а записывает ограничением вида
   `DelegateReceiverNotInScope`; на этом виде `SanitizedEntryChecks` сообщает явный отказ
   «reachability is undecided», а не «не достигнуто».

   Отдельно уточнена семантика `IsDecided`: раньше это было `Diagnostics.Count == 0`, поэтому
   обход, записавший лимит, всё равно назывался решённым. Теперь `IsDecided` требует пустых
   `Diagnostics` **и** `Limits`: лимит — это конструкция, которую обход не проследил by design
   (цель делегата вне области), и объявлять отсутствие вызова доказанным на таком обходе нельзя
   (требование плана «неподдерживаемая конструкция не превращается в доказательство отсутствия
   запрещённого вызова»). Вердикт контрактных проверок от этого не меняется: `SanitizedEntryChecks`
   собирает нарушения из `Diagnostics`, поэтому production-проверка при одном лишь лимите остаётся
   зелёной — иначе тестовый шов `boundarySeam` делал бы её красной. (После §3.3 это верно для
   ограничения вида `TargetOutsideTheScope`; ограничение вида `DelegateReceiverNotInScope` —
   не разобранный сайт вызова — сообщается нарушением.)

   Добавлены шесть фактов контракта и один факт механизма, и все они проверены пробами (полные
   копии репозитория вне рабочего дерева, удалены после прогонов):

   - `Delegate_registered_in_another_method_is_followed` — контроль: короткая `_seam()` при
     регистрации в другом методе даёт путь (зелёный и до правки);
   - `Explicit_delegate_invoke_registered_in_another_method_fails`,
     `Conditional_explicit_delegate_invoke_registered_in_another_method_fails`,
     `Null_forgiving_this_qualified_explicit_delegate_invoke_fails`,
     `Legacy_begin_and_end_invoke_forms_are_followed` — четыре положительных факта на формы
     `Invoke`, `?.Invoke`, `this.`+`!` и `BeginInvoke`/`EndInvoke`; каждый утверждает ровно одно
     нарушение и путь `DemoManager.GetSanitizedPublishedSolutionAsync -> DemoManager.GetSanitizedPublishedSolution`.
     Проба faithful-состоянием прежней логики (разбор явной формы вызова делегата удалён) роняет
     ровно эти четыре факта; контроль и остальные 64 факта класса остаются зелёными;
   - `Cleared_delegate_keeps_success_for_both_invocation_forms` — отрицательная сторона: делегат,
     очищенный (`_seam = null;`) в области, не даёт ни пути, ни лимита ни в короткой, ни в явной
     форме. Зелёный и до, и после правки — это защита от ложного срабатывания, а не контрпример;
   - `Explicit_delegate_invocation_without_a_target_in_the_scope_is_a_limit` (корпус механизма):
     `_callback.Invoke()` без присваиваний в области даёт лимит с именем члена, ноль диагностик и
     `IsDecided == false`; тем же фактом проверена и скобочная форма `(_callback).Invoke()`, потому что
     скобки — обёртка, через которую модель член не связывает, и без её разворачивания форма молча
     ушла бы в обычное ребро. Проба прежней логики роняет ровно этот факт: он получал `IsDecided == true`
     и пустой список лимитов (проба снята до добавления скобочной формы — та проверяет тот же путь и
     требование только усиливает).

   Production-наблюдение после правки: `ProductionAnalysis.Instance` — 140 файлов, 0 ошибок входа;
   обход от async-входа записывает ровно одно ограничение (текст ниже), ноль диагностик, и
   `IsDecided` меняется с `true` на `false`, потому что лимит теперь учитывается. Проверка
   `VerifySanitizedEntries` на `Services/SolutionManager.cs` по-прежнему возвращает 0 нарушений.
   Отдельно проверено, что правка не добавила лимитов в других местах: `PublishMainOnly` из
   `AnalyzerShadowGenerationPublisher` достижим от async-входа, а три его тестовых хука
   (`AfterRequiredCopyBeforeManifest`, `BeforeMove`, `MutateSourceAfterFirstHash`), вызываемые через
   `?.Invoke(...)`, очищаются в `ResetTestHooks` внутри области, поэтому целей не имеют и в `Limits`
   не попадают — как и раньше.

2. **Два правила sync-over-async были заявлены, но не закреплены.** §8 решения называет в наборе
   членов `Task.Wait`/`WaitAll`/`WaitAny` и awaiter-типы, включая `Configured*`, но корпус имел факты
   только на `Wait` и `WaitAll` и на `GetResult` простого `TaskAwaiter`: удаление `WaitAny` или любой
   из четырёх `Configured*`-записей `AwaiterMetadataNames` оставляло все 61 факт зелёными, то есть
   регрессия заявленного правила была ненаблюдаемой. Правила при этом работали (проба ревьюера:
   `Task.WaitAny(Task.CompletedTask)` → нарушение блокирующего ожидания;
   `Task.CompletedTask.ConfigureAwait(false).GetAwaiter().GetResult()` → нарушения `GetAwaiter` и
   `GetResult`).

   Добавлены два факта:

   - `Task_wait_any_in_sync_entry_fails` — `Task.WaitAny(Task.CompletedTask)` в синхронном входе;
   - `Configured_awaiter_get_result_fails` — четыре формы в async-входе
     (`Task.CompletedTask.ConfigureAwait(false)`, `Task.FromResult(0).ConfigureAwait(false)`,
     `new ValueTask().ConfigureAwait(false)`, `new ValueTask<int>(1).ConfigureAwait(false)`), каждая
     с `GetAwaiter().GetResult()`, то есть по одной на каждый вложенный тип
     `ConfiguredTaskAwaitable(+<T>)` и `ConfiguredValueTaskAwaitable(+<T>)`. Факт утверждает полный
     набор — 8 нарушений, по два на форму, — поэтому удаление любой одной записи из
     `AwaiterMetadataNames` оставляет одну форму с одним нарушением и роняет факт. Рядом в том же
     методе стоит обычный `await Task.CompletedTask.ConfigureAwait(false);`: он не даёт ни одного
     нарушения, и это удерживает отрицательную сторону правила.

   Пробы (в той же копии вне репозитория): удаление `WaitAny` роняет ровно
   `Task_wait_any_in_sync_entry_fails`; удаление каждой из четырёх `Configured*`-записей по
   отдельности роняет ровно `Configured_awaiter_get_result_fails` (8 нарушений вместо 7); в обоих
   случаях остальные факты класса зелёные. Пробы сняты на составе из 67 фактов — до добавления
   `Cleared_delegate_keeps_success_for_both_invocation_forms` и
   `Legacy_begin_and_end_invoke_forms_are_followed`, которые правила блокирующих членов не
   затрагивают.

Дополнительно в корпусе остались все диагностические пары эпохи 1: отсутствующий метод,
перегрузка целевого метода, метод без тела, обрезанный источник, чужой namespace (имена фактов
сохранены), а также `WaitAsync_result_is_a_non_compiling_example`: некомпилируемая мутация
по-прежнему не выдаётся за обнаружение — теперь она возвращает ошибку компиляции входа **и**
`unresolved blocking member name \`Result\``.

Каждый пример, объявленный компилируемой мутацией, проверяется на отсутствие ошибок компиляции
(`Assert.Empty(InputErrors(...))`), поэтому отказ проверки на некомпилируемом источнике невозможен
без падения самого теста. Якоря мутаций берутся из нормализованного текста; отсутствующий якорь
роняет пример (`ReplaceFirst`/`SanitizedEntryDemoSource.Mutate` бросают исключение), а не оставляет
источник неизменённым.

### 3.3. Дефект, найденный следующим независимым ревью

1. **Вызов делегата через вычисляемого получателя молча становился доказательством отсутствия.**
   Третье ревью (§3.2) закрыло явную форму `_seam.Invoke()` для получателя-члена области, но
   классификация шла по одному признаку — «связанный член объявлен на типе-делегате **и** получатель
   связан с делегат-членом области». Если получатель вычисляется, второй признак ложен, ветка разбора
   делегатов не достигалась, и вызов уходил в обычную ветку `symbol is IMethodSymbol`: ребро на
   метаданный `Invoke()`, у которого цели нет. Итог — ни лимита, ни диагностики, `IsDecided == true`,
   то есть **решённое «не достигнуто»** там, где цель зарегистрирована в области и вызывается в рантайме.
   Это прямое нарушение требования плана «неподдерживаемая конструкция не превращается в доказательство
   отсутствия запрещённого вызова».

   Проба (полная копия рабочего дерева вне репозитория, удалена после прогонов; источник каждого факта
   — тот же, что у добавляемого факта) на прежнем коде:

   | Форма | Прежний вердикт обхода | Контрактная проверка |
   |---|---|---|
   | `Entry() => GetSeam().Invoke()` (получатель — результат метода) | решённое «не достигнуто», лимитов 0, диагностик 0 | — |
   | `Entry() => GetSeam()()` | решённое «не достигнуто» (ребро на метод `GetSeam`) | — |
   | `Entry() => _seams[0].Invoke()` | решённое «не достигнуто», лимитов 0 | — |
   | `Entry() => _seams[0]()` | диагностика «call … does not resolve» — нерешённый результат, но по ложной причине (вызов связывается с `Invoke` типа элемента) и без лимита | — |
   | `Func<int> copy = _callback.Invoke; copy();` при `_callback = ReadTarget;` | решённое «не достигнуто»: ребро на метаданный `Invoke` вместо `ReadTarget` | — |
   | `Func<int> copy = GetSeam().Invoke; copy();` | решённое «не достигнуто» | — |
   | `(int)_callback.DynamicInvoke()!` при `_callback = ReadTarget;` | решённое «не достигнуто» (`DynamicInvoke` — член класса `System.Delegate`) | — |
   | `_ = GetSeam()!.Invoke();` в async-входе при `_seam = GetSanitizedPublishedSolution;` в другом методе области | — | **0 нарушений** (зелёный вердикт) |
   | `_ = GetSeam()!();` в том же источнике | — | **0 нарушений** |

   Проба ревьюера на источнике с регистрацией цели в области (`GetSeam() => _seam` и `_seam = Wanted;`)
   дала тот же вердикт для первой строки — `reachable=False decided=True limits=[] diagnostics=[]`, —
   а побайтово тот же источник с `_seam!.Invoke();` вместо `GetSeam()!.Invoke();` в контрактной проверке
   дал путь до sync-входа. Разница только в получателе, поэтому и вердикты обязаны совпадать.

   Все примеры таблицы краснеют на прежнем коде ровно по этой причине. Один из девяти добавленных
   фактов, `Reflection_invoke_stays_an_ordinary_decided_call`, — отрицательный контроль: он зелёный и
   до, и после правки, потому что `MethodBase.Invoke` обычным ребром и остаётся; правило, завязанное
   на имя `Invoke` без проверки объявляющего типа, роняет именно его.

   Реализовано: единая классификация вызова делегата (`IsDelegateCall`) вместо разбора по одному
   признаку. Она отвечает на два вопроса сразу — «это вызов делегата вообще» и «через какой
   делегат-член области он идёт»:

   - **вызов делегата** — это (1) выражение, связанное с делегат-членом (`_seam()`); (2) член
     `Invoke`, `BeginInvoke`, `EndInvoke` типа-делегата или `DynamicInvoke` типа `System.Delegate`
     (`_seam.Invoke()`, `GetSeam().Invoke()`, `_seams[0].Invoke()`, `_seam?.Invoke()`); (3) выражение,
     тип которого — делегат, то есть вызов значения, а не члена (`GetSeam()()`, `_seams[0]()`);
   - **получатель** ищется по выражению вызова: member access, member binding внутри conditional
     access, со снятием `!` и скобок. Получатель принимается, только если он связан с
     делегат-членом области; иначе вызов записывается в `Limits` с текстом, называющим саму
     конструкцию и её сайт: «`<конструкция>` at `<файл>:<строка>`: the invoked delegate is not a
     delegate member of the declared scope, so the targets it can hold are not followed».
     `MethodInfo.Invoke`/`MethodBase.Invoke` под правило не попадает: его объявляющий тип — класс,
     а не делегат, поэтому вызов остаётся обычным ребром и обход остаётся решённым
     (`Reflection_invoke_stays_an_ordinary_decided_call`).

   Второе следствие той же ошибки — **метод-группа с члена-вызова делегата**: `Func<T> copy = _seam.Invoke;`
   даёт делегат, чья цель — то, что держит получатель, а не метаданный `Invoke`. Такое присвоенное
   значение теперь передаёт получателя в тот же разбор делегатов (получатель-член области) или
   записывается лимитом (вычисляемый получатель); обычным ребром на `Invoke` он больше не становится.
   Циклы и граница разбора при этом не изменились: получатель входит в то же множество посещённых
   членов и в тот же счётчик `MaxVisitedDelegateMembers`.

   Третье следствие — **лимит перестал быть однородным**. Их два вида, и они различаются не текстом,
   а значением `ReachabilityLimitKind`: `TargetOutsideTheScope` (вызванный член области не имеет
   присваивания в области — цель зарегистрирована вне её) и `DelegateReceiverNotInScope` (сам сайт
   вызова не разобран). Различие обязательно: проверка контракта обязана отказать на втором виде —
   такой сайт не видит ни одно другое её правило, — но не может отказать на первом, иначе
   production-проверка станет красной из-за тестового шва `boundarySeam`, выставляемого вне области.
   Поэтому `Limits` теперь `IReadOnlyList<ReachabilityLimit>` (вид + текст), а
   `SanitizedEntryChecks` добавляет нарушение «reachability is undecided — <текст лимита>» только для
   `DelegateReceiverNotInScope`. Число диагностик и условие `IsDecided` не изменились: лимит любого
   вида делает обход нерешённым.

   Добавлены девять фактов — семь в корпусе механизма и два в корпусе контракта:

   - `Delegate_invocation_through_a_computed_receiver_is_a_limit` (`GetSeam().Invoke()`),
     `Bare_delegate_invocation_of_a_computed_receiver_is_a_limit` (`GetSeam()()`),
     `Delegate_invocation_through_an_element_access_is_a_limit` (`_seams[0].Invoke()` и
     `_seams[0]()`), `Delegate_invoke_method_group_of_a_computed_receiver_is_a_limit`
     (`Func<int> copy = GetSeam().Invoke;`) — четыре факта на лимит: ровно один лимит вида
     `DelegateReceiverNotInScope`, ноль диагностик, `IsDecided == false`, и текст называет
     конструкцию с её сайтом;
   - `Delegate_invoke_method_group_of_a_scope_member_is_followed` — контроль к предыдущему: получатель
     с присваиванием в области даёт путь `Manager.Entry -> Manager.ReadTarget`, а не лимит;
   - `Delegate_dynamic_invoke_forms_are_followed_or_limited` — `DynamicInvoke` на члене области даёт
     путь, на вычисляемом получателе — лимит; заодно это единственный факт, который проверяет
     опознание по объявляющему типу `System.Delegate`;
   - `Reflection_invoke_stays_an_ordinary_decided_call` — отрицательный контроль: `MethodBase.Invoke`
     остаётся обычным ребром, обход решён. Правило, завязанное на имя `Invoke` без проверки
     объявляющего типа, роняет именно этот факт;
   - `Delegate_invocation_through_a_computed_receiver_is_an_undecidable_refusal` и
     `Bare_delegate_invocation_of_a_computed_receiver_is_an_undecidable_refusal` (корпус контракта) —
     `_ = GetSeam()!.Invoke();` и `_ = GetSeam()!();` в async-входе при `_seam =
     GetSanitizedPublishedSolution;` в другом методе области: ровно одно нарушение, текст называет
     и отказ, и конструкцию. На прежнем коде оба дают 0 нарушений.

   Production-наблюдение после правки (кэшированный анализ, 140 файлов, 0 ошибок входа): обход от
   async-входа по-прежнему записывает **ровно одно** ограничение — вид `TargetOutsideTheScope`, текст
   «`AfterPhysicalLoadBeforePrepareAsync`, reached from the invoked delegate `boundarySeam` at
   `…\Services\SolutionManager.cs:825`, has no assignment inside the declared scope; targets registered
   outside the scope are not followed», — ноль диагностик, путь до sync-входа не найден,
   `IsDecided == false`; `VerifySanitizedEntries` на `Services/SolutionManager.cs` возвращает
   **0 нарушений**. Правка не добавила production-обходу ни одного лимита нового вида: единственная
   форма «вызов значения» в production-коде (`descriptor.CreateFactory()(_services)` в
   `Hosting/McpToolActivationService.cs`) лежит вне обхода от async-входа, а три `?.Invoke(...)`-хука
   `AnalyzerShadowGenerationPublisher` очищаются в `ResetTestHooks` внутри области, как и раньше (§3.2).

### 3.4. Дефект, найденный следующим независимым ревью

1. **Значение делегата, полученное вызовом, молча становилось доказательством отсутствия.**
   §3.3 закрыла вычисляемого получателя на сайте вызова, но **значение**, присваиваемое делегату,
   разбиралось по одному признаку — «связанный символ является `IMethodSymbol`»: и метод-группа
   (`_callback = ReadTarget;`), и результат вызова (`System.Func<int> local = GetSeam();`) попадали
   в одну ветку, поэтому целью разбора становился сам вызванный член `GetSeam`. Обход входил в его
   тело, не находил там вызовов и заканчивался **решённым «не достигнуто»** с пустыми `Diagnostics` и
   `Limits` — при том, что `GetSeam` возвращает делегат, зарегистрированный в области на публичный
   синхронный вход. Это прямое нарушение требования плана «неподдерживаемая конструкция не
   превращается в доказательство отсутствия запрещённого вызова».

   Проба (полная копия рабочего дерева вне репозитория, удалена после прогонов; исходники — те же,
   что у добавляемых фактов) на прежнем коде:

   | Форма | Прежний вердикт обхода | Прежняя контрактная проверка |
   |---|---|---|
   | `_callback = ReadTarget; System.Func<int> local = GetSeam(); return local();` при `GetSeam() => _callback` | решённое «не достигнуто»: `reachable=False decided=True diagnostics=0 limits=0` | — |
   | `Func<Snapshot?> local = GetSeam(); _ = local();` в async-входе при `_seam = GetSanitizedPublishedSolution;` в другом методе области | — | **0 нарушений** (зелёный вердикт), тогда как побайтово тот же источник с `_ = _seam();` даёт нарушение достижимости с путём |

   Формы `local.Invoke()`, `GetSeam()!`, блочное тело аксессора, поле вместо локальной переменной и
   локальная функция-аксессор давали тот же вердикт; все они закреплены фактами ниже. На том же
   прежнем коде упали ровно десять новых фактов (семь механизма и три контракта), а все 99 прежних
   фактов этих двух классов остались зелёными — то есть факты нацелены на дефект.

   Реализовано: присвоенное значение, которое является результатом вызова, больше не читается как
   метод-группа. Обход находит вызванный член (или локальную функцию) в области, читает его
   `return`-выражения и тело-выражение и классифицирует их **тем же разбором**, что и присваивание:
   возвращённый делегат-член входит в тот же разбор с тем же множеством посещённых и той же границей
   `MaxVisitedDelegateMembers`, возвращённая метод-группа становится ребром вызова, возвращённая
   лямбда — исполняемой областью. Вглубь идёт ровно один вызов: результат другого вызова, условие,
   доступ к элементу, член вне области и член без тела в области записываются ограничением
   `DelegateReceiverNotInScope` — «reachability is undecided» на контракте, а не «не достигнуто».
   `MethodBase.Invoke` (рефлексия) остаётся обычным решённым ребром, формы вызова делегата из
   §3.2/§3.3 не изменились.

   Добавлены десять фактов — семь в корпусе механизма и три в корпусе контракта:

   - `Delegate_value_returned_by_an_accessor_is_followed` — выражение-тело аксессора, локальная
     переменная, короткая форма `local()`: путь `Manager.Entry -> Manager.ReadTarget`;
   - `Delegate_value_returned_by_an_accessor_is_followed_in_the_explicit_forms` — тот же путь для
     `local.Invoke()` и для `System.Func<int> local = GetSeam()!;`;
   - `Delegate_value_returned_by_a_block_bodied_accessor_is_followed` и
     `Delegate_value_returned_by_a_local_function_is_followed` — то же значение из блочного тела и из
     локальной функции (путь тот же, объявление ищется по связанному символу);
   - `Delegate_value_returned_by_an_accessor_is_followed_into_a_field` — значение хранится в поле и
     вызывается из него;
   - `Delegate_value_returned_by_an_accessor_without_a_scope_target_is_a_limit` — контроль: аксессор
     возвращает делегат-член, которому область ничего не присваивает, поэтому обход записывает ровно
     одно ограничение вида `TargetOutsideTheScope`, а не решённое «не достигнуто» (на прежнем коде
     лимитов было 0);
   - `Delegate_value_computed_by_an_accessor_is_a_limit` — вычисленное значение: результат другого
     вызова и условие `_preferFirst ? _callback : _fallback` дают лимит вида
     `DelegateReceiverNotInScope` с текстом, называющим конструкцию и её сайт;
   - `Delegate_value_produced_by_an_accessor_is_a_reachability_violation` и
     `Delegate_value_produced_by_an_accessor_is_a_violation_in_the_explicit_invoke_form` (контракт) —
     `Func<Snapshot?> local = GetSeam(); _ = local();` и `_ = local.Invoke();`: ровно одно нарушение,
     текст называет путь `DemoManager.GetSanitizedPublishedSolutionAsync ->
     DemoManager.GetSanitizedPublishedSolution`; на прежнем коде оба дают 0 нарушений;
   - `Delegate_value_computed_by_an_accessor_is_an_undecidable_refusal` (контракт) — аксессор
     возвращает результат другого вызова: ровно одно нарушение «reachability is undecided», потому
     что этот сайт не видит ни одно другое правило проверки.

   Production-наблюдение после правки (кэшированный анализ, 140 файлов, 0 ошибок входа): вердикт не
   изменился — путь до sync-входа не найден, диагностик 0, ограничение ровно одно и того же вида
   `TargetOutsideTheScope` с тем же текстом шва, `IsDecided == false`; `VerifySanitizedEntries` и
   `VerifyRawPublishedEntry` на `Services/SolutionManager.cs` возвращают **0 нарушений** каждое.
   Число обойдённых методов — те же 404 (замер пробником на копии): значение делегата, полученное
   вызовом, в production-обходе от async-входа не встречается, поэтому лимитов нового вида обход не
   получил.

## 4. Судьба пилотных артефактов эпохи 1

| Артефакт эпохи 1 | Судьба | Какая проверка сохраняет контракт |
|---|---|---|
| `WorkspaceAnalyzerSanitizerTests.GetPublishedSolutionAfterDiskSyncAsync_still_returns_raw_published_snapshot` | **перенесён**, имя сохранено | `SanitizedEntryChecks.VerifyRawPublishedEntry(ProductionAnalysis.Instance, SolutionManagerTarget())`, `Assert.Empty` |
| `WorkspaceAnalyzerSanitizerTests.GetSanitizedPublishedSolution_takes_workspace_lock_and_async_does_not_call_sync` | **перенесён**, имя сохранено | `SanitizedEntryChecks.VerifySanitizedEntries(ProductionAnalysis.Instance, SolutionManagerTarget())`, `Assert.Empty` |
| помощник `SourceTarget` | **объединён** с нормализованным `SourceFile`; форма записи изменилась на `(File, NamespaceName, TypeName)` | цель ищется в объявленной области по metadata name |
| помощник `SourceMethodLocator` | **заменён и удалён** | `SourceSetAnalysis` (поиск типа/метода/ссылок по символам) + `MethodBodyScope` (тело, отложенные области, неразрешённые вызовы) + `ReachabilityAnalysis` (путь) |
| помощник `SanitizedEntryChecks` | **переписан**, публичные точки входа те же (`VerifySanitizedEntries`, `VerifyRawPublishedEntry`) | все утверждения §1.1 README, теперь по символам |
| корпус `SanitizedEntryChecksTests` (25 фактов) | **объединён и расширен** до 57 фактов в том же файле и классе при миграции, до 61 после второго ревью (§3.1), до 69 после третьего (§3.2), до 71 после следующего (§3.3) и до 74 после правки значения делегата (§3.4); имена 23 фактов сохранены, два факта переименованы | сам корпус; см. таблицу ниже |
| `Epoch1SemanticInventoryTests` | **отложен в эпоху 3** (в этой эпохе не мигрирован, не изменялся) | — |

Переименований два:

1. `Partial_type_is_a_diagnostic_error` → `Partial_type_parts_are_merged_into_one_target`
(и добавлен отрицательный факт `Violation_in_the_second_partial_declaration_is_detected`). Причина:
части partial-типа теперь объединяются, и диагностика на две декларации противоречила бы требованию
«partial-типы не теряют части молча».
2. `WaitAsync_result_is_a_syntax_only_example` → `WaitAsync_result_is_a_non_compiling_example`.
Старое имя называло меру гарантии эпохи 1: проверка сравнивала имена по синтаксическому дереву, а сам
пример `_workspaceLock.WaitAsync().Result` не компилируется и исполняемой мутацией не считался. Новое
имя называет наблюдаемое свойство примера — некомпилируемость, — а факт утверждает путь диагностики,
по которому теперь приходит отказ: ошибка компиляции входа и `unresolved blocking member name \`Result\``
вместо прежнего текстового `member access Result`.

Ссылки на старые имена остались только в исторических документах (`epoch-1-results.md`, план
эпохи 2), которые не правятся «под факт».

Соответствие старых утверждений новым проверкам:

| Утверждение эпохи 1 | Проверка эпохи 2 |
|---|---|
| async: есть вызов `GetOrCreateSanitizedPublishedSolution()` | `VerifyHelperCall`: вызов по символу в непосредственном теле; вызов внутри локальной функции или лямбды не засчитывается |
| async: нет ссылки на `GetSanitizedPublishedSolution` | `ReportMentions` (только формы без вызова: `method group`, `nameof mention`) + `ReachabilityAnalysis` (вызов, прямой или транзитивный, с путём). Каждая ссылка сообщается ровно один раз: `this.M()` и `x?.M()` — вызовы и попадают только в достижимость |
| sync: есть `_workspaceLock.Wait(` | вызов `Wait` на символе поля `_workspaceLock`; добавлена проверка типа поля (`SemaphoreSlim`) |
| sync: есть `_workspaceLock.Release()` в `finally` | вызов `Release` на символе поля в `finally` защищённого `try`; добавлено положение захвата до `try` |
| sync: helper вызывается внутри защищённого `try` | вызов символа helper внутри блока защищённого `try` |
| sync: нет `.Result` и `GetAwaiter()` | `BlockingMemberRules`: `Task<TResult>.Result`/`ValueTask<TResult>.Result` по символу, `GetAwaiter` по имени (запрет не ослаблен), плюс новые правила `Task.Wait/WaitAll/WaitAny` и `GetResult` на awaiter-типах |
| raw: есть `return _solution;` | оператор `return`, выражение которого — символ поля `_solution` (`this._solution` покрыт автоматически) |
| raw: нет `GetOrCreateSanitizedPublishedSolution`, `RemoveUnresolvedAnalyzers`, `SetPublishedSolution` | запрет ссылок по имени в непосредственном теле; содержимое лямбд и локальных функций больше не считается телом |
| граница записи: одна подстрока `workspace.TryApplyChanges(` | `WriteBoundaryChecks`: ровно одна ссылка на **символ** `Workspace.TryApplyChanges` во всей production-области |
| граница записи: есть `private bool TryApplyWorkspaceChanges` | метод `TryApplyWorkspaceChanges` находится по символу, и единственный сайт вызова обязан быть внутри него |
| граница записи: нет `RevertAnalyzerReferenceOverlayForApply` | ни один член с таким именем не объявлен в области |

Достижимость от production async-входа проверяется тем же обходом: путь до публичного
синхронного входа не найден, обход завершён без неразрешённых вызовов и без ошибок входа, а
записанное ограничение ровно одно. После реализации поиска по присваиваниям (§3.1, пункт 2) обход
проходит дальше: локальная переменная `boundarySeam` (`Services/SolutionManager.cs:825`,
`var boundarySeam = AfterPhysicalLoadBeforePrepareAsync;`) получает значение из свойства-шва
`AfterPhysicalLoadBeforePrepareAsync` (`Services/SolutionManager.cs:129`), а у самого свойства
присваиваний и инициализатора внутри production-области нет — его выставляют
`RoslynMcpServer.LifecycleTestHost` и тестовый проект, которые в область не входят. Поэтому текст
ограничения теперь называет оба члена: «`AfterPhysicalLoadBeforePrepareAsync`, reached from the
invoked delegate `boundarySeam` at `…Services/SolutionManager.cs:825`, has no assignment inside the
declared scope; targets registered outside the scope are not followed». Вердикт не изменился —
по-прежнему одно ограничение, ноль диагностик, путь до sync-входа не найден, — но он стал точнее:
раньше текст утверждал, что присваивания нет у `boundarySeam`, хотя присваивание ему в области есть.
Это закреплено фактом `Production_reachability_records_the_test_seam_as_the_single_limit`
(ровно одно ограничение, оба имени в тексте, `Diagnostics` пуст, `IsDecided == false`).

Третьим ревью production-обход перепроверен на правке явной формы вызова делегата (§3.2, пункт 1) и
на новой семантике `IsDecided`. Числа на кэшированном анализе: 140 файлов, 0 ошибок входа, путь не
найден, диагностик 0, ограничение ровно одно — тот же текст шва, — а `IsDecided` стало `false`
(раньше обход с лимитом назывался решённым). Число обойдённых методов при перезамере — 404
(прежние 286 — замер сравнения до реализации разбора делегатов, §2.2 решения), время шага 311 мс.
Другие явные формы вызова делегата в production-коде лимитов не добавляют: `PublishMainOnly` из
`Services/AnalyzerShadowGenerationPublisher.cs` достижим от async-входа, а его три тестовых хука
(`AfterRequiredCopyBeforeManifest` в строке 116, `BeforeMove` в 166, `MutateSourceAfterFirstHash`
в 373) вызываются через `?.Invoke(...)` и очищаются присваиванием `null` в `ResetTestHooks` внутри
области, поэтому целей не дают — обход по-прежнему заканчивается одним ограничением. Отрицательная
сторона этой формы закреплена фактом `Cleared_delegate_keeps_success_for_both_invocation_forms`
(§3.2).

Следующим ревью production-обход перепроверен на правке вычисляемого получателя (§3.3): числа те же —
140 файлов, 0 ошибок входа, путь не найден, диагностик 0, ограничение ровно одно и того же вида
`TargetOutsideTheScope` с тем же текстом шва, `IsDecided == false`. Лимита нового вида
(`DelegateReceiverNotInScope`) обход не получил: единственная форма «вызов значения» в
production-коде — `descriptor.CreateFactory()(_services)` в `Hosting/McpToolActivationService.cs` —
лежит вне обхода от async-входа. `VerifySanitizedEntries` на `Services/SolutionManager.cs`
возвращает 0 нарушений. Факт `Production_reachability_records_the_test_seam_as_the_single_limit`
дополнен проверкой вида ограничения, поэтому смена вида или появление второго лимита роняет его.

Последним ревью production-обход перепроверен на правке значения делегата (§3.4): вердикт и текст
ограничения те же — 140 файлов, 0 ошибок входа, путь до sync-входа не найден, диагностик 0,
ограничение ровно одно вида `TargetOutsideTheScope` («`AfterPhysicalLoadBeforePrepareAsync`, reached
from the invoked delegate `boundarySeam` at `…\Services\SolutionManager.cs:825`, has no assignment
inside the declared scope; targets registered outside the scope are not followed»),
`IsDecided == false`, `VerifySanitizedEntries` и `VerifyRawPublishedEntry` — 0 нарушений каждое,
обойдено те же 404 метода. Присвоений делегат-членам из вызовов в этом обходе нет: шов берётся из
свойства (`var boundarySeam = AfterPhysicalLoadBeforePrepareAsync;`), а не из вызова, поэтому правка
не изменила ни одного его ребра.

## 5. Пилотная миграция второго контракта

`WorkspaceWriteBoundaryTests.Production_TryApplyChanges_has_single_SolutionManager_call_site` —
имя теста сохранено, тело заменено на `WriteBoundaryChecks.Verify(ProductionAnalysis.Instance,
"RoslynMcpServer.Services", "SolutionManager")`. Область — весь production-код (139 файлов), а не
один файл, как раньше. На реальном коде проверка находит ровно одну ссылку —
`Services/SolutionManager.cs:1384` внутри `TryApplyWorkspaceChanges` — и ни одного объявления
`RevertAnalyzerReferenceOverlayForApply`. Корпус `WriteBoundaryChecksTests` (10 фактов) показывает:
второй сайт вызова — отказ, вызов из другого метода — отказ, отсутствие обёртки — диагностика,
одноимённый метод другого типа (`FakeWorkspace.TryApplyChanges`) — успех (при этом текстовый
предфильтр находит две ссылки, а вердикт выносится по символу), комментарий и строка — успех,
неразрешившаяся ссылка — «счёт неразрешим», объявленный revert-помощник — отказ, CRLF/LF — один
и тот же локализованный вердикт, упоминание без вызова (`nameof` и метод-группа) — отказ в обеих формах
(`Nameof_and_method_group_mentions_stay_fail_closed`); границы этой формы разобраны в §9.

## 6. Рантайм-проверка дедлока

`DeadlockWitnessTests` — два факта, оба зелёные.

| Сценарий | Что проверено | Факт |
|---|---|---|
| Нормальный: корректный async-вход | host получил `ready pid=<pid>`, операция завершилась за отведённую границу, **значение результата** равно 42 (`result:42`), процесс завершился сам с кодом 0 | passed |
| Мутация: async-вход вызывает публичный синхронный вход, уже держа лок | мутация скомпилирована (ошибок компиляции нет), операция **не** завершилась за 5 с, родитель завершил дерево процесса, дождался выхода, процесса с этим pid больше нет, следующая операция в новом host снова вернула `result:42` | passed |

Проверки завершения процессов включены в результат: тест утверждает отсутствие процесса по pid
(`Process.GetProcessById` бросает `ArgumentException`), факт выхода убитого процесса
(`HasExited` после `Kill(entireProcessTree: true)` + ожидание), и то, что последующий прогон
не подвешен. Дерево процесса у host-а состоит из одного процесса: исполнитель — сам host,
а зависание происходит в его потоке.

Ограничения, зафиксированные явно: `Task.WhenAny` после прямого вызова не является защитой
(синхронная часть async-метода зависает до возврата `Task`), `Task.Run` не завершает зависшее
исполнение, поэтому используется отдельный процесс с внешним таймаутом. Мутация — изолированная
копия исходника в временном каталоге, а не переключатель в production API; тот же текст мутации
(`SanitizedEntryDemoSource.AsyncCallsPublicSync`) доказывается статически фактом
`Direct_public_sync_call_from_async_entry_fails`.

## 7. Прогоны

| Команда | Результат |
|---|---|
| `run_dotnet_build RoslynMcpServer.sln` (`-v:minimal --no-incremental`) | успех, 0 ошибок; предупреждения совпадают с baseline: `CS8603` ×2, `CS8601` ×1, `xUnit1030` ×7 — ни одного из новых файлов |
| `FullyQualifiedName~SourceStructure.SanitizedEntryChecksTests` | 74 всего, **74 passed**, 0 failed |
| `FullyQualifiedName~SourceStructure.SourceSetAnalysisTests` | 35 всего, **35 passed** |
| `FullyQualifiedName~SourceStructure.WriteBoundaryChecksTests` | 10 всего, **10 passed** |
| `FullyQualifiedName~SourceStructure.DeadlockWitnessTests` | 2 всего, **2 passed**; 12,5 с настенных часов |
| `FullyQualifiedName~Tests.SourceStructure` | 121 всего, **121 passed** |
| `FullyQualifiedName~WorkspaceAnalyzerSanitizerTests` | 8 всего, **8 passed** (baseline: 7 passed / 1 failed) |
| `FullyQualifiedName~WorkspaceWriteBoundaryTests` | 17 всего, **17 passed** |
| `Category!=AnalyzerLifecycle` | **776 всего, 776 passed, 0 failed, 0 skipped**. Итоговый прогон выполнен на CRLF-рабочей копии (см. §2), то есть в разметке, которую получает CI |
| без фильтра | 789 всего, 776 passed, 0 failed, **13 skipped** — это ровно 13 тестов `AnalyzerLifecycle`, пропускаемых без `ROSLYN_MCP_ANALYZER_LIFECYCLE`; unit-фильтр их исключает |
| `Category=AnalyzerLifecycle` | 13 собрано, 13 skipped (переменная окружения не выставлена); lifecycle-прогон не выполнялся |

Три факта добавлены при ревью эпохи: `Lock_wait_inside_the_protected_try_fails` (корпус контракта),
`Nameof_and_method_group_mentions_stay_fail_closed` (корпус границы записи) и
`Uncalled_private_method_body_is_not_executed` (корпус контракта, разбор дефектов — §3); двенадцать
фактов добавлены вторым ревью (восемь в корпусе механизма — завершение разбора делегатов, поиск по
присваиваниям и граница обхода; четыре в корпусе контракта — async-вход и делегат-присваивание,
разбор — §3.1); девять — третьим ревью (восемь в корпусе контракта — формы вызова делегата
`Delegate_registered_in_another_method_is_followed`,
`Explicit_delegate_invoke_registered_in_another_method_fails`,
`Conditional_explicit_delegate_invoke_registered_in_another_method_fails`,
`Null_forgiving_this_qualified_explicit_delegate_invoke_fails`,
`Legacy_begin_and_end_invoke_forms_are_followed`,
`Cleared_delegate_keeps_success_for_both_invocation_forms`, `Task_wait_any_in_sync_entry_fails` и
`Configured_awaiter_get_result_fails`; один в корпусе механизма —
`Explicit_delegate_invocation_without_a_target_in_the_scope_is_a_limit`; разбор — §3.2); девять —
следующим ревью (семь в корпусе механизма — вычисляемый получатель в четырёх формах, метод-группа с
члена-вызова делегата как контроль и как лимит, формы `DynamicInvoke`, контроль рефлексии; два в
корпусе контракта — отказ на вызов через вычисляемого получателя в явной и короткой формах; разбор —
§3.3); десять — последним ревью (семь в корпусе механизма — значение делегата, полученное вызовом:
выражение-тело, блочное тело, локальная функция, поле, явная форма `Invoke` и `!`, контроль с целью
вне области и лимит на вычисленном значении; три в корпусе контракта — отказ с путём в короткой и
явной формах и отказ «reachability is undecided» на вычисленном значении; разбор — §3.4). Числа
фактов выше — уже с ними.

Прогоны повторялись, и полный набор устойчиво зелёным не бывает: в наборе есть прежние гонки
параллельного исполнения, не связанные с `SourceStructure`. `UtilityToolsSearchCodeTests.SearchCode_without_directory_uses_loaded_workspace_directory`
меняет `Environment.CurrentDirectory` процесса, и тесты, читающие конфигурацию от текущего каталога,
в этом окне падают (`DirectoryNotFoundException` либо `Assert.True`). В `DiagnosticReportStoreTests.cs`
два класса (`DiagnosticReportStoreTests`, `DiagnosticReportAttachmentTests`) делят один статический
`DiagnosticReportStore` и оба сбрасывают его в конструкторе и `Dispose`, а `[Collection]` у них нет;
сброс одного класса уносит курсор другого — так падает
`DiagnosticReportStoreTests.Over_cap_report_is_prefix_plus_explicit_marker` (строка 97). Та же схема в
`NavigationOverflowStoreTests.cs`: `NavigationListingHelperTests` кладёт и тут же забирает курсор
`NavigationOverflowStore`, а параллельный класс `NavigationOverflowStoreTests` сбрасывает это
хранилище. Наблюдённые в повторных прогонах падения:
`UtilityToolsSearchCodeTests.SearchCode_without_directory_uses_loaded_workspace_directory`,
`NavigationOverflowStoreTests.TryStore_then_TryTakeChunk_returns_tail_and_exhaustion_errors`,
`NavigationListingHelperTests.AppendCappedLines_over_maxResults_mentions_cursor_and_incomplete`,
`DiagnosticReportStoreTests.Over_cap_report_is_prefix_plus_explicit_marker`; каждое из них зелёное в
других прогонах. Итог: unit-фильтр — 776 из 776 (в прежнем прогоне на 736 фактах наблюдалось
735 из 736 из-за перечисленных гонок), без фильтра — 789 всего / 776 passed / 0 failed / 13 skipped.
Факты `SourceStructure` ни статических хранилищ, ни текущего каталога не делят, поэтому к ним это
не относится. Следующим ревью та же гонка наблюдена ещё раз: в одном прогоне без фильтра упал
`DiagnosticReportStoreTests.Over_cap_report_is_prefix_plus_explicit_marker` (765 passed / 1 failed),
в повторном прогоне без фильтра и в одиночном прогоне класса он зелёный (15 из 15).

Сходимость с baseline: 789 − 96 новых фактов = 693 (baseline родителя), а
674 + 96 (новые) + 6 (прежние падения, теперь зелёные) = 776. Все пять фактов эпохи 1, падавших
на CRLF (`Missing_lock_wait_fails`, `Missing_lock_release_fails`, `Release_outside_finally_fails`,
`Snapshot_read_before_lock_fails` и переименованный partial-факт
`Partial_type_parts_are_merged_into_one_target`), теперь проходят; шестое прежнее
падение (`CliProgressTests.Test_orchestration_without_pre_test_reports_only_dotnet_test`) проходит
и в одиночном прогоне, и в полном наборе — исправлений в него не вносилось.

## 8. Стоимость

Замеры на 140 файлах (139 production + синтетический global usings), 257 metadata references:

| Что | Значение |
|---|---|
| Сборка области (чтение файлов, создание ссылок) | 57 мс |
| Разбор деревьев / создание компиляции / `GetDiagnostics()` | 237 / 7 / 768 мс |
| Первый прогон анализа (создание с нуля) | ≈ 1,03 с |
| Повторное создание анализа с нуля | 531 мс |
| Повторный `GetDiagnostics()` на том же объекте | 32 мс |
| Проверка sanitized-входов на кэшированном анализе | 98 мс |
| Проверка raw-входа | 1 мс |
| Проверка границы записи | 73 мс |
| Достижимость от async-входа (404 метода обойдено; перезамер третьего ревью — прежние 286 сняты до реализации разбора делегатов, §2.2 решения; последним ревью перезамерено то же число 404) | 311 мс |
| Поиск ссылок по имени (`TryApplyChanges` / `GetSanitizedPublishedSolution`) | 43–48 / 61–72 мс |
| Анализ демо-источника корпуса | 3–25 мс |
| Разбор делегатов: член без присваиваний / граница 256 членов | единицы миллисекунд на демо-источнике (замер пробником корпуса, не перезамерялся) |
| Рантайм-проверка: 2 факта, включая намеренные 5 с ожидания таймаута | 12,5 с настенных часов |
| Unit-набор целиком (776 фактов, `--no-build`) | 26,7 с настенных часов (замер прежнего состава на 733 фактах; на 776 фактах не перезамерялся) |

Числа строк «Сборка области», «Разбор деревьев», «Первый прогон», «Проверка sanitized-входов»,
«Проверка raw-входа» и «Проверка границы записи» — замеры сравнения вариантов; третьим ревью
перезамерялись только достижимость (обход стал длиннее вместе с разбором делегатов) и числа набора
(§7), следующим ревью — только числа набора (§3.3, §7): правка вычисляемого получателя добавляет
разбору одну проверку на вызов и не меняет ни множества обойдённых членов, ни границы обхода.
Последним ревью (§3.4) перезамерено только число обойдённых методов production-обхода: оно осталось
404, потому что значение делегата, полученное вызовом, в этом обходе не встречается; числа набора
обновлены в §7.

Кэширование: `ProductionAnalysis.Instance` — `Lazy` на процесс тестов, поэтому production-компиляция
строится один раз на весь набор и переиспользуется всеми проверками. MSBuild-загрузка проекта в
unit-набор не добавлена: единственный внешний артефакт — `obj/project.assets.json`, который
появляется от обычного restore.

## 9. Незакрытое, отложенное и честные ограничения

- **CI не запускался.** Проверка в окружении `windows-latest` остаётся за CI. Локально итоговый
  прогон сделан на CRLF-рабочей копии (§2) — той же разметке, которую даёт checkout на
  `windows-latest`, — поэтому вердикт проверен в этом отношении; сам CI-прогон этой эпохой не
  выполнялся. Дополнительно механизм нормализации закреплён четырьмя фактами «CRLF против LF»
  в корпусах: каждый берёт обе стороны из явно построенного текста (CRLF из `SourceFile.Normalize`,
  LF обратно через `SourceFile.Create`), сравнивает локализованные наборы нарушений и требует
  одинакового отказа. При отключении нормализации краснеют все четыре (проверено пробой) — то есть
  факты не могут остаться зелёными «за компанию» с разметкой рабочей копии.
- **Новых пропусков нет**: под unit-фильтром 0 skipped. 13 пропусков вне фильтра — это
  `AnalyzerLifecycle`-тесты без обязательной переменной окружения; skip не выдаётся за проверку.
- `Epoch1SemanticInventoryTests` (инвентарь семантических входов) — **отложен в эпоху 3**, как и
  написано в плане; в этой эпохе он не мигрирован и не изменялся.
- README серии, §5 «Известное состояние после эпохи 1», всё ещё утверждает, что корпус
  `SanitizedEntryChecksTests` красный в CI на пяти фактах. После этой эпохи утверждение устарело.
  Строка состояния README и статус плана эпохи 2 обновляются после ревью родителя — таков порядок
  серии; таблица §5 в эту правку не входит и оставлена как историческая запись.
- Рантайм-проверка относится к **зеркальному** демо-контракту, а не к `Services/SolutionManager.cs`
  напрямую: production-класс требует workspace, файловой системы и реального лока, поэтому его
  дедлок в unit-наборе не воспроизводится. Статическая проверка при этом смотрит именно на
  production-файл.
- Не проверяются: поток управления, переопределения виртуальных и интерфейсных членов, рефлексия.
  Ни одна из этих конструкций не записывается лимитом, то есть обход через них остаётся решённым:
  ребро идёт к статически связанному члену (`MethodInfo.Invoke`/`MethodBase.Invoke` — обычное ребро
  класса, не делегата), и цель, достигнутая в рантайме, не показывается. Это записанное ограничение
  анализа, а не умолчание о делегате: вызов делегата лимитом становится (см. ниже), а рефлексия и
  виртуальная диспетчеризация — нет. Проверка не обещает отсутствия дедлоков.
- **Ограничение обхода (`Limits`) — не нарушение само по себе и не доказательство.** Видов два, и
  они различаются значением `ReachabilityLimitKind`, а не текстом (§3.3, §3.4):
  `TargetOutsideTheScope` — вызванный делегат-член области не имеет присваивания в области, то есть
  цель зарегистрирована вне её. Такое ограничение **не** становится нарушением контракта: иначе
  тестовый шов `boundarySeam`, выставляемый вне области, делал бы production-проверку красной.
  `DelegateReceiverNotInScope` — сам сайт вызова делегата не разобран (вычисляемый получатель) либо
  значение делегата, полученное вызовом, разрешить не удалось; такое ограничение
  `SanitizedEntryChecks` сообщает как нарушение
  «reachability is undecided — <текст>», потому что этот сайт не видит ни одно другое правило
  проверки. В обоих случаях вердикт обхода читается как «не показано», а не «нет»: при непустых
  `Limits` `IsDecided` ложно. Production-обход сейчас — одно ограничение первого вида (шов), ноль
  диагностик, `IsDecided == false`, 0 нарушений контракта (§3.3, §3.4).
- **Делегаты: что именно гарантирует разбор.** Цели ищутся по присваиваниям и инициализаторам
  внутри области и берутся из всей области независимо от порядка операторов, условий и
  достижимости ветки: `if (false) _callback = Target;` тоже даёт цель. Это over-приближение в
  сторону отказа — цель считается возможной, а не доказанной для конкретного запуска, — и оно
  закреплено фактами `Delegate_assignment_to_a_method_group_is_followed` и
  `Delegate_assignment_to_a_lambda_body_is_followed` (лямбда назначена в методе, который обход не
  исполняет). Все формы вызова делегата разбираются одинаково: короткая `_seam()`, явные
  `_seam.Invoke()`, `_seam?.Invoke()`, `_seam!.Invoke()`, `BeginInvoke`/`EndInvoke`,
  `DynamicInvoke` (опознаётся по объявляющему типу `System.Delegate`) и метод-группа с
  члена-вызова другого делегата. Получатель берётся по символу и обязан быть делегат-членом
  области, а связанный член — принадлежать типу-делегату (или `System.Delegate` для
  `DynamicInvoke`), поэтому `MethodInfo.Invoke(...)` обычным ребром и остаётся.
  Получатель, вычисленный выражением (`GetSeam().Invoke()`, `GetSeam()()`, `_seams[0].Invoke()`,
  `_seams[0]()`), делегат-членом не является: вызов записывается **лимитом**
  `DelegateReceiverNotInScope` с текстом, называющим конструкцию и её сайт, а не ребром на
  метаданный `Invoke`. До правки §3.3 такая форма молча давала решённое «не достигнуто»; теперь она
  закреплена четырьмя фактами механизма, а на контракте — отказом
  (`Delegate_invocation_through_a_computed_receiver_is_an_undecidable_refusal`,
  `Bare_delegate_invocation_of_a_computed_receiver_is_an_undecidable_refusal`).
  **Значение, полученное вызовом**, разбирается на один вызов вглубь (§3.4): обход читает
  `return`-выражения вызванного члена области (или его тело-выражение) и классифицирует их так же,
  как присваивание, — возвращённый делегат-член входит в тот же разбор с той же границей,
  возвращённая метод-группа становится ребром, возвращённая лямбда — исполняемой областью. Дальше
  этого обход не идёт: результат другого вызова, условие, доступ к элементу и член вне области
  записываются лимитом `DelegateReceiverNotInScope`, то есть «не показано», а не «нет»; на контракте
  это отказ «reachability is undecided». Граница «один вызов вглубь» — записанное ограничение
  анализа: она закреплена фактами `Delegate_value_computed_by_an_accessor_is_a_limit` (механизм) и
  `Delegate_value_computed_by_an_accessor_is_an_undecidable_refusal` (контракт), а прослеженные формы —
  пятью положительными фактами механизма, контролем
  `Delegate_value_returned_by_an_accessor_without_a_scope_target_is_a_limit` и двумя контрактными
  фактами с путём.
  Разбор делегатов ограничен `MaxVisitedDelegateMembers` = 256: при большем графе
  обход объявляется неразрешимым (`Delegate_chain_longer_than_the_bound_is_an_undecidable_walk`),
  а не «не достигнутым». `null` и `default` в присваивании — легальная очистка делегата без
  диагностики и без лимита; это закреплено фактом `Cleared_delegate_keeps_success_for_both_invocation_forms`
  и подтверждено production-обходом, который проходит через три `?.Invoke(...)`-хука
  `AnalyzerShadowGenerationPublisher` и всё равно заканчивается одним ограничением. Присваивание через
  другой экземпляр (`other._callback = Target;`) связывается с тем
  же символом поля и тоже даёт цель — как и раньше, это over-приближение, а не различение
  экземпляров.
- **Граница записи и упоминания без вызова.** `WriteBoundaryChecks` решает по `Symbol` и не смотрит
  `CandidateSymbols`: `nameof(workspace.TryApplyChanges)` не связывается (`CandidateReason.MemberGroup`,
  `Symbol == null`) и сообщается как «ссылка не связывается, счёт неразрешим» — fail-closed ложное
  срабатывание на упоминании, которое нельзя вызвать. В production-коде такой формы нет (проверка
  находит там ровно одну ссылку), поэтому ограничение латентно. Метод-группа
  (`Func<Solution, bool> apply = workspace.TryApplyChanges`, в том числе с явным приведением)
  связывается и считается сайтом ссылки, то есть тоже даёт отказ: делегат может быть вызван позже.
  Обе формы закреплены фактом `Nameof_and_method_group_mentions_stay_fail_closed`; проверка не
  ослаблена.
- Тела лямбд обходятся как исполняемые (over-приближение в сторону отказа); невызванная локальная
  функция, наоборот, не считается исполненной — обе границы закреплены фактами корпуса. Локальная
  функция в этих фактах объявлена именно локальной функцией внутри тела async-входа: удаление
  поддержки `LocalFunctionStatementSyntax` в обходе роняет `Called_local_function_body_is_analyzed`
  (проверено пробой), а невызванная пара остаётся зелёной — это и есть положительная и отрицательная
  стороны требования.
- Проверяется рабочая копия, а не собранная сборка: расхождение между исходником и бинарником
  эта проверка не обнаружит.

## 10. Что рекомендуется эпохе 3

1. Мигрировать оставшиеся проверки §1.5 README (`Epoch1SemanticInventoryTests`) на этот API:
   единица учёта — вызывающий метод + целевой символ, а не регулярное выражение по файлу.
2. Переиспользовать `SourceSetAnalysis`/`MethodBodyScope`/`ReachabilityAnalysis` как публичные
   точки входа; новые структурные проверки писать только через них, а не через текст исходников.
3. Взять корпус эпохи 2 как основу для проверок защиты: детектор новых текстовых ассертов должен
   блокировать `IndexOf`/`Contains`/`Regex` по production-исходникам, но не сами эти проверки.
4. Сохранить правило «диагностика вместо зелёного»: ошибка компиляции входа, неразрешённый символ
   и неподдерживаемая конструкция обязаны быть падением, а не пропуском. Для обхода это значит:
   новый вид «не прослежено» обязан быть либо диагностикой, либо ограничением, а проверка на его
   основе — решать по виду ограничения (`ReachabilityLimitKind`), а не по тексту: вид
   `DelegateReceiverNotInScope` отказ, вид `TargetOutsideTheScope` — «не показано» (§3.3).
5. Учесть предел стоимости: одна компиляция production-дерева на процесс и поиск ссылок по
   предфильтру имени; полный обход всех вызовов дерева (1,9 с) без необходимости не выполнять.

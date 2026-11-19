# MVP/task-01 — отчёт о реализации

- Дата реализации: **2026-11-16** (Europe/Moscow).
- Статус: **принято 2026-11-17; исправления двух дефектов и Windows-validation завершены 2026-11-18**.
  Актуальная validation под SDK 10: Release-сборка, IL 106/106, SourceStructure 123/123, полный main suite
  2116/2116; [исправления, scope и доказательства](evidence/task-01-fixes-sdk10-2026-11-18.md).
  Далее — история исходной реализации и приёмки. Замечания
  раунда ревью 1 обработаны, см. [раздел ниже](#раунд-ревью-1);
  раунд ревью 2 — ACCEPTED, его minor-замечание R2-001 закрыто
  дополнением координатора в отчёте. Финальная validation координатора — Release-сборка и полный main suite
  2099 из 2099 (прогон). Проверку
  «чтение не держит файл» на Windows тогда отложили по решению владельца; отсрочка закрыта прогоном 2026-11-18.
- Scope: внутренний сервис чтения метаданных и IL и графа зависимостей типов верхнего уровня с тремя
  ступенями. Инструмент `estimate_affected_tests`, его ответ, публикация workspace, версия сервера и CHANGELOG
  не менялись; новый код из продукта никем не вызывается.
- Исполнитель: Claude Fable 5.2 (`claude-fable-5-2`), сабагент координатора.
- Спайк `spikes/TestImpactIlProbe`, `forks/`, файл задачи, план и решения владельца не менялись.

## Реализация

Код — в [`Services/Testing/Il`](../../../Services/Testing/Il) (логика, 9 файлов) и
[`Services/Testing/Il/Models`](../../../Services/Testing/Il/Models) (данные, 16 файлов). Новых зависимостей
NuGet нет: `System.Reflection.Metadata` приходит из общей платформы `Microsoft.NETCore.App`.

Путь данных — четыре шага, каждый со своим значением на выходе:

| Шаг | Вход | Вызов | Выход |
| --- | --- | --- | --- |
| Образ файла | путь | `IlAssemblyImageLoader.Load` | `IlAssemblyImage` (путь и байты) либо причина |
| Чтение сборки | образ | `IlAssemblyReader.Read` (или `ReadFile` по пути) | `IlAssemblyReadResult`: `IlAssemblyFacts` либо причина |
| Карта соседства | проекты: путь и признак тестового | `IlNeighborMapBuilder.Build` | `IlNeighborMap` |
| Граф и ответы | значения чтения и карта | `IlDependencyGraphBuilder.Build` (или `BuildFromFiles` по путям) | `IlGraphBuildResult`: граф, `IlTestClosures`, непрочитанные сборки |

- **Три входа.** Проект описан `IlAssemblyInput(OutputFilePath, IsTestProject)`; карта соседства — отдельное
  значение `IlNeighborMap`. `BuildFromFiles(inputs, neighbors)` читает сборки и строит граф.
- **Чтение отделено от графа.** `IlAssemblyReadResult` не ссылается ни на файл, ни на образ.
  `IlDependencyGraphBuilder.Build(reads, neighbors)` файлов не открывает: task-03 может хранить значения,
  перечитать одну сборку и построить граф заново из остальных.
- **Как читается файл.** `IlAssemblyImageLoader` открывает файл с
  `FileShare.ReadWrite | FileShare.Delete`, читает его в память до конца и закрывает; `PEReader` работает по
  буферу. `File.OpenRead` в продукте нет. Параметры открытия собраны в одном месте,
  `IlAssemblyImageLoader.CreateOpenOptions()`, и это единственное место кода `Services/Testing/Il`, которое
  открывает файл (после ревью, см. [раунд 1](#раунд-ревью-1)).
- **Один буфер на граф и отпечатки.** Образ — самостоятельное значение `IlAssemblyImage` с
  `ImmutableArray<byte> Content`. Вызывающая сторона (task-03) загружает образ один раз, отдаёт его
  `IlAssemblyReader.Read` и функции отпечатков task-02 и отпускает. Ни факты сборки, ни граф образ не
  удерживают. `ReadFile` держит образ только на время разбора одной сборки; `BuildFromFiles` читает
  параллельно и держит по одному образу на поток чтения.
- **Узлы и рёбра** перенесены из `Reading/AssemblyReader.cs`, `SignatureTypes.cs`, `AttributeTypeNames.cs`,
  `OperandSizes.cs` спайка без изменения правил: базовые типы и интерфейсы, сигнатуры полей и методов,
  ограничения обобщений, атрибуты и их `typeof`, локальные переменные, блоки `catch`, токены типа, метода и
  поля в IL; вложенные типы, лямбды и автоматы относятся к внешнему типу.
- **Три ступени** — `IlDependencyStage`: `References`, `Discovery`, `DiscoveryWithLoads`. Обход перенесён из
  `Graph/TestClosures.cs`; ветки слоёв `wholeAssembly`, `referencesWithLoads`, `discoveryWithNamedLoads` и
  объяснение пути (`Explain`) не перенесены. Замыкания ступени считаются при первом обращении к ней.
- **Ответы** — `IlTestClosures`: `TestClasses`, `ReachedTypes(тест, ступень)`, `Reaches(тест, тип, ступень)`,
  `AffectedTestClasses(типы, ступень)`. Типы называются парой «сборка, полное имя» (`IlTypeName`); типа,
  которого в графе нет, запрос не роняет.
- **Тестовый класс** — как в спайке: неабстрактный необобщённый тип с методами под атрибутом xUnit, NUnit или
  MSTest (по имени, включая атрибуты solution, унаследованные от них), с унаследованными тестовыми методами.
- **Карта соседства (M-06).** `IlNeighborMapBuilder.Build` для каждого тестового проекта один раз перечисляет
  файлы каталога его выходной сборки, без подкаталогов, и оставляет проекты solution, имя выходного файла
  которых там есть. Сосед назван выходным путём своего проекта; копии не открываются. Сборки не из входов в
  карту не попадают. Каталоги других проектов не просматриваются вообще.
- **Целевые платформы.** В граф входят только переданные сборки. Значения с одним именем сборки объединяются в
  один узел сборки и один узел на тип; зависимость в любой из них считается, соседи всех выходных каталогов
  тестового проекта объединяются.
- **Отсутствующая и нечитаемая сборка** возвращается в `IlGraphBuildResult.Unread` с причиной
  (`Missing`, `Inaccessible`, `NotAnAssembly`) и текстом; граф строится по остальным.
- **PDB не читается.** Не перенесены: `DocumentPaths`, файлы типов, отбор сборок по PDB, `AssemblyFiles`
  (обход каталогов), уровень 0 по ссылкам сборок, счётчик `OpaqueLoads`, MVID, покрытие, `recall`, отчёты.

## TDD

1. Написаны модели, пять классов логики с `NotImplementedException` и все тесты с фикстурами.
2. Красный прогон: Release, **69 из 69 упали**, 0 прошли. По TRX во всех 69
   сообщениях — `NotImplementedException` заглушек (в 6 случаях внутри `Assert.All`); ошибок компиляции и
   фикстур нет.
3. Реализация, затем рефакторинг.
4. Зелёный прогон: Release, **71 из 71 прошли**.
5. После раунда ревью 1 добавлены тесты; итоговый зелёный прогон: Release,
   **97 из 97 прошли**.

После красного прогона ни один тест не ослаблен и не удалён. До ревью изменён один тест и добавлены два
(что изменилось по ревью — в [разделе о раунде 1](#раунд-ревью-1)):

- `IlAssemblyFileTests.Another_thread_overwrites_and_deletes_the_file_while_the_read_is_stopped_inside_it` —
  исправлена ошибка самого теста: сообщение проверки всегда ждало конца чтения, которое тест держал на точке
  остановки. Проверки прежние.
- Добавлены `IlNeighborMapTests.Projects_built_into_one_directory_are_neighbors_of_every_test_project_there`
  и `IlAssemblyFileTests.Empty_output_path_is_an_unread_assembly_and_not_an_exception`.

## Проверки из задачи

Тесты — в [`RoslynMcpServer.Tests/Testing/Il`](../../../RoslynMcpServer.Tests/Testing/Il). Исходники случаев
(`IlFixtureSources`) компилируются в тесте через Roslyn во временный каталог (`IlFixtureCompiler`, поверх
существующего `MetadataReferenceSets.Runtime()`), без PDB; от выхода сборки репозитория тесты не зависят.
Случаи взяты из тестов и `Fixtures` спайка.

| Проверка из задачи | Тест |
| --- | --- |
| Рёбра: базовый тип и интерфейс | `IlDependencyEdgeTests.Base_type_and_interface_are_dependencies` |
| Рёбра: сигнатура | `…Types_of_a_field_and_of_a_method_signature_are_dependencies` |
| Рёбра: атрибут и его `typeof` | `…Attribute_and_its_typeof_argument_are_dependencies` |
| Рёбра: локальная переменная | `…Type_of_a_local_variable_is_a_dependency` |
| Рёбра: токен типа, метода и поля в IL | `…Type_method_and_field_tokens_in_a_method_body_are_dependencies` (3 строки) |
| Рёбра: лямбда, автомат `async` | `…Lambda_and_async_state_machine_bodies_belong_to_the_declaring_type`, `…Code_of_a_nested_type_belongs_to_the_outermost_type` |
| Рёбра: `switch` по типу | `…Type_named_in_a_switch_by_type_is_a_dependency`; случай спайка с инструкцией IL `switch` — `…Reference_after_the_switch_instruction_is_read` (тест проверяет, что инструкция в скомпилированном методе есть) |
| Рёбра: константа и `nameof` ребра не дают | `…Constant_and_nameof_leave_no_dependency` |
| Рёбра, сверх списка задачи (есть в README спайка) | `…Generic_constraint_and_catch_clause_are_dependencies`, `…Consumer_depends_on_the_contract_and_not_on_the_implementation`, `…Generic_registration_names_the_implementation` |
| `references`: доходит по цепочке, не доходит до несвязанного | `IlDependencyStageTests.References_reach_a_type_through_a_chain_of_edges_and_not_an_unrelated_type`, `…Explicit_registration_reaches_the_implementation_by_references`, `…Test_without_a_container_does_not_reach_the_implementation_at_any_stage` |
| `discovery`: `typeof(X).Assembly` и `FromAssemblyOf<T>()` дают реализации достигнутого типа | `…Assembly_handed_to_reflection_gives_implementations_of_a_reached_type_only_with_discovery` (2 строки) |
| `discovery`: сканируется сборка названного типа `X`, а не сборка вызывающего кода | `…Reflection_is_over_the_assembly_of_the_named_type_and_not_of_the_calling_code` (2 строки: `typeof(X).Assembly` и `FromAssemblyOf<T>()` из другой сборки) |
| `discovery`: реализация достигнутого типа при обоих порядках обхода | `…Implementation_is_discovered_when_the_assembly_is_scanned_before_its_contract_is_reached`, `…Implementation_is_discovered_when_its_contract_is_reached_before_the_assembly_is_scanned` |
| `discovery`: типы, обращённые к фреймворку | `…Assembly_handed_to_reflection_gives_types_that_face_a_framework` (2 строки) |
| `discovery`: тип той же сборки без признака не даёт | `…Type_of_a_scanned_assembly_without_a_mark_is_not_discovered` (2 строки) |
| `discovery`: `Assembly.GetEntryAssembly()` не считается | `…Entry_assembly_is_neither_reflection_over_an_assembly_nor_a_load_by_name` |
| `discoveryWithLoads`: `Assembly.Load*`, `AssemblyLoadContext.Load*`, `AppDomain.GetAssemblies` | `…Test_that_reaches_a_load_by_name_depends_on_every_type_of_the_assemblies_next_to_its_test_assembly` (3 строки) |
| `discoveryWithLoads`: класс без такого вызова сохраняет ответ `discovery` | `…Test_that_reaches_no_load_keeps_its_discovery_answer` (5 строк), `IlNeighborMapTests.Test_without_a_load_keeps_its_answer_whatever_lies_next_to_the_test_assembly` |
| Карта соседства: копия `Lib.dll` рядом с `Tests.dll` | `IlNeighborMapTests.Copy_of_the_library_next_to_the_test_assembly_makes_the_loading_test_depend_on_its_types` |
| Карта соседства: без копии — не зависит | `…Without_a_copy_next_to_the_test_assembly_the_loading_test_does_not_depend_on_the_library` |
| Карта соседства: сборка не из solution в ответ не входит | `…Assembly_outside_the_solution_next_to_the_test_assembly_is_not_in_the_answer` |
| Карта соседства: каталог третьего проекта ответ не расширяет | `…Directory_of_a_third_project_holding_a_copy_of_the_test_assembly_does_not_widen_the_answer`, `IlDependencyStageTests.Load_by_name_does_not_reach_an_assembly_that_is_not_next_to_the_test_assembly` |
| Карта соседства: без обхода вглубь, копии не читаются, вид карты | `…Copy_in_a_subdirectory_of_the_test_output_is_not_a_neighbor`, `…Copies_next_to_the_test_assembly_are_not_read`, `…Map_names_a_neighbor_by_the_output_path_of_its_own_project_and_has_an_entry_per_test_project`, `…Projects_built_into_one_directory_are_neighbors_of_every_test_project_there`, `…Test_project_whose_output_directory_is_missing_has_no_neighbors` |
| Чтение не держит файл | `IlAssemblyFileTests.Another_thread_overwrites_and_deletes_the_file_while_the_read_is_stopped_inside_it` (поведение; отличает режимы только на Windows); режим открытия без обращения к ОС — `…File_is_opened_for_reading_with_write_and_delete_shared`; файл открывается только загрузчиком и только с этими параметрами — `IlFileAccessChecksTests.Production_il_code_opens_files_only_in_the_image_loader_through_its_open_options` и 20 случаев самой проверки на примерах и контрпримерах; закрытие и один буфер — `…File_is_closed_when_the_image_is_returned`, `…Image_holds_the_whole_file_and_is_parsed_without_the_file` |
| Каждая ступень включает предыдущую, на всех случаях | `IlGraphQueries.AssertEveryStageIncludesThePrevious` по всем тестовым классам графа: `IlDependencyStageTests.Every_stage_includes_the_previous_one` (граф всех случаев рёбер, ступеней и тестовых классов), `IlNeighborMapTests.Every_stage_includes_the_previous_one_in_every_layout` (четыре раскладки), `IlGraphBuildTests.Neighbors_of_every_build_of_a_test_project_are_united` |
| Тестовые классы: xUnit, NUnit, MSTest | `IlTestClassTests.Test_classes_are_the_non_abstract_types_with_test_methods_of_any_supported_framework`, `…Method_under_an_xunit_nunit_or_mstest_attribute_makes_a_test_class` (3 строки), `…Attribute_derived_from_a_test_attribute_in_the_solution_makes_a_test_class` |
| Тестовые классы: абстрактный не тестовый, наследник тестовый | `…Abstract_class_is_not_a_test_class_and_its_heir_counts_the_inherited_test_methods` |
| Тестовый класс не достигается другим тестом через рефлексию | `…Reflection_over_the_test_assembly_does_not_reach_other_test_classes`, `…Load_by_name_does_not_reach_other_test_classes` |
| Отсутствующая и повреждённая сборка | `IlAssemblyFileTests.Missing_and_damaged_assemblies_are_named_with_the_reason_and_the_graph_is_built_from_the_rest`, `…Read_of_a_missing_or_damaged_file_returns_the_failure_instead_of_throwing`, `…Empty_output_path_is_an_unread_assembly_and_not_an_exception`, `IlGraphBuildTests.Failed_read_values_are_returned_as_unread_and_the_graph_is_built_from_the_rest` |
| Граф из прочитанных значений равен графу из тех же файлов | `IlGraphBuildTests.Graph_from_read_values_equals_the_graph_built_by_reading_the_same_files_again`, `…Graph_is_built_from_read_values_after_their_files_are_gone`, `…Replacing_one_read_value_rebuilds_the_graph_without_reading_the_other_files` |
| Целевые платформы: объединение сборок с одним именем | `IlGraphBuildTests.Assemblies_with_one_name_are_merged_into_one_assembly_and_one_node_per_type`, `…Neighbors_of_every_build_of_a_test_project_are_united` |
| Сверка со спайком на сборках репозитория | ручная, [evidence](evidence/task-01-spike-comparison.md) |
| Время и пиковая память на сборках репозитория | ручной замер, [evidence](evidence/task-01-spike-comparison.md#замер-на-сборках-этого-репозитория) |
| SourceStructure, Release build | итоговый зелёный прогон: SourceStructure 123/123, сборка solution 0 ошибок |
| Полный main suite | не запускался исполнителем: его запускает координатор |

Сверх задачи проверено правило спайка «преамбула сборки» (инициализатор модуля, `SetUpFixture`):
`IlTestClassTests.Every_test_class_depends_on_the_module_initializer_and_the_setup_fixture_of_its_assembly`.

## Замеры

Пять выходных сборок проектов solution, Release, новый процесс на запуск (время включает JIT);
[команды и вывод](evidence/task-01-spike-comparison.md#замер-на-сборках-этого-репозитория). Машина: Linux x64,
2 процессора, 3843 МБ.

| Величина | Workstation GC, три запуска | Server GC, один запуск |
| --- | --- | --- |
| Карта соседства | 10–11 мс | 10 мс |
| Чтение пяти сборок и граф (750 типов, 3031 ребро) | 451–510 мс | 468 мс |
| Замыкания 173 тестовых классов, три ступени | 47–50 мс | 29 мс |
| Весь процесс | 0,58–0,63 с | 0,58 с |
| Пик памяти процесса | 72,8–73,4 МБ | 85,3 МБ |

Порога в задаче нет. Число спайка (1,1 с, 81 МБ) включает поиск файлов, чтение PDB и статистику, поэтому это
ориентир, а не сравнение одной и той же работы. Таблица снята до ревью; повтор на итоговой сборке
(Workstation GC, три запуска) дал то же: чтение и граф 446–496 мс, замыкания 43–48 мс, весь процесс
0,57–0,62 с, пик памяти 73,6–73,7 МБ.

## Сверка со спайком

[Как сверялось, команды и вывод](evidence/task-01-spike-comparison.md). Спайку даны выходные каталоги пяти
проектов solution; он прочитал 7 сборок (пять проектов и две сборки форка BuildHost из подкаталога). Продукту
даны те же 7 сборок, совпадение проверено по имени и MVID.

| Ступень продукта и слой спайка | Пар «тестовый класс, тип» | Результат |
| --- | --- | --- |
| 1 и `references` | 14785 | файлы побайтно равны |
| 2 и `discovery` | 16112 | файлы побайтно равны |
| 3 и `discoveryWithLoads` | 47405 | файлы побайтно равны |

- **Расхождений ступени 3 на этом репозитории нет**: копии `RoslynMcpServer.Tests.dll` в чужих каталогах нет.
- Объяснение «только чужие каталоги» проверено отдельно: спайку добавлен искусственный каталог с копиями
  тестовой сборки и `RoslynMcpPlugin.dll`. Ступени 1 и 2 остались равны; спайк дал на 228 пар больше в
  `discoveryWithLoads` — 76 тестовых классов на 3 типа `RoslynMcpPlugin`; пар, которые есть только у продукта,
  нет.
- Числа в таблице сняты до ревью. На итоговой сборке сверка повторена: набор сборок совпадает, три файла
  ступеней побайтно равны (14799, 16126 и 47495 пар — в тестовой сборке стало на один тестовый класс больше).

## Отступления от задачи, толкования и ограничения

Отступлений от текста задачи нет. Ниже — места, где задача оставляла выбор, и что из него следует.

1. **Что делает признак тестового проекта.** Он определяет, для каких проектов строится запись карты
   соседства (чей каталог перечисляется). Тестовый класс от него не зависит и определяется по атрибутам, как
   в спайке: иначе ступени 1 и 2 не совпали бы со слоями спайка. Следствие: тестовый класс в проекте, не
   помеченном тестовым, соседей не имеет, и ступень 3 для него добавляет только типы его собственной сборки.
   Рецензент с толкованием согласился. Его замечание на будущее, вне этой задачи: task-03 должна брать
   признак из того же источника, по которому эпоха 1 считает проект тестовым.
2. **Сосед ищется по имени выходного файла проекта**, как сказано в задаче («файл с именем которых лежит в
   каталоге»). Спайк сопоставлял имя файла с именем сборки из метаданных. Когда имя файла и имя сборки
   различаются, ответы различаются; на этом репозитории такого нет. Имена файлов сравниваются без учёта
   регистра: это может только добавить соседа.
3. **Чтение не держит файл — ограничение Linux.** На Linux .NET держит на открытом файле рекомендательную
   блокировку при любом `FileShare`, кроме `None`. Пока идёт чтение (миллисекунды на сборку), писатель из
   другого .NET-процесса, который просит исключительный доступ (`FileShare.None`), получает `IOException`;
   `File.Copy` с перезаписью — в их числе
   ([проба](evidence/task-01-spike-comparison.md#проба-что-может-другой-писатель-пока-файл-открыт-linux)).
   Режим доступа из задачи на это не влияет. `dotnet build` при удержанной разделяемой блокировке копирует
   сборку в `bin` без ошибки и предупреждения: это проверил рецензент
   (раунд 1, п. 8), исполнитель не повторял; почему копирование MSBuild
   не упирается в блокировку, не исследовалось. Насколько известно исполнителю, снять блокировку можно только
   переключателем `System.IO.DisableFileLocking` на весь процесс или открытием файла мимо `FileStream`; это
   не проверялось и не сделано. По ревью в рамках task-01 делать ничего не нужно; ограничение записано в
   XML-doc `IlAssemblyImageLoader`. Вопрос закрыт, строка в рисках task-03 и task-06 — за координатором.
4. **Чтение не держит файл — Windows, не проверено.** По правилам совместного доступа Windows писатель,
   который сам просит исключительный доступ (`FileShare.None`), получит отказ и при режиме доступа из задачи:
   исключительный доступ несовместим с любым открытым читателем. Режим из задачи пропускает запись с общим
   чтением, удаление и замену файла. Ни это, ни то, как именно пишут `csc` и MSBuild на Windows, не
   запускалось.
5. **Повреждённый образ.** `IlAssemblyReader.Read` превращает в причину `NotAnAssembly` любое исключение
   разбора, кроме нехватки памяти, а не только `BadImageFormatException`: файл, пойманный на середине записи,
   ломает читатель метаданных разными исключениями. Ошибка в самом читателе тоже выглядела бы как нечитаемая
   сборка; текст причины содержит тип исключения.
6. **Тело метода, которое не разобралось**, сборку нечитаемой не делает: как в спайке, оно считается в
   `UnreadableBodies` графа, а его зависимости в граф не попадают. На сборках репозитория таких тел 0.
7. **Порядок узлов** определяется порядком входов; чтение идёт параллельно, но результаты кладутся по месту.
   Ответы отдаются именами типов, а не номерами узлов.
8. Пробелы спайка, которые перенесены как есть: константы и `nameof`, другой процесс, типы по строке,
   `x.GetType().Assembly` как сборка вызывающего кода, обобщённый тестовый класс не считается тестовым.

## Что не запускалось и не проверено при реализации 2026-11-16

Ниже — исходные ограничения прогона исполнителя. Актуальная проверка Windows и исправления 2026-11-18 —
в [дополнительном отчёте](evidence/task-01-fixes-sdk10-2026-11-18.md).

- **Windows.** Ни один тест задачи на Windows не запускался. Проверка «чтение не держит файл» обязательна на
  Windows и не выполнена. Поведенческий тест написан так, чтобы там ловить запрет записи: при открытии через
  `File.OpenRead` перезапись в точке остановки должна дать ошибку доступа (ожидание, не прогон). Категории у
  него нет, поэтому задание `unit` на `windows-latest` в `.github/workflows/test-suite.yml` (фильтр
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`) его выполнит; на Linux он тоже идёт. На Linux
  этот тест проверяет только, что точка остановки лежит внутри окна открытого файла и что перезапись и
  удаление проходят. Возврат к `File.OpenRead` на Linux теперь ловят два теста, не зависящих от ОС (режим в
  параметрах открытия и форма кода, см. раунд 1), но поведение Windows они не заменяют.
- **Полный main suite** и финальная Release-validation — за координатором. Исполнитель запускал: Release-сборку
  solution, 97 тестов задачи, SourceStructure 123/123 и 33 теста с `Inventory` в имени (все прошли).
- **`AnalyzerLifecycle`** не запускался: задача не касается жизненного цикла анализаторов.
- Сверка со спайком и замер сделаны на одном solution, маленьком и почти без DI. На solution владельца
  ничего не запускалось.

## Раунд ревью 1

Отчёт рецензента: CHANGES REQUESTED, три major и два minor. Правила
продукта по замечаниям не менялись; изменены тесты, фикстуры, способ, которым загрузчик задаёт параметры
открытия, и порядок членов в двух моделях.

| Замечание | Что сделано | Чем закрыто |
| --- | --- | --- |
| R1-001, major: `typeof(X).Assembly` и `FromAssemblyOf<T>()` проверены только для `X` из сборки вызывающего кода | В фикстуру добавлены тестовые классы `CrossAssemblyScanTests` и `CrossAssemblyGenericScanTests`: вызовы стоят в `Shop.Tests`, названный тип `OrderConsumer` — в `Shop.Lib` | `IlDependencyStageTests.Reflection_is_over_the_assembly_of_the_named_type_and_not_of_the_calling_code` (2 строки): сканируется ровно `Shop.Lib`; `OrderService` нет на `References` и есть на `Discovery`; обращённый к фреймворку помощник тестовой сборки не достигается |
| R1-002, major: ветка «реализует достигнутый тип» в `Scan` не покрыта | Добавлены два случая, по одному на каждый порядок обхода, и оба не зависят от номеров узлов. `ContractFirstTests` называет только контракт `IPricingPolicy`, а до `PolicyScanner` обход доходит только через сигнатуру этого контракта: сборка сканируется после того, как контракт обработан. `ScannerFirstTests` называет только `AssemblyScanner`, контракт `IOrderService` — в двух шагах за ним: сборка сканируется раньше, чем контракт достигнут | `…Implementation_is_discovered_when_its_contract_is_reached_before_the_assembly_is_scanned` (ветка в `Scan`) и `…Implementation_is_discovered_when_the_assembly_is_scanned_before_its_contract_is_reached` (цикл по подтипам) |
| R1-003, major: проверка «чтение не держит файл» на Windows не выполнена | Прогон на Windows — вопрос координатора, исполнитель его не решал. Сделана защита, не зависящая от ОС. (а) Параметры открытия собраны в `IlAssemblyImageLoader.CreateOpenOptions()`, файл открывается `new FileStream(path, CreateOpenOptions())`. (б) Проверка формы кода `IlFileAccessChecks` на `SourceSetAnalysis`: в `Services/Testing/Il` типы `File`, `FileStream`, `FileInfo`, `StreamReader`, `StreamWriter`, `MemoryMappedFile` используются только в загрузчике; в загрузчике ровно одно открытие, и это `FileStream` с результатом `CreateOpenOptions()` | (а) `IlAssemblyFileTests.File_is_opened_for_reading_with_write_and_delete_shared`; (б) `IlFileAccessChecksTests.Production_il_code_opens_files_only_in_the_image_loader_through_its_open_options` и 20 случаев самой проверки на примерах и контрпримерах в том же классе |
| R1-004, minor: порядок членов | В `Models/IlNeighborMap.cs` и `Models/IlAssemblyImage.cs` свойства стоят перед конструктором | чтение кода |
| R1-005, minor: две пустые строки подряд | Одна убрана в `IlAssemblyFileTests.cs` | чтение кода |
| Пожелание: ограничение Linux в комментарии загрузчика | Добавлено в XML-doc `IlAssemblyImageLoader` и в п. 3 раздела ограничений | — |
| Пожелание: формулировка про `BuildFromFiles` | Исправлено в разделе «Реализация»: по одному образу на поток чтения | — |

Вместе с новыми тестовыми классами расширены точные списки в двух существующих тестах
(`Test_classes_are_the_non_abstract_types_with_test_methods_of_any_supported_framework`: 13 имён → 17;
`Affected_test_classes_are_the_test_classes_that_reach_any_of_the_types`: ступень 2 — 3 класса → 6, ступень
3 — 6 → 9). Это по-прежнему сравнение с точным списком; ни один тест не ослаблен.

### Как проверено, что новые тесты падают при нарушении

Каждая подмена временно вносилась в production-код рабочей копии, после неё запускалась сборка и тесты задачи
(`--filter "FullyQualifiedName~RoslynMcpServer.Tests.Testing.Il"`, 97 случаев), затем файл возвращался из
копии. Скрипт — `$HOME_DIR/tia-mvp/mut/run.py`, вывод — `$HOME_DIR/tia-mvp/mut/result.txt`. После последней
подмены три затронутых файла сверены с копиями побайтно, solution пересобран, и
итоговые прогоны сделаны уже на возвращённом коде.

| Подмена | Упало из 97 | Какие тесты упали |
| --- | --- | --- |
| M12: `typeof(X).Assembly` даёт сборку вызывающего кода (`IlAssemblyReader`, `owner.Scans.Add(_assembly)` вместо сборки названного типа) | 2 | `Reflection_is_over_the_assembly_of_the_named_type_and_not_of_the_calling_code(CrossAssemblyScanTests)`, `Affected_test_classes_are_the_test_classes_that_reach_any_of_the_types` |
| M13: `FromAssemblyOf<T>()` даёт сборку вызывающего кода (`_assembly` вместо `argument.Assembly`) | 2 | `Reflection_is_over_…(CrossAssemblyGenericScanTests)`, `Affected_test_classes_…` |
| M6: в `Scan` убрана проверка «реализует достигнутый тип» | 1 | `Implementation_is_discovered_when_its_contract_is_reached_before_the_assembly_is_scanned` |
| M7: цикл по подтипам в обходе ничего не находит | 6 | `Implementation_is_discovered_when_the_assembly_is_scanned_before_its_contract_is_reached`, обе строки `Assembly_handed_to_reflection_gives_implementations_…`, обе строки `Reflection_is_over_…`, `Affected_test_classes_…` |
| M2: `Share = FileShare.Read` в параметрах открытия | 1 | `File_is_opened_for_reading_with_write_and_delete_shared` |
| S1: загрузчик открывает файл без `CreateOpenOptions()` (`new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`) | 1 | `Production_il_code_opens_files_only_in_the_image_loader_through_its_open_options` |
| S2: `IlAssemblyReader.ReadFile` сам читает файл (`File.ReadAllBytes(path)`) | 5 | `Production_il_code_opens_files_only_…` и четыре теста отсутствующей сборки, которые упали на исключении `File.ReadAllBytes` |

M6 и M7 роняют разные тесты: случай `ContractFirstTests` ловит только ветку в `Scan`, случай
`ScannerFirstTests` — только цикл по подтипам. M2 и S1 закрывают друг друга: первый ловит смену режима в
параметрах, второй — открытие в обход параметров.

### Что из R1-003 сделано и что нет

- Сделано (а) и (б). Для (б) большого нового механизма не понадобилось: проверка — один статический класс
  поверх существующих `SourceSetAnalysis`, `SourceSetScope.Demo` и `ProductionAnalysis`, решение принимается
  по привязке символов, а не по тексту. Она лежит рядом с тестами задачи, не в каталоге `SourceStructure`.
- Граница проверки (б): она видит перечисленные шесть типов. Открытие файла другим API
  (`System.IO.RandomAccess` по готовому описателю, P/Invoke, чужая библиотека) она не заметит;
  `File.OpenHandle` заметит, потому что это член `File`. Не заметит она и метод BCL, который открывает файл
  сам (например, `AssemblyName.GetAssemblyName(path)`), и вызов помощника из другого каталога репозитория,
  который открывает файл: проверяется только код внутри `Services/Testing/Il`, без транзитивных вызовов
  (дополнение координатора по R2-001).
- Категория `RequiresWindows` поведенческому тесту не поставлена: на Linux он не бессмыслен (проверяет точку
  остановки, перезапись и удаление) и идёт на обеих ОС. В CI его выполнит задание `unit` на `windows-latest`;
  CI запускается на push в `main` и на pull request, для ветки `rapid-tia` без PR прогона на Windows не будет.
- На исходной приёмке 2026-11-17 сам прогон Windows был отложен по решению владельца, задача принята с этой
  отсрочкой. Прогон выполнен 2026-11-18 под SDK 10;
  [результаты и закрытие отсрочки](evidence/task-01-fixes-sdk10-2026-11-18.md).

### Прогоны после правок

Release-сборка solution — 0 ошибок; тесты задачи — 97 из 97; SourceStructure — 123 из 123; тесты с
`Inventory` в имени — 33 из 33. Сверка со спайком, замер и проба блокировки повторены на итоговой сборке,
результат прежний ([повтор](evidence/task-01-spike-comparison.md#повтор-после-раунда-ревью-1)).

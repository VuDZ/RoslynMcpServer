# Rapid TIA 2, task-06 — сквозные проверки и замеры

**Принято 2026-11-19**, Europe/Moscow. Задача [task-06](task-06-end-to-end-and-owner-measurement-med.md).
Продукт **1.8.1**, `9bb3c8c`, Release, Windows x64, SDK **10.0.402**. Код продукта и тесты из задач 03–05
с последующими исправлениями. Замеры выполнялись в отдельных копиях исходников.
Независимое ревью приняло матрицу, результаты и границы замеров после сверки с итоговым TRX и логами клиента.

## Сквозные сценарии задач 03–05

| Сценарий | Проверка в коде | Что проверяется |
| --- | --- | --- |
| Правка тела метода | [`Build_precedes_narrowing_and_direct_executes_fewer_classes_than_all_without_rebuilding_twice`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L124) | Реальные MSBuild и VSTest. Ступени `changed/direct/di/all` дают `0/1/2/3`, запуск `direct` выполняет один класс, `all` — три. |
| Правка комментария | [`All_stages_empty_builds_without_starting_a_test_and_explains_why(true)`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L159) | После сборки отпечатки совпадают, ступени пусты, VSTest не запускается, ответ сообщает о неизменившемся коде. |
| Правка константы | [`Const_edit_runs_the_test_whose_compiled_code_contains_the_new_constant`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L213) | На ступени `changed` запускается класс с вписанным компилятором новым значением. |
| Правка теста | [`Edited_test_is_in_the_changed_stage_once_by_epoch_1_and_by_fingerprint`](../../../RoslynMcpServer.Tests/Testing/Narrowing/TestClassStageTests.cs#L141), [`Fresh_direct_test_edit_reports_the_compiled_type_without_widening_the_method_filter`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs#L56) | Ступень 0 учитывает класс один раз. Вызов инструмента сохраняет фильтр конкретного метода для прямой правки и сообщает изменившийся тип после сборки. |
| Удаление типа | [`Removed_type_selects_the_test_that_depended_on_it_before_the_edit`](../../../RoslynMcpServer.Tests/Testing/Narrowing/TestClassStageTests.cs#L164) | Граф до правки сохраняет путь обнаружения удалённого типа. Ступень `direct` пуста, ступень `di` выбирает класс, который перечисляет типы. |
| Снятый интерфейс | [`Removed_interface_selects_the_test_that_scans_for_implementations_at_the_discovery_stage`](../../../RoslynMcpServer.Tests/Testing/Narrowing/TestClassStageTests.cs#L72) | Граф до правки по-прежнему доводит до теста на ступени DI. |
| Правка типа без тестов | [`All_stages_empty_builds_without_starting_a_test_and_explains_why(false)`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L159) | Ответ называет изменённый тип, все ступени пусты, VSTest не запускается. |
| Правка без сборки | [`Build_precedes_narrowing_and_direct_executes_fewer_classes_than_all_without_rebuilding_twice`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L124) | До первого build проект идёт целиком с причиной об устаревшей сборке и предложением собрать. Вызов оценки сам сборку не запускает. |
| Вторая правка без сборки после первой собранной | [`Edit_and_build_narrow_a_second_edit_without_a_build_keeps_the_project_whole_and_a_build_narrows_both`](../../../RoslynMcpServer.Tests/Testing/Narrowing/TestNarrowingBuildTests.cs#L60) | Первый build даёт сужение. Вторая правка снова даёт целый проект, следующий build сужает по обеим правкам. |
| Сборка сразу после загрузки | [`Rebuilding_before_the_first_snapshot_read_runs_the_whole_project_with_the_unknown_baseline_reason`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsDepthTests.cs#L247) | Реальная сборка во время фонового первого чтения завершается. DLL, задержанная до чтения, получает неизвестное состояние до правки. VSTest запускает весь проект. |
| Reload | [`Reset_workspace_cancels_the_unfinished_first_read_and_reload_keeps_the_snapshot`](../../../RoslynMcpServer.Tests/Testing/Snapshots/AssemblySnapshotPublicationTests.cs#L38) | При reload того же solution сохраняются исходная прочитанная запись и неизвестное состояние прерванной записи. Текущая версия прерванной сборки читается. |
| Не-C# вход | [`Project_selected_for_another_reason_than_a_CSharp_edit_stays_whole`](../../../RoslynMcpServer.Tests/Testing/Narrowing/TestNarrowingWholeTests.cs#L195), [`Whole_project_plan_runs_without_a_filter`](../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs#L203) | Для изменённого `data.json` остаётся типизированная причина `NonCSharpInput`. В публичном вызове с правкой не-C# входа запускаются все тесты выбранного проекта. |

Итог прогона на финальном коде — в разделе [Проверка финального кода](#проверка-финального-кода).

## Проверка финального кода

- Release-сборка `RoslynMcpServer.sln` через `run_dotnet_build` прошла.
- Windows main — **2622/2622**, 0 пропусков, 5 мин 25 с. Все тесты таблицы прошли, включая параметры теорий.
  В main вошли все **123** теста SourceStructure.
- AnalyzerLifecycle — **13/13**, 0 пропусков, 53 с. Прогон включён из-за изменений пути workspace после task-03.

Первый запуск внутри песочницы остановился на соединении VSTest с testhost. Прогоны с доступным
локальным соединением прошли. `Assert-NoSkippedTests.ps1` подтвердил счётчики обоих итоговых TRX.

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:NuGetAudit = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build RoslynMcpServer.sln -c Release
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore `
    --filter 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk' --logger 'trx;LogFileName=main.trx' `
    --results-directory artifacts/task-06/validation/main-host
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore `
    --filter 'Category=AnalyzerLifecycle' --logger 'trx;LogFileName=lifecycle.trx' `
    --results-directory artifacts/task-06/validation/lifecycle
```

SHA-256 итоговых TRX относительно `artifacts/task-06/validation`:

```text
main-host/main.trx      4D52AAF4F69A4206D152AD18F77DDA3AB6D4D628E97DAE540E59E492DA408CC8
lifecycle/lifecycle.trx 6B44718B134E2D5D7A115A90BFDC82761B1F94A3B599995902C419EA481588D5
```

## Windows — сборка во время фонового чтения

Отдельный тест [`Assembly_rewritten_while_its_first_read_holds_the_file_stays_unknown_and_the_write_succeeds`](../../../RoslynMcpServer.Tests/Testing/Snapshots/AssemblySnapshotSessionTests.cs#L55)
удерживает реальный читающий дескриптор DLL и проверяет, что запись проходит, а исходная версия остаётся
неизвестной. В сквозном сценарии «Сборка сразу после загрузки» реальный MSBuild выполняется, пока первый
снимок занят чтением и ждёт допуска к целевой DLL. После сборки тест пропускает чтение DLL через этот барьер.
Код Release-сервера вернул `before the edit is unknown: The file was written after the baseline was published`,
запустил проект целиком и получил `Outcome: passed` для всех шести тестов. Оба сценария прошли на Windows
в итоговом main. Сценарии можно повторить с фильтром
`FullyQualifiedName~Rebuilding_before_the_first_snapshot_read|FullyQualifiedName~Assembly_rewritten_while_its_first_read`.

## Замер на этом репозитории

[Подготовка, измерение памяти и подсчёт классов](evidence/task-06-snapshot-measurement.md) используют те же
`AssemblySnapshotSession` и `VstestTestInventory`, что и продукт. Для 5 сборок удерживаемая память одного
снимка — **1,92 МиБ**, двух — **3,85 МиБ**. Всего **203 класса** в одном тестовом проекте.

20 независимых правок тел методов в пяти проектах подготовлены в
[списке случаев](evidence/task-06-self-cases.json). Каждая вставляет один вызов `GC.KeepAlive(null)`,
чтобы менять IL без изменения результата метода. На каждую пробу — новый MCP-процесс и собранное чистое
состояние. Клиент дожидается обеих записей о завершении baseline до сохранения. Время запроса
`estimate_affected_tests` берётся с часов клиента (`perf_counter` перед отправкой — получение полного
ответа MCP). Оно включает ожидание обработки запроса. Сборка и загрузка идут отдельно.
Ступень `all` использует карту соседства M-06 продукта. Часть прогонов пересекалась с другими локальными
сборками и проверками, поэтому времена описывают эту серию на одной машине.

```powershell
$repo = (Get-Location).Path
$taskRoot = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
$copy = Join-Path $taskRoot 'self'
New-Item -ItemType Directory -Path $copy -Force | Out-Null
git archive --format=tar --output="$taskRoot/self.tar" 9bb3c8c
tar -xf "$taskRoot/self.tar" -C $copy
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:NuGetAudit = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build "$copy/RoslynMcpServer.sln" -c Release -m:1
$harness = Join-Path $repo 'docs/rapid-tia-2/mvp/evidence/measure-task06.py'
$cases = Join-Path $repo 'docs/rapid-tia-2/mvp/evidence/task-06-self-cases.json'
$server = Join-Path $repo 'bin/Release/net10.0/RoslynMcpServer.dll'
python $harness $server "$copy/RoslynMcpServer.sln" $cases "$taskRoot/mcp" |
    Tee-Object "$taskRoot/mcp.jsonl"
python $harness $server "$copy/RoslynMcpServer.sln" $cases "$taskRoot/external" --external-save |
    Tee-Object "$taskRoot/external.jsonl"
```

При `update_file_content` первое чтение заняло **1,21 с** в медиане, ответ — **5,74 с**. В 17 пробах
инструмент выдал ступени. Самая широкая выбрала от **1 до 98 из 203** классов. В пробах 16–17 был
прямой фильтр одного метода, для него инструмент не печатает ступени. В пробе 20 уровень 0 не выбрал
тестовый проект.

```text
Проба   Первое чтение, мс   Ответ MCP, мс   changed/direct/di/all   Целый   Ответ при внешней записи, мс
01                   1341          6033,8            0/78/83/93     203                         5316,8
02                   1298          9283,0            0/83/88/98     203                         5638,8
03                   1674          9141,7            0/82/87/97     203                         6038,4
04                   1605          5393,9            0/27/27/89     203                         5200,0
05                   1224          6281,2            0/77/82/92     203                         4337,6
06                   1165          5431,2            0/83/88/97     203                         4347,5
07                   1197          4978,8            0/28/28/89     203                         4473,1
08                   1191          5256,6            0/17/17/88     203                         4897,5
09                   1185          5743,5            0/78/83/93     203                         5075,9
10                   1145          6279,5            0/82/87/97     203                         4630,5
11                   1176          5744,6            0/83/88/98     203                         5168,2
12                   1262          5600,3            0/83/88/97     203                         5327,1
13                   1096          5323,4            0/28/28/89     203                         4751,1
14                   1274          6551,9            0/17/17/88     203                         4725,3
15                   1287          6507,3                1/1/1/1     203                         4948,3
16                   1349          7642,2              по методу     203                         4984,4
17                   1315          6320,9              по методу     203                         5883,9
18                   1160          4333,6               0/0/0/88     203                         4744,8
19                   1155          3940,8               0/0/0/88     203                         3836,2
20                   1127          3532,4                     —       —                         3509,9
```

В серии с внешней записью **17 из 20** правок оставили все 203 класса, причина `not newer than the last edit`.
В двух пробах сохранился прямой фильтр одного метода. В одной уровень 0 не выбрал тестовый проект.
Локальные копии чисел из stdout клиента находятся в `artifacts/task-06/timing/self-mcp.jsonl` и
`artifacts/task-06/timing/self-external-client.jsonl`.

При внешнем сохранении после успешной сборки проверка свежести увидела исходник новее DLL. Это
воспроизвелось в отдельной копии. При первой семантической синхронизации после внешнего сохранения
`MSBuildWorkspace.TryApplyChanges` заново пишет исходник. В пробе 13 DLL имела timestamp
`09:46:01.3005572Z`, исходник после синхронизации — `09:46:02.1584688Z`. Сервер залогировал
`workspace_disk_sync updated=1 … write=FullSuccess` в `09:46:02.169Z`. Отдельная задача
[про timestamp после внешнего сохранения](../follow-up-external-save-build-freshness.md).

## Замер на solution владельца

Для основного замера выбран согласованный владельцем **Jellyfin**. Полный solution успешно собран в Release.
Выполнено **9 независимых правок** тел методов на сервере **1.8.1** 2026-11-19. Первое чтение 41 выхода
заняло **1,50–2,48 с**. В снимке 39 прочитанных сборок, 2497 отпечатков типов и две непрочитанные сборки.
Удерживаемая память одного снимка **4,88 МиБ**, двух **9,82 МиБ**. Полный инвентарь — 300 классов в
17 тестовых проектах. [Память и методика подсчёта](evidence/task-06-snapshot-measurement.md).

Первые шесть правок сделаны внешним сохранением. Последние три сделаны через `update_file_content`.
Классы уровня 0 суммируются в проектах ответа. Знак `—` означает, что инструмент не выдал ступени
для этих проектов и оставил их целиком. Для проб 7–9 указаны ступени только того проекта, который
инструмент смог проанализировать. Остальные 13 проектов содержали 241 класс и остались целиком.
Времена ниже взяты из stdout клиента, локальные копии в `artifacts/task-06/timing/owner-external-client.jsonl`
и `artifacts/task-06/timing/owner-mcp.jsonl`. Серверный лог показывает длительность обработчика RPC
внутри этого интервала. Для дополнительной серии сохранён `artifacts/task-06/timing/owner-alt.jsonl`.

```text
Проба   Чтение, мс   Ответ, мс   Проектов   Классов уровня 0   Ступени / полный проект   Целых проектов по причинам
01            1650     33441,6         14                269                       —   14 чтение DLL
02            1498     47732,6         14                269                       —   14 чтение DLL
03            2482     45428,0         16                299                       —   16 чтение DLL
04            1846     42752,3         14                242                       —   13 чтение DLL, 1 timestamp
05            1670     45110,8         14                242                       —   13 чтение DLL, 1 timestamp
06            1650     38047,8         14                242                       —   13 чтение DLL, 1 timestamp
07            2323     42293,4         14                242               0/1/1/1 / 1   13 чтение DLL
08            1880     39533,8         14                242               0/1/1/1 / 1   13 чтение DLL
09            1955     40539,2         14                242               0/1/1/1 / 1   13 чтение DLL
```

Ответ выбранной ступени `direct` ни разу не уменьшил общее число классов уровня 0 (**9/9** без уменьшения).
Из 128 решений по проектам **122** остались целиком из-за ошибки чтения DLL, **3** — из-за timestamp после
внешней записи. Ещё **3** получили ступени шириной в единственный класс проекта. Ответ «изменившиеся типы
без тестов» не получен (**0/9**). Причина гонки первого чтения DLL со сборкой не встретилась (**0/9**).
В этих девяти пробах клиент дождался первого чтения. Намеренное перекрытие проверено отдельным Windows-тестом.

Одна из двух непрочитанных DLL была рядом с выходами **16 из 17** тестовых проектов по правилу M-06.
Сервер отклонил аргументы атрибутов, содержащие одиночный суррогат UTF-16. Вынесено в
[отдельную задачу по декодированию атрибутов](../follow-up-il-attribute-decoding.md).

Пробы 7–9 дали сопоставимые прогоны `direct` и целого проекта на тех же DLL. Каждый запуск исполнил два
теста без пропусков. Во всех трёх парах фильтр стоял первым. С одним классом эти пробы показывают стоимость двух
способов запуска при одинаковой ширине.

```text
Проба   Классов direct / всего   Тестов direct / всего   direct, мс   Целый проект, мс
07                       1 / 1                  2 / 2       1444,3             1549,7
08                       1 / 1                  2 / 2       1273,8             1303,4
09                       1 / 1                  2 / 2       1863,4             1580,1
```

### Дополнительные пары с фактическим уменьшением числа классов

Полная сборка второго предложенного solution, **Bitwarden**, остановилась на трёх ошибках: отсутствует
`cargo` (`MSB3073`) и два обращения не компилируются (`CS1061`). Для проверки риска запущен отдельный
Release `.slnx` из трёх unit-тестовых проектов и всех их транзитивных `ProjectReference`, всего **16**
проектов и **25** тестовых классов. Сервер прочитал все 16 выходов. В каждом случае менялось одно тело
через `update_file_content`. У каждой пары одна целевая тестовая сборка и одинаковые DLL. В третьей
пробе два других тестовых проекта уровня 0 дали пустые ступени с причиной «изменившиеся типы без
тестов». В этой дополнительной серии причина встретилась в **1 из 3** правок, в **2 из 5** решений по
проектам. Отказов от сужения по гонке снимка, чтению или свежести не было. Во всех парах VSTest прошёл
без ошибок и пропусков.

```text
Проба  Чтение, мс  Ответ, мс  Ступени цели  Целый, классов  Тестов direct / всего  direct, мс  Целый, мс
01           1774     9889,5      0/8/8/8             14               64 / 127      1649,0     1151,3
02           1965    10028,3      0/2/2/2              7                 5 / 27      1649,5     1287,0
03           1860     8533,9      0/2/2/2              4                 6 / 12      1065,4      876,2
```

Проверка риска дала **3 из 3** случаев, где уменьшение числа классов на **43–71%** сопровождалось
более долгим запуском: на **22–43%** в этих парах. Фильтр запускался первым. Один прогон на порядок
запуска и этот набор коротких тестов ограничивают вывод о времени, поэтому здесь зафиксированы наблюдения
конкретного прогона. Эти пары характеризуют только собранные 16 проектов.

### Как повторить замер

1. В отдельной копии solution соберите конфигурацию и TFM, которые вы обычно используете. Запустите
   проверяемую Release-сборку сервера как отдельный stdio MCP, вызовите `get_mcp_server_info` для версии и
   `load_workspace` с той же конфигурацией, платформой и TFM. Сохраните `elapsed_ms` из серверного лога
   `assembly_snapshot first_read outcome=success` и измерьте две удерживаемые версии снимка по
   [инструкции к замеру памяти](evidence/task-06-snapshot-measurement.md).
2. Выберите несколько типичных правок. Каждую начинайте с чистой собранной базы и нового процесса сервера.
   Дождитесь `baseline_capture generation=… outcome=success` и окончания первого чтения сборок в этом
   процессе. Сохраните правку, соберите её и замерьте время одного вызова
   `estimate_affected_tests { "depth": "all", "run": false }`. Для каждого выбранного тестового проекта
   запишите `changed/direct/di/all` и число классов во всём проекте по `inventory` из той же инструкции.
   `inventory` вызывает построитель списка классов, который использует сам инструмент, с теми же
   конфигурацией, платформой и TFM. Ступень `all` берите из ответа продукта: карта M-06 строится по
   выходному каталогу тестовой сборки. При выборе целого проекта запишите причину из ответа. Отдельно
   подсчитайте правки с гонкой первого чтения и сборки, правки с ответом «изменившиеся типы без тестов» и
   случаи, когда размер выбранной ступени равен размеру проекта. Для гонки проведите отдельную пробу со
   сборкой сразу после загрузки.
3. Хотя бы для трёх правок с успешной сборкой и непустым фильтром выбранной ступени (`Stages (classes)`)
   сравните на одном и том же наборе DLL и проектов
   `dotnet test <проект> --no-build --no-restore -c <конфигурация> --filter '<фильтр из ответа>'`
   с такой же командой без `--filter`. Сохраните те же настройки, включая `-f <TFM>` для проектов с
   несколькими TFM. Если проектов несколько, сложите времена и число тестов по одним и тем же проектам
   для обеих стратегий. Запишите время, число фактически исполненных тестов и код выхода каждого вызова.
   Запуск с нулём тестов не даёт основания засчитать экономию.
4. Передайте в отчёт дату, версию и число правок, длительности, память, размеры ступеней, знаменатели,
   числа исходов и причины. В причинах оставьте категорию и уберите пути и имена. Сырые логи и ответы
   закрытого solution храните локально.

# Пункт 5: `failed==0` при ненулевом exit code

Статус: **proposal, не реализовано**. Это разбор. Ship: [implementation/05-nonzero-exit.md](implementation/05-nonzero-exit.md).

Base truth — локальный `main`
`46d796d258c9af5212caa4fb673cc896ab1ac53e` (v1.3.32); публичный
`origin/main` — `bc3ec0946def13ee575a81873854ad6bb1001758`, форк —
`VladD2/RoslynMcpServer@main` `58dc361248fb51c06f1ab5c131849bbb4a1a9883`.

Каталог — [README.md](README.md). Агрегация VSTest-блоков (п. 2) — предварительное
условие: пока counts склеены из разных сборок, `failed==0` нельзя считать
доказанным. Temp-отчёты из того же коммита форка **не** входят (каталог §5.2).

Приоритет: **P2**, но на фильтрованном `run_specific_test` / `run_test_by_filter`
по `.sln` с несколькими тест-сборками это ложный красный для агента. В форке
закрыто после прогонов на **CsNitra** (MSTest) и **xUnit v3** solution.

---

## 1. Симптом

`dotnet test --filter …` по solution гоняет **каждую** тест-сборку. Сборки без
совпадений печатают `No test matches the given testcase filter …`.

| Адаптер | Exit процесса | Что видит агент у нас |
|---|---|---|
| MSTest | часто 0 | ложный «no matching tests», если display name не совпал с needle (частично закрыто `1.3.23`) |
| xUnit (v2/v3) | **1**, даже когда другая сборка прогнала тест | `failed==0 && exitCode==1` → заголовок `❌ 0 Tests Failed` |

У нас успех жёстко: `failed == 0 && exitCode == 0`
(`Diagnostics/VstestOutputParser.cs:304`). Иначе ветка fail
(`❌ {failed} Tests Failed`), даже при `failed == 0` и разобранном `Passed: 1`.
`Tools/TestTools.cs:643` дополнительно навешивает «Silent / unparsed failure
hints» только когда `IsSilentUnparsedFailure` — при живом summary этот путь не
срабатывает, но заголовок уже красный.

Реальный фикстурный вывод форка (`fa5a264`, тест
`BuildMarkdownReport_xunit_multi_project_matching_test_with_nonzero_exit_passes`):

- sibling `B.dll`: `No test matches …` → testhost exit 1;
- `A.dll`: `Passed Ns.Class.Method`, `Total tests: 1` / `Passed: 1`;
- процесс: exit 1, `failed == 0`.

Агент читает «упало 0 тестов» и начинает крутить filter / `load_workspace`.

Второй кусок того же коммита: «no matching tests» только если **ни один** тест
не выполнился **и** в логе есть явная строка `No test matches`. Иначе noise
от sibling-сборок плюс MSTest method-only display (`Passed Test_Foo [28 ms]`)
коротит весь прогон. Name-matching у нас уже есть (`1.3.23`); gate по
`Summary.Total > 0` форка — дополнительный предохранитель, не замена.

---

## 2. Почему нельзя копировать форк один в один

Форк решает заголовок так: `if (failed == 0) { … success; if (exitCode != 0) note }`.
Exit больше не участвует в verdicte.

Это закрывает sibling-фильтр, но exit ≠ 0 бывает и настоящей аварией:

| Сценарий | Summary | `failed` | Нужный verdict |
|---|---|---|---|
| xUnit sibling no-match + 1 passed | есть, Total>0 | 0 | **успех** + пометка про exit |
| MSTest sibling no-match + 1 passed, exit 0 | есть | 0 | успех (уже) |
| Все сборки no-match | нет / Total 0 | — | «no matching tests», не успех |
| Restore hang / locked `obj` | нет | — | silent failure (уже `IsSilentUnparsedFailure`) |
| Compile до тестов, `Build FAILED`, 0 Error(s) в футере | нет / не VSTest | — | не успех; pitfall 21 |
| Реальные падения | Failed>0 | >0 | fail, exit обычно 1 |
| Timeout / cancel | — | — | отдельные ветки `TestTools`, не трогать |

Слепой `failed==0` без summary превратит hung restore в «все тесты прошли».

---

## 3. Норма

Заголовок **успеха** (`## All tests passed` / `## Filtered tests passed`) только
если **все** условия:

1. Есть разобранный `TestSummary` (не `partial`, не Total-only → `null`).
2. `summary.Failed == 0`.
3. `summary.Total > 0`. В корректном VSTest summary skipped входят в Total,
   поэтому отдельное `Skipped > 0` не нужно; нарушение
   `Total == Passed + Failed + Skipped` — partial, не success.
4. Не `IsSilentUnparsedFailure`.
5. В логе нет неразобранного compile/restore fail **до** VSTest (уже отсекается
   п. 4 и pre-test `dotnet build` в `TestTools`).

`exitCode` **не** входит в п. 1–5. Если успех и `exitCode != 0` — тот же зелёный
заголовок плюс одна строка-пояснение (как в форке), без смены Status на fail.
В metadata по-прежнему фактический exit.

Заголовок **«no matching tests»** (`requireFilterMatch`) только если одновременно:

- `summary` отсутствует или `Total == 0`; и
- в логе есть явная строка `No test matches the given testcase filter`.

Одного `FilterMatchedAnyTest == false` недостаточно: неизвестный формат adapter,
обрезанный вывод или отсутствие per-test lines не доказывают нулевой матч.

Если `Total > 0`, sibling `No test matches` **не** имеет права коротить прогон.
`FilterMatchedAnyTest` (`1.3.23`, display suffix) остаётся; gate по Total —
второй замок, не удаление name-match.

Не менять: timeout/cancel, `IsSilentUnparsedFailure` в `TestTools`, бюджеты
StdOut/StdErr (`1.3.24`), progress heartbeats, `TruncatedProcessLog`.

---

## 4. Правка

Только `Diagnostics/VstestOutputParser.BuildMarkdownReport` (+ узкий regex
`No test matches the given testcase filter`, у нас его нет).

1. Успех: `failed == 0` при условиях §3, **без** `&& exitCode == 0`.
2. При `exitCode != 0` на этой ветке — italic note, не fail-блок.
3. Перед name-match: если `requireFilterMatch` и `summary.Total > 0` — не
   заходить в «no matching tests».
4. «no matching tests» при нулевом прогоне: **обязательно** требовать явную
   no-match строку. Пустой/неизвестный лог без summary остаётся partial/silent,
   а не мимикрирует под «фильтр мимо».

`Parse` / `IsSilentUnparsedFailure` / `FilterMatchedAnyTest` не ломать.
После п. 2 (агрегация блоков) summary на `.sln` станет суммой сборок —
тогда `failed==0` означает «ни в одной сборке не падало», а не «первый блок
зелёный».

---

## 5. Тесты

Рецепт форка (`VstestOutputParserTests`) плюс наши отрицательные кейсы:

| Тест | Ожидание |
|---|---|
| xUnit: `No test matches` в B.dll + `Passed FQN` / `Total tests: 1` / `Passed: 1`, exit 1 | зелёный заголовок, нет `Tests Failed` / `no matching tests`, есть пометка `non-zero` / exit |
| MSTest: два sibling no-match + method-only `Passed Test_Foo`, `Total tests: 1`, exit 0 | успех, нет `no matching tests` (регресс `1.3.23` + Total-gate) |
| Все сборки только `No test matches`, нет Total, exit 0 или 1 | `## Filtered test run — no matching tests` |
| `failed==0`, **нет** summary, exit 1, лог restore/`Build FAILED` | не успех; `IsSilentUnparsedFailure` true |
| Реальный fail: `Failed: 1`, exit 1 | `❌`, детали assertion |
| Одна сборка, exit 0, `Failed: 0` | как сейчас, без note про exit |

Фикстуры — синтетика по образцу форка (CsNitra / xUnit v3), без их деревьев в репо.

---

## 6. Acceptance

- Фильтрованный прогон по `.sln` с несколькими тест-проектами, где фильтр попал
  в одну сборку, а остальные написали no-match: агент видит успех и counts
  попавшей сборки (после п. 2 — сумму), а не `❌ 0 Tests Failed`.
- Полный промах фильтра по всем сборкам — по-прежнему явный no-match, не успех.
- Hung restore / lock / compile без VSTest summary — по-прежнему не успех.
- Exit code в metadata не подменяется.

**Риск.** Средний, локализован в renderer. Главный промах — объявить успех при
`failed==0` без summary. Поэтому §3 п. 1 обязателен и покрыт тестом.
Делать **после п. 2**; до агрегации «успех по failed==0» на склейке блоков
может зеленеть неполный отчёт.

---

## 7. Вне скоупа

- `TempReportWriter` / смена контракта verbose-лога.
- Pass/fail без parsed summary.
- Смена pre-test `dotnet build` в `TestTools`.
- Локаль CLI (п. 1) и platform (п. 3) — независимы, но на ru-RU без п. 1
  парсер может не увидеть `Passed:` и тогда п. 5 не дойдёт до success-ветки.

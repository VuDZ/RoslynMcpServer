# BuildHost/task-04 — Извлечение ProjectFileInfo в host

- Дата: 2026-10-26.
- Статус: **in progress, 2026-10-26; шаг 0 выполнен, GC9 — эксперимент в стенде**.
  Принятый объём: разбивка extraction и повторные офисные данные; production extraction прежний.
  Поздний GetAllGlobs вызывает дополнительную evaluation; ранний Record отвергнут контрпримером.
  Новый профиль — server GC + ConserveMemory=9 только в опытной копии host. На домашней Roslyn.slnx
  против A+B: загрузка −6,0 %, load + first query −4,5 %, host 1518 → 1725 MB (+13,6 %), CPU +12,5 %.
  Одна диагностическая пара: GetAllGlobs 8663 → 8232 мс. Полные RPC 434 результатов совпадают по всем
  полям с учётом естественного порядка Excludes/Removes; raw comparisons сохранены. Диапазон памяти
  около 1,7 против 1,45 ГБ владелец счёл приемлемым. Release build, main 1467/0/0 (serial), включая 123
  SourceStructure, и сценарии сбоев пройдены. Офисный порог ≥10 % и production-активация — pending
  ([валидация и отчёт](evidence/task-04-projectfileinfo-extraction.md#следующий-эксперимент-gc-только-в-host-2026-10-26)).
  Кандидат C — общий кеш SDK resolution (`SharedSDKCache`), проба в стенде. Linux: identical, выигрыш не
  измерим. Дома, Roslyn.slnx: identical (1199 отпечатков), SDK resolution — 45–50 % каждой evaluation;
  один контекст на решение — wall ×0,773, смена TFM ×0,632, globs ×0,589; контекст на проект — ×0,898.
  Офис: identical (426), SDK resolution 35–36 %; globs ×0,645 (project) и ×0,686 (solution).
  Итог пробы — go к реализации в форке: сначала контекст на проект с проверкой версий SDK
  ([проба](evidence/task-04-projectfileinfo-extraction.md#кандидат-c-общий-кеш-sdk-resolution--проба-2026-10-26)).
  C реализован в форке (переключатель, по умолчанию выключен): контекст на проект и проверка версий SDK. На
  Linux identical в матрице, полные RPC совпадают, фикстура с разными версиями SDK и `global.json` пройдена,
  сценарии сбоев и SourceStructure пройдены. Windows, Release: main 1467/1467, AnalyzerLifecycle 13/13.
  ([реализация](evidence/task-04-projectfileinfo-extraction.md#кандидат-c-в-форке-2026-10-26)). Матрица дома,
  Roslyn.slnx: identical, полные RPC совпадают; к A+B загрузка ×0,819, load + first query ×0,879, CPU host
  ×0,834, память без изменений. Смена TFM ×0,575, поздний GetAllGlobs ×0,44, сборки не медленнее. Порог
  Roslyn.slnx выполнен ([матрица](evidence/task-04-projectfileinfo-extraction.md#матрица-c-roslynslnx-домашний-компьютер-2026-10-27)).
  Офис: identical, загрузка ×0,901 — на 9,9 % при пороге 10 %; load + first query ×0,971. Полные RPC
  остановились на `fork-ab`: регистр одной папки, следствие порядка загрузки A + B, не C; RPC `fork-abc` не
  сняты ([матрица](evidence/task-04-projectfileinfo-extraction.md#матрица-c-офисное-решение-офисный-компьютер-2026-10-27)).
  Решение владельца 2026-10-27: порог принят как в пределах погрешности; регистр путей на Windows — не отличие.
  Стенд: RPC-сравнение на Windows выводит такие пути отдельным счётчиком, добавлен `-RpcOnly`
  ([решения и стенд](evidence/task-04-projectfileinfo-extraction.md#решения-владельца-по-офисной-матрице-и-правка-стенда-2026-10-27)).
  Полные RPC на офисе: `fork-ab` и `fork-abc` identical, у обоих те же 28 путей с другим регистром. C прошёл все
  пороги и проверки ([RPC и итог C](evidence/task-04-projectfileinfo-extraction.md#полные-rpc-c-на-офисном-решении-2026-10-27)).
  Решение владельца 2026-10-27: C включён в сервере по умолчанию тем же выключателем, что A и B; task-04
  принимается в этом объёме. Linux: build, 161 точечный тест с SourceStructure, загрузка сервером с трассой — C
  включён, при выключенных оптимизациях нет. Main-набор и AnalyzerLifecycle на Windows — pending, после них — accepted
  ([включение](evidence/task-04-projectfileinfo-extraction.md#production-включение-c-2026-10-27)).
- Сложность: hi; риск — сократить извлечение ценой неполного или иного `ProjectFileInfo`.
- Сопутствующее исправление, 2026-10-26: matrix/baseline/scenarios разрешают относительные `Solution` и
  `OutDirectory` от location PowerShell; автоматический каталог без `OutDirectory` сохранён и проверен
  ([проверка](evidence/task-04-projectfileinfo-extraction.md#исправление-относительных-путей-стенда-2026-10-26)).
- Depends on: [task-02](task-02-buildhost-optimization-spike-hi.md) (выполнен, A + B — go),
  [task-03](task-03-buildhost-fork-server-integration-hi.md) (accepted: форк с A + B — production BuildHost сервера).

## Почему

После A + B корневая evaluation с потока запросов почти ушла, и извлечение `ProjectFileInfo` стало одной из трёх
крупных статей работы host на потоке запросов ([отчёт task-02](evidence/task-02-optimization-spike.md#куда-ушло-время-и-что-осталось),
медианы трассы `fork-ab-trace` этапа 3):

| решение, компьютер | извлечение | доля загрузки | для сравнения: сборка |
|---|---|---|---|
| офисное решение, офисный | 10,1 с | 32 % — самая крупная статья | 6,9 с |
| Roslyn.slnx, офисный | 18,9 с | 22 % | 23,2 с |
| Roslyn.slnx, домашний | 7,8 с | 22 % | 10,9 с |

Извлечение — span `extract` (`MSBuild/ProjectFile/ProjectFile.cs`): после design-time build каждого TFM
`ProjectInstanceReader.CreateProjectFileInfo()` переводит результат сборки в данные для клиента: аргументы
компилятора, документы с путями, `Link` и папками, additional- и editorconfig-файлы, ссылки на проекты и пакеты,
metadata references (если аргументов нет), capabilities, content-файлы и `FileGlobs` (`Project.GetAllGlobs()`). Идёт
на потоке запросов, клиент ждёт ответа. На офисном решении это около 70 мс на результат сборки (142 проекта,
18 877 документов), на Roslyn.slnx на офисном компьютере — около 43 мс (434 результата). Что из этого дорого, не
измерено.

## Этапы

0. **Шаг 0, без изменения поведения.** В форке — под-span внутри `extract` по частям `CreateProjectFileInfo`:
   `GetAllGlobs`, аргументы компилятора (`GetCompilerCommandLineArgs` и запасной `ReadCommandLineArgs`), документы с
   метаданными, additional/editorconfig, остальные элементы и свойства. Поля span — число элементов каждой части.
   Сводка трассы (`trace-summary`) показывает части отдельными категориями. Linux: `RoslynMcpServer.sln` identical с
   трассой; владелец — `Invoke-BuildHostForkMatrix.ps1 -Variants fork-ab,fork-ab-trace` на Roslyn.slnx и офисном
   решении. Итог шага 0 — какая часть дорогая, от чего зависит её цена (документы, TFM, импорты) и сколько можно
   выиграть в лучшем случае. Если ни одна часть не даёт заметной доли загрузки, задача закрывается без кода
   оптимизации.
1. **Кандидат — один за раз, по итогу шага 0.** Направления-гипотезы, не обещание:
   - сделать дешевле дорогую часть, например не считать заново то, что одинаково для всех TFM одного проекта, — с
     доказательством, что входы действительно те же;
   - вынести извлечение с потока запросов, хотя бы для проектов, которые A собрала наперёд: результат сборки —
     неизменяемый снимок, но `GetAllGlobs()` и часть полей читают `Project`, а следующая смена TFM этот `Project`
     переключает (то же ограничение, из-за которого B читает ссылки до того, как отдать evaluation).
   Включается своим переключателем, как A и B, независимо от них.
2. **Замер.** Матрица task-02 с новым вариантом против A + B; трасса — куда ушло время.

## Корректность

- Identical по правилам task-00 против host из пакета, comparator не ослабляется; сценарии сбоев task-00.
- `SolutionObservation` сравнивает поля workspace, а не `ProjectFileInfo` целиком: не захвачены `FileGlobs`,
  `ContentFilePaths`, `ProjectCapabilities`, `PackageReferences`, папки и логические пути документов,
  `TargetFrameworkIdentifier`/`Version`. Кандидат, который меняет вычисление какого-то поля, сначала добавляет
  сравнение этого поля — на уровне ответа host (RPC) или workspace, — и только потом меняет вычисление.
- Не отбрасывать и не упрощать поля ради выигрыша, даже если клиент 5.9.0 их не читает: ответ host — контракт
  протокола.

## Пороги

После шага 0, до production-кода следующего кандидата, зафиксированы пороги к **A + B без трассы**:
медиана загрузки офисного решения короче не менее чем на **10 %**, Roslyn.slnx не медленнее более чем на **2 %**;
прогрев + минимум три измеренных fresh-process раунда. Load + first useful query учитывается отдельно по
каждому раунду. Корректность — identical по task-00 и по затронутым полям ответа host, включая сценарии сбоев.
Память host и клиента — как в task-02: **+10 % — ориентир**, отклонения отражаются в отчёте
(решение владельца 2026-10-26).

Отчёт — `evidence/task-04-projectfileinfo-extraction.md`; статусы здесь и в README. Production включение кандидата —
отдельное решение, как в task-03.

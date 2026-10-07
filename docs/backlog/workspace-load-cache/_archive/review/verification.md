ID: V-01
Severity: Blocker
Category: Observability

Target:
verification.md / «Независимый oracle»

Claim:
Сравнивать восстановленный workspace с отдельным свежим `MSBuildWorkspace` на стабильном дереве. Мок, повторяющий DTO, не доказывает эквивалентность. Нормализуются ProjectId/DocumentId, порядок, temp shadow-пути.

Evidence:
Если восстановленный host — `AdhocWorkspace` (R-01), сравнение с `MSBuildWorkspace` даёт систематические отличия: analyzer host, generator execution, `FilePath` проекта, ApplyChanges, encoding. Нормализация shadow-путей и Id скрывает host-различие, но не устраняет его. Если оба oracle — MSBuild после Open, hydrate не проверяется.

Failure scenario:
1. Матрица красная на всех generator/analyzer случаях → все в unsupported → «профиль чистый».
2. Либо нормализуют различия host как «не семантические» — oracle перестаёт быть независимым.
3. V01 зелёный на графе references и красный/пропущенный на V04/V07; эпоху закрывают по V01.

Suggested change:
Oracle = тот же host, что production после hit. Если production host не может быть `MSBuildWorkspace` без Open, матрица не доказывает README. Не сравнивать Adhoc со свежим MSBuild и не называть это эквивалентностью.

Confidence:
High

---

ID: V-02
Severity: High
Category: Observability

Target:
verification.md / V11

Claim:
Добавление/удаление `.cs`, Compile Remove: Epoch 2 делает miss; результат равен свежему DTB.

Evidence:
После miss алгоритм Epoch 2 идёт в обычный MSBuild. Равенство свежему DTB тривиально: сравнивают DTB с DTB. Утверждение не проверяет hydrate и не проверяет, что *детектор* membership сработал по правильной причине (новый файл в glob vs любой Created).

Failure scenario:
1. Тест добавляет `.cs`, видит `inputs_changed`/`membership_changed` или даже `unsupported`, затем DTB.
2. Галочка V11.
3. Детектор сработал потому, что хеш дерева/mtime каталога изменился случайно, а не потому что membership algorithm увидел glob. Регресс алгоритма позже не ловится.

Suggested change:
V11 split: (a) причина miss обязана быть `membership_changed` при неизменных hashes старых documents/csproj; (b) равенство DTB — отдельный пункт, не доказательство hydrate. Hydrate+новое членство в Epoch 2 *не должно* быть hit — это не тест гидрации членства.

Confidence:
High

---

ID: V-03
Severity: High
Category: Observability

Target:
verification.md / V04; epoch-0 приёмка generator fixture

Claim:
Analyzer, generator, AdditionalFiles, editorconfig: входы/результаты совпадают либо профиль отвергнут. Для generator fixture проверять emitted sources и диагностику использования generated-типа.

Evidence:
Emitted sources через MCP недоступны (E0-03). «Профиль отвергнут» — допустимый зелёный исход, который выкидывает единственный случай, где Adhoc vs MSBuild точно разъедется. Диагностика использования generated-типа без чтения константы версии снова сводится к отсутствию CS0103.

Failure scenario:
1. V04 = unsupported.
2. Профиль без generator объявляют первым.
3. In-solution generators — как раз те репозитории, где load дорогой и overlay уже существует; кэш их не покрывает.

Suggested change:
V04 не может быть unsupported для go основного маршрута, если overlay/SG входят в текущий продукт. Либо in-process SG oracle обязателен, либо disk-hit запрещён на solution с analyzer/generator projects.

Confidence:
High

---

ID: V-04
Severity: Medium
Category: Measurement

Target:
verification.md / Release gates «default false сохраняется»; «доказанный cross-process hit»

Claim:
Default false сохраняется. Доказанный cross-process hit без DTB на стабильном поддерживаемом графе. Ноль необъяснённых semantic differences.

Evidence:
Gate позволяет выпустить параметры и store при нулевом полевом использовании (E2-06). «Поддерживаемый граф» может быть fixture из двух проектов (E0-02). Ноль differences на fixture не переносится на большой sln, который не в профиле.

Failure scenario:
1. Все gate зелёные на SDK fixture.
2. README «Agent tools by version» добавляет `useDiskCache`.
3. На целевой монорепе всегда `unsupported`/`inputs_changed`; агенты тратят контекст на флаг без эффекта.

Suggested change:
Release gate: hit-rate и e2e 70% на заранее названном большом решении, иначе параметры не появляются в Description. Fixture-only = experiment, не shipped.

Confidence:
High

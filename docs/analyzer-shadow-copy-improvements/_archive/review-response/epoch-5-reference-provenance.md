# Ответ: эпоха 5

Источники: [review](../review/epoch-5-reference-provenance.md),
[спецификация](../epoch-5-reference-provenance.md).
Обозначения источников — в [README ответа](README.md).

## E5-01 — NEEDS CLARIFICATION

**Основание.** Конфликт требований выявлен верно: нельзя одновременно обещать
обязательное доказательство происхождения и сохранение исходного repro, не
показав доступного канала metadata. Но отсутствие обычного Roslyn ProjectReference
не доказывает отсутствие evaluated MSBuild данных вообще. Проверка доступности
не выполнена ни в proposal, ни в этом ответе. Suggested fallback тоже не доказательство:
missing/unreadable NuGet analyzer может случайно совпасть с AssemblyName проекта.

**Что уточнить.** До смены matcher нужен результат feasibility на Roslyn 5.9.0:
какие evaluated данные связывают Analyzer item с project, с какой стоимостью.
Если их нет, независимый арбитр/владелец требований должен выбрать: сохранить
явно эвристический opt-in fallback для missing-path или ограничить поддержку.
Unreadable нельзя молча приравнивать к missing. Предпочтение автора ответа —
сначала сохранить shipped matcher и отдельно исправлять доказанный foreign-path case.

**Затрагиваемые разделы.** «Решения и требования» 2–5, «Приёмка», README
результат эпохи 5: этап discovery/go-no-go перед rollout, без объявления обоих
несовместимых acceptance выполненными.

**Последствия.** Семантика выбора исполняемой сборки, false positives/negatives,
MSBuild evaluation cost, compatibility. Нового public API пока не предлагается.
Арбитраж A-04; это вопрос evidence и допустимого риска, не запрос на правки сейчас.

## E5-02 — PARTIALLY ACCEPT

**Основание.** Existing foreign path и missing-path recovery нужно проверять
раздельно. Но «missing + unique name → replace» остаётся эвристикой; отсутствие
файла не подтверждает принадлежность. И «existing different path → never replace»
слишком сильно: путь может указывать на устаревший output того же подтверждённого
project после смены Configuration/OutputPath.

**Изменение.** «Решения и требования», «Приёмка»: decision table для exact
resolved output, подтверждённого project с неправильным путём, существующего
foreign analyzer и missing/unreadable без provenance. Положительный и отрицательный
fixture обязательны; последняя ветка остаётся decision gate E5-01, а не
автоматическим skip или replace. До gate не ломать shipped repro новым matcher.

**Последствия.** Compatibility matrix и точность диагностик. Проверять разные
исполняемые маркеры при одинаковой форме API генераторов; сравнение только
имён типов не обнаружит ошибочную замену.

## E5-03 — ACCEPT

**Основание.** W принимает `targetFramework`, а F использует output загруженного
Project. Непроинтерпретированный список `TargetFrameworks` не должен заново
создавать неоднозначность после успешной evaluation. U не требует второго
самостоятельного интерпретатора MSBuild.

**Изменение.** «Решения и требования» 6, «Приёмка»: исходить из фактически
загруженных inner projects, их global properties и resolved outputs. Не
перебирать TFM XML как источник выбора. Несколько одинаковых AssemblyName
в текущей solution обрабатывать отдельно; учитывать verified provenance,
если он впоследствии позволит различить candidates.

**Последствия.** Matcher tests Configuration/TFM и отсутствие лишней evaluation
на semantic reads. Это не обещание поддержать произвольный multi-targeting:
ошибки/неполнота load остаются диагностируемыми и не маскируются skip matcher.

## E5-04 — PARTIALLY ACCEPT

**Основание.** F `Applied`/свободный `SkipReason` плохо выражают разные причины,
а ambiguous candidates сейчас вовсе пропускаются без результата. Но полный
запрет `provenance_unconfirmed` необоснован: отсутствие evidence — честная
причина отказа, если именно такой контракт будет выбран. Нельзя вместо неё
выдавать более конкретный, но недоказанный `original_exists_unrelated`.

**Изменение.** «Диагностика»: стабильные внутренние reason codes для
`source_output_missing`, `ambiguous_assembly_name`, foreign-path при доказанном
несоответствии, `provenance_unconfirmed`, access/copy failure. Для каждого —
основание и отдельное состояние source/original path; generic unconfirmed
не заменяет известную причину. Считать matched/skipped по определённым правилам.

**Последствия.** Внутренний result DTO, логи и tests причин. Public summary
остаётся кратким; расширение structured MCP schema — отдельное изменение,
а не неизбежное следствие введения внутренних кодов.

## E5-05 — PARTIALLY ACCEPT

**Основание.** Dependency эпохи 5 от эпохи 1 уже явно указана, поэтому её
отрицание в review неточно. Безопасный matcher не обязан ждать файловой
реализации эпохи 2: wrong-match и overwrite — самостоятельные дефекты.
Однако регрессии должны проверять фактическую идентичность исполнения, а не
только shape generated API.

**Изменение.** В эпоху 1 добавить same-filename external analyzer и in-solution
candidate с различимыми маркерами, а в эпоху 5 сделать этот test release gate.
Если fixture появляется позднее baseline, baseline расширяется явно. Сохранить
алгоритмическую независимость от 2–4, но не выдавать passing matcher tests за
готовность всей серии, пока другие baseline failures остаются.

**Последствия.** Матрица oracle и rollout gate, не обязательная перестановка
всех эпох. Дополнительный dependency — feasibility E5-01; новая публичная
функция не требуется.

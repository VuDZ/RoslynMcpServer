# Эпоха 5 — Происхождение analyzer references

Статус: **E5-S1 выполнено** ([epoch-5-s1-results.md](epoch-5-s1-results.md));
U-ARB-01 выбран — capture на load ([UNRESOLVED-v2.md](UNRESOLVED-v2.md)).
Rollout matcher **заблокирован** до принятого capture design. Запас: Alt-2
unique-name эвристика, Alt-3 сужение до exact path. Inaccessible не выбран.
Зависимость: exact oracle и fixtures эпохи 1. Алгоритм не зависит от эпох 2–4,
но его приёмка не означает готовность всей серии.

## E5-S1. Проверка выполнимости без изменения matcher

Совпадение filename с уникальным `AssemblyName` даёт кандидата, но не доказывает
происхождение analyzer item. До изменения выпущенного matcher исследовать Roslyn
5.9.0/evaluated MSBuild: все доступные связи analyzer item с проектом для
`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, нужную evaluation,
global properties, стоимость и стабильность по Configuration/TFM.
Не предполагать наличие обычного Roslyn `ProjectReference` для analyzer-связи.

Discovery не изменяет matcher и не выполняет build ради сопоставления. Результат
дополнительной evaluation, если она нужна, собирается на load/явной границе
обновления графа, а не при каждом семантическом запросе. Записать её входы и цену.

U-ARB-01 должен определить missing-path policy после evidence. До решения сохранить
выпущенный matcher целиком; даже частичное внедрение новой таблицы ниже не разрешено.
Нельзя автоматически выбрать skip-all или unique-name fallback, а inaccessible
нельзя приравнять к missing. Если provenance не существует, выбор между эвристикой
и сужением исходного repro делает владелец продуктового требования.

## E5-S2. Нормативная таблица для последующего rollout

Поиск кандидатов и подтверждение связи — отдельные шаги. Кандидаты берутся из
фактически загруженных inner `Project`, их global properties и resolved outputs
(`CompilationOutputInfo`/`OutputFilePath` в пределах доступной модели). Не выбирать
TFM повторным разбором unevaluated `TargetFrameworks`. Несколько загруженных
кандидатов уточняются verified provenance; неустранённая неоднозначность означает skip.

| Случай | Правило после разрешения gate | Обязательное различие |
| --- | --- | --- |
| Exact resolved output загруженного проекта | Подтверждённое точное соответствие позволяет подготовку/rewrite | Проверенное равенство output, не похожая строка пути |
| Provenance-confirmed проект, original path stale/wrong | Использовать подтверждённый resolved output, если пригоден | Существующий другой путь может быть stale output того же проекта |
| Доказанно внешний analyzer | Сохранить исходную ссылку; не заменять in-solution кандидатом | Foreign должен быть доказан, не выведен только из несовпадения путей |
| Missing original без доказательства | Действие не выбрано: U-ARB-01 | Missing не является provenance |
| Inaccessible original | Действие не выбрано отдельно в U-ARB-01 | Access failure не означает отсутствие файла |
| Неоднозначные загруженные кандидаты | Skip, если verified provenance не снимает неоднозначность | Не выбирать первый и не добавлять фиктивную неоднозначность из TFM XML |
| Source output отсутствует | Не готовить замену; исходная ссылка и конкретная причина | Отсутствие source отдельно от original-path state |

Это таблица разных условий, а не алгоритм с неявным приоритетом строк: выбор связи
и доступность файлов фиксируются раздельно. Ветки missing/inaccessible до решения
остаются открытыми даже при понятном diagnostic code.

## E5-S3. Диагностика

Ввести стабильные внутренние reason codes:

| Код | Значение |
| --- | --- |
| `reference_rewritten` | Подготовленная ссылка заменена; исполнение не утверждается |
| `source_output_missing` | Нет output выбранного source проекта |
| `ambiguous_assembly_name` | Несколько неразличённых загруженных кандидатов |
| `proven_foreign_path` | Подтверждённое внешнее происхождение |
| `provenance_unconfirmed` | Доказательства связи отсутствуют; само по себе не выбирает skip для U-ARB-01 |
| `access_failure` | Не удалось проверить/прочитать путь из-за доступа |
| `preparation_failure` | Сбой подготовки выбранной сборки |

Для результата хранить отдельно состояние original path и selected-source path,
исходный путь, выбранный проект, основание выбора и при наличии generation.
Известную конкретную причину не заменять generic unconfirmed; не выдавать более
конкретное foreign объяснение без evidence. Подсчёт rewritten/skipped следует
фактическим результатам ссылок, включая ambiguous. Публичная сводка компактна;
новая структурированная схема ответа не вводится. Load/execute статусы — эпоха 3.

## E5-S4. Приёмка

До rollout обязательны metadata feasibility и записанное решение U-ARB-01, включая
политику inaccessible. Проверить original missing-path repro и foreign same-name
fixture эпохи 1 с разными exact executed markers, в том числе missing foreign.
Отсутствие generated ошибки или одинаковая форма API не доказывают верный выбор.

Приёмка исходного repro зависит от решения: при достаточном provenance он должен
исправляться; если владелец выберет сужение поддержки, ограничение и изменённое
ожидание фиксируются самим решением до тестов/rollout, а не выбираются реализацией.
Внешний analyzer не подменяется, неоднозначные проекты не выбираются случайно,
Configuration/TFM проверяются по реально загруженным inner projects. Диагностика
различает missing output, unconfirmed, foreign и access failure.

Автосборка генераторов и универсальное восстановление любых MSBuild-путей вне эпохи.

# Ответы: Epoch 0

Исходные замечания: [review/epoch-0-feasibility.md](../review/epoch-0-feasibility.md). O/A: [основания](sources-and-scope.md).

## E0-01

**Вывод: ACCEPT.**

**Основание.** O6 допускает другой host, но O8/A-WRITE запрещают потерю возможностей записи и ложный успех. Таблица свойств DTO сама по себе этого не проверяет. Выбор host и сохранения должен предшествовать разрешению production lifecycle, иначе Epoch 1 может оказаться повторным исследованием выполнимости.

**Предлагаемые изменения.** Epoch 0 «Работа» п.2–4 и «Приёмка»: результат выбора host, способ гидрации без скрытого DTB, host services/analyzer loader и feasibility операций V07. MSBuild без Open, Adhoc/ProjectInfo, Open+подмена — различимые варианты; не требовать трёх дорогостоящих прототипов, если API уже позволяет аргументированно исключить вариант. Open+подмена не засчитывается как skip-DTB. В Epoch 1 поступает определённая граница записи, не обещание «публичных API достаточно».

**Последствия.** Внутренние API/capabilities, переносимые project identities, tests читающих и пишущих tools. Без решения нет go на production hydrate. Тестовый host не заменяет fresh MSBuild oracle (V-01). N2/N6 остаются отдельными препятствиями, даже если ProjectInfo создан успешно.

## E0-02

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Распознаваемый отказ до hit необходим по O2/O8. Объявить feature unsupported без способа обнаружить её недостаточно. Но корректный negative test не является красной equivalence-проверкой: это успешная проверка admission. V2 verification уже запрещает считать skipped/unsupported равным passed equivalence. Поддержка generator для всего первого профиля зависит от реальной цели A-02; нельзя автоматически требовать её из одного наличия overlay в продукте.

**Предлагаемые изменения.** Epoch 0 п.5/«Приёмка», contract §4 и V12: разделить positive supported, negative rejected и untested; для каждого исключения показать detector и момент отказа. Обязательные positive features определить по целевой нагрузке, отдельно от списка negative fixtures. Не выпускать профиль, пригодность которого нельзя доказать без неизвестных предположений.

**Последствия.** Versioned admission rules, evidence matrix и stop condition для нераспознаваемого профиля. Число зелёных negative tests не доказывает покрытие продукта. Более широкий вопрос первоначальной обязательности O5/O7 — A-01, не техническое разрешение сузить всё до двух проектов.

## E0-03

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Отсутствие CS0103 и name-search не доказывают тело/версию генерации; принимается. Но in-process oracle уже существует: [SourceGeneratorOracle.ReadAsync](../../../../RoslynMcpServer.LifecycleTestHost/SourceGeneratorOracle.cs:14) проверяет exact ConstantValue и читает GetSourceGeneratedDocumentsAsync. План verification «Независимый oracle» уже требует emitted sources. Следовательно, отсутствие публичного MCP tool не является блокером или свидетельством отсутствия наблюдаемости.

**Предлагаемые изменения.** Epoch 0 «Приёмка», V04: явно использовать существующий test host с production published accessor и независимой fresh MSBuild веткой. Для equivalence всех generated outputs обязательны полный набор/тексты/diagnostics; текущий поиск одного документа по имени и `Success=true` при unavailable GeneratedText недостаточны. Не засчитывать маркер как доказательство совпадения всего generated set.

**Последствия.** Расширение тестового результата/assertions без нового production MCP инструмента. Test API failure — not run/failed согласно причине, не пустой успешный output. Свежие процессы для independent generator identity; тестовый oracle не должен запускать raw branch раньше production и менять loader state (N6).

## E0-04

**Вывод: PARTIALLY ACCEPT.**

**Основание.** O1 требует реальной пользы; полное время должно включать probes, capture/prepare по выбранному пути, compilation и первый полезный ответ. Но review ошибочно говорит о замене критерия в Epoch 2: её «Приёмка» прямо требует performance-gate Epoch 0. 70% в Epoch 0 — предложение бюджета, не первичное требование пользователя. Запрет продолжать любой функциональный эксперимент без большого решения был бы сильнее установленной цели.

**Предлагаемые изменения.** Epoch 0 handoff и общий handoff: отдельно разрешение продолжить experiment и production go. Epoch 2/verification release gate: утверждённый до измерений workload/budget, end-to-end и отсутствие regressions, а не один DTB count. Не объявлять полевую полезность доказанной при отсутствии большого решения. Порог согласовать до результатов.

**Последствия.** Release decision и observability; никаких новых runtime defaults. Измерять и попадания, и misses/неудачные capture; отделять OS cache от нового PID. Scope обязательной нагрузки остаётся A-02; экспериментальный go не должен автоматически разрешать публичный cache feature.

## E0-05

**Вывод: NEEDS CLARIFICATION.**

**Основание.** O7 в первоначальном плане выделяет metadata fast-open в самостоятельную первую эпоху. V2 относит его к дополнительному исследованию, review предлагает перенести в optional. Без первичного задания нельзя решить, обязательна ли оценка более простого ускорения прежде собственного store. Само независимое исследование не подменяет evidence дискового кэша; смешивать их результаты действительно нельзя.

**Что требуется уточнить.** A-01: этот проход рассматривает только кэш или весь первоначальный маршрут ускорения; должна ли Epoch 0 сравнить его с более простым metadata вариантом. Если исследование остаётся, у него отдельный outcome и измерения, не go для full-source hydrate.

**Затрагиваемые разделы и последствия.** Epoch 0 п.6, Epoch 4A и handoff dependencies. Перенос меняет порядок и объём исследований, не runtime. Будущий metadata API обязан иметь свой scope/identity/fallback contract; никаких автоматических metadata substitutions в Epoch 2 из этого решения не следует.

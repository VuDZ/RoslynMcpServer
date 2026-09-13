# Ответы: проверка корректности и производительности

Исходные замечания: [review/verification.md](../review/verification.md). O/A: [основания](sources-and-scope.md).

## V-01

**Вывод: REJECT.**

**Основание.** Эталоном результата при тех же входах является fresh MSBuild load, а объектом проверки — production hydrate host. Одинаковый тип host не является условием независимого differential test. Если generator/options/persistence отличаются, это полезный обнаруженный дефект или обоснованное исключение из профиля, а не доказательство некорректного oracle. Сравнение двух одинаково построенных DTO hosts, напротив, способно воспроизвести общую ошибку и потерять независимость. O6 допускает Adhoc; contract §1 запрещает нормализовать реальные семантические расхождения. Нормализация случайных IDs не удаляет различия generated output или записи.

**Предлагаемые изменения.** Предложенный запрет сравнивать Adhoc со fresh MSBuild не принимать. Сохранить oracle; обеспечить оба пути через production lifecycle и сравнивать наблюдаемый результат, не CLR type equality. E0-03/C-09/E1-01 отдельно конкретизируют generated text, decoding и persistence. Пропущенный обязательный тест не становится passed вследствие нормализации.

**Последствия.** Два независимых пути исполнения с одинаковыми properties/toolset/overlay policy и контролируемым деревом; свежие процессы избегают загрязнения process-lifetime loader. Тестовый fixture DTB count не заменяет e2e. Ни новых public API, ни замены oracle на mock не требуется.

## V-02

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Изоляция membership detector улучшит тест; fallback equality не доказывает hydrate. Но она не бессмысленна: ошибочный fallback может загрузить иной scope/properties или опубликовать stale candidate. Кроме того, вся строка V11 включает Compile Remove: изменение `.csproj` законно даёт inputs_changed. Требовать membership_changed для всех её случаев неверно.

**Предлагаемые изменения.** V11 разделить: добавление/удаление compile file при неизменных старых hashes и csproj → membership_changed; Compile Remove/изменение graph input → соответствующая причина; во всех случаях проверить отсутствие disk-hit и корректный fallback. Положительный неизменный hit — отдельная обязательная проверка. Non-member creation нужен как контроль отсутствия ненужного invalidation.

**Последствия.** Test fixtures, reason precedence и instrumentation admission/DTB, без новых product параметров. Negative tests не заменяют positive hydrate matrix. Действующий профиль должен быть eligible до мутации, иначе unsupported маскирует непроверенный detector.

## V-03

**Вывод: PARTIALLY ACCEPT.**

**Основание.** Generated outputs/marker необходимы, если generator входит в поддержанный профиль; это уже требует verification. In-process oracle [существует](../../../../RoslynMcpServer.LifecycleTestHost/SourceGeneratorOracle.cs:14), поэтому отсутствие публичного MCP инструмента не препятствует тестированию. Утверждение «Adhoc vs MSBuild точно разъедется» не подтверждено экспериментом. Наличие SG в продукте не доказывает требование cache-hit для любого SG-проекта; обычный fallback сохраняет функцию, но может не достичь performance цели конкретной монорепы.

**Предлагаемые изменения.** V04 и Epoch 0 «Приёмка»: supported SG fixture обязан пройти полный generated-text/marker/diagnostics oracle; rejected profile проходит только negative admission, не equivalence. Фичи обязательного positive профиля определить по A-02. Test host должен считать недоступные обязательные generated outputs ошибкой, а не successful marker-only observation.

**Последствия.** Tests, profile coverage и meaningful release gate. Analyzer/helper/main-only/restart cases идут с одинаковой политикой в baseline и hydrate; disk-hit не обходит A-ADMISSION. Если обязательное целевое решение содержит неподдержанный SG, для этой цели go запрещён; это не универсальный запрет всех иных профилей.

## V-04

**Вывод: PARTIALLY ACCEPT.**

**Основание.** O1 требует пользы на большом решении, поэтому fixture-only не доказывает готовность фичи для целевой нагрузки. Но Epoch 2 уже требует perf gate; review преувеличивает отсутствие требования. Ноль «полевых пользователей» до выпуска сам по себе не дефект: достаточно воспроизводимой репрезентативной проверки, если scope и budget согласованы. Порог 70% предложен v2 и не является подтверждённым первичным требованием.

**Предлагаемые изменения.** Verification «Release gates» должен явно требовать **прохождения**, а не только фиксации заранее выбранного бюджета; связать его с Epoch 2 и обязательной нагрузкой A-02. Включить hit-rate по всем load attempts, coverage/reasons, first-semantic, miss overhead и capture cost. Fixture-only обозначать experiment; публикацию flags отделить от разрешения продолжить исследование.

**Последствия.** Handoff/release metadata и критерии выпуска, не новый runtime. Defaults false не заменяют performance evidence. Если accepted budget не выполнен — revise/ограниченный эксперимент, без утверждения цели достигнутой; точный формат распространения экспериментального API ещё нуждается в решении H-01.

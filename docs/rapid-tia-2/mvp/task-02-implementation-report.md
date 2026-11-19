# Task-02 — отчёт реализации

Дата: 2026-11-18. Состояние: **accepted**. Координатор завершил финальную проверку под SDK **10.0.402**, Release: focused **518/518**, включая **289 fingerprint + 106 прежних IL + 123 SourceStructure**, main **2405/2405**, без пропусков, ошибок и таймаутов. [Финальная validation и timing](evidence/task-02-final-validation-sdk10-2026-11-18.md), [независимое ревью раунда 4 — ACCEPT](evidence/task-02-review-round-4.md). Четыре раунда ревью, три раунда исправлений; D-01 закрыт решением владельца M-09 и отдельной регрессией. Ниже сохранена история промежуточных кандидатов.

Scope: внутренние канонические отпечатки типов верхнего уровня и сборки из `IlAssemblyImage`, сравнение снимков. Интеграция инструмента, lifecycle workspace, task-03, версия и CHANGELOG не меняются.

## Проверки

Фильтр: `FullyQualifiedName~RoslynMcpServer.Tests.Testing.Il.IlFingerprint`.

Красный прогон SDK 10.0.402 выполнял координатор: 141 тест, 140 ожидаемых падений `NotImplementedException`, один преждевременный pass — слишком широкий `ThrowsAny<Exception>` в проверке повреждённого образа. Эта проверка исправлена на точный `BadImageFormatException`. До реализации также исправлены два дефекта самого test packet: неоднозначный overload `WriteBytes` и сортировка `GenericParam` по coded owner. Логи координатора: `TestResults/task02-sdk10/red.log`, `red.trx`.

Первый кандидат: 146 тестов, 145 pass, одно падение sequence-assertion для `ImmutableArray`; ожидание исправлено на явный массив `IlTypeName[]`. Это ещё не итоговый зелёный прогон. Дополнительные атомарные и граничные тесты добавлены при проверке полноты после начала реализации; первоначальный red не выдаётся за их индивидуальный red/mutation-прогон. Логи: `TestResults/task02-sdk10/focused-1.log`, `focused-1.trx`.

Расширенный кандидат: 430 тестов, 426 pass, четыре падения обнаружили неверное сопоставление CA → DeclSecurity в metadata fixture. `MetadataBuilder` сортировал DeclSecurity по coded owner, а CA использовали исходные row handles. Fixture исправлен: MethodDef (coded 5), Assembly (6), TypeDef (8) создаются сразу в каноническом порядке. Эти четыре теста являются отдельным red-свидетельством маршрутизации security attributes. Логи: `TestResults/task02-sdk10/focused-3.log`, `focused-3.trx`.

После исправления: **430/430 pass, 0 skips**, SDK **10.0.402**, Release: **201 новых fingerprint-тестов + 106 существующих IL-тестов + 123 SourceStructure**. Логи координатора: `TestResults/task02-sdk10/focused-4.log`, `focused-4.trx`. Это зелёный кандидат для независимого ревью; полный main suite, timing и приёмка пока не подтверждены.

Независимое ревью раунда 1 вернуло MUST-FIX: неоднозначная native-разрядность FieldRVA, потеря типа пустого/null массива named argument, два неатомарных свидетеля BaseType/InterfaceImpl. До исправлений поведения добавлены 22 регрессии `IlFingerprintReviewRegressionTests`: SDK10 red — **12 fail, 10 pass, 0 skips** (`TestResults/task02-sdk10/review-1-red.log`, `review-1-red.trx`). Восемь fail — отсутствие отказа AnyCPU RVA, четыре — коллизии typed named arrays. Проходящие случаи — fixed-bitness controls и два новых независимых metadata witnesses уже существовавшего правильного поведения BaseType/InterfaceImpl. После red добавлены ещё четыре passing controls для однозначного mixed-mode PE32.

Исправления раунда 1: **456/456 pass, 0 skips**, SDK10 Release — **227 fingerprint + 106 прежних IL + 123 SourceStructure** (`TestResults/task02-sdk10/review-1-green.log`, `review-1-green.trx`). Индивидуальные omission-прогоны BaseType/InterfaceImpl проведены отдельно с сохранением и восстановлением точных исходных байтов reader; зелёное состояние не объявляется их red-свидетельством.

Оба omission-свидетельства подтверждены координатором под SDK10, фильтр `FullyQualifiedName~RoslynMcpServer.Tests.Testing.Il.IlFingerprintReviewRegressionTests.Base_and_interface_metadata_change_without_member_or_il_changes`:

- Исключён только вклад BaseType: **2 executed, 1 fail (base-type), 1 pass (interface-type), 0 skips** — `TestResults/task02-sdk10/mutation-base.log`, `mutation-base.trx`.
- После точного восстановления исключён только вклад interfaces: **2 executed, 1 fail (interface-type), 1 pass (base-type), 0 skips** — `TestResults/task02-sdk10/mutation-interfaces.log`, `mutation-interfaces.trx`.

В каждом случае падение происходит на требовании неравенства отпечатков A, при неизменных методах, сигнатурах и сырых IL. После двух временных мутаций `IlFingerprintReader.cs` восстановлен побайтово; SHA256 до и после: `959F1E5994D32EB8831AA7905109B91480666773708D453648D139274DEB08FF`. Ни одна временная omission-правка в финальный кандидат не входит. Восстановленный код повторно прошёл SDK10 Release: **456/456 pass, 0 skips** (`TestResults/task02-sdk10/review-1-restored-green.log`, `review-1-restored-green.trx`), после чего передан в независимое ревью раунда 2.

Независимое ревью раунда 2 закрыло все три замечания первого раунда и обнаружило R2-F-01: raw FieldMarshal сохранял версию solution-сборки внутри MarshalTypeRef. До исправления добавлены 12 paired-compilation регрессий `IlFingerprintMarshallingTests`: **3 fail** (solution-version invariance отдельно на поле/параметре/return), **9 pass, 0 skips** (внешняя версия без AssemblyRef, другой marshaler-тип, cookie как обычный текст, каждый на тех же трёх владельцах). Координатор подтвердил red под SDK10: `TestResults/task02-sdk10/review-2-red.log`, `review-2-red.trx`.

Исправление R2-F-01 использует общий decoder FieldMarshal: в CustomMarshaler канонизируется только типизированное managed-type имя; GUID/native-type prefix и cookie suffix сохраняются побайтово. Версия solution/собственной сборки исключается из managed-type identity, версия внешней сборки сохраняется при каждом соответствующем type leaf, даже без строки AssemblyRef. Прочие Marshal descriptors сохраняются целиком.

Координатор подтвердил green исправления R2-F-01: **468/468 pass, 0 skips**, SDK **10.0.402**, Release — **239 fingerprint + 106 прежних IL + 123 SourceStructure** (`TestResults/task02-sdk10/review-2-green.log`, `review-2-green.trx`). Код и тесты заморожены для независимого ревью раунда 3; эта запись не означает приёмку или полный main-suite pass.

Независимое ревью раунда 3 закрыло CustomMarshaler R2-F-01 и нашло два других typed-name пути: SafeArrayUserDefinedSubType в FieldMarshal и бинарный DeclSecurity. До изменения поведения добавлены **49** случаев: SDK10 red — **16 fail, 33 pass, 0 skips** (`TestResults/task02-sdk10/review-3-red.log`, `review-3-red.trx`). Три fail — SafeArray solution-version для field/parameter/return; девять — binary permission solution-version для outer SecurityAttribute / System.Type value / enum typed name, каждый у type/method/assembly; четыре — требование отказа для неполных binary payloads. Остальные 33 контроли сохраняют внешние версии без AssemblyRef, subtype/type identity, числовые и обычные string values и точный owner.

Исправление R3-F-01 расширяет общий FieldMarshal decoder на SafeArray: variant subtype и остальные байты сохраняются, optional typed user subtype нормализуется с той же solution/external политикой, что CustomMarshaler. Исправление R3-F-02 разбирает структурно полный binary permission set, проверяет границы/counts и полное потребление payload. Outer security type и typed named values используют version policy, обычные данные не очищаются; общий named-value parser сохраняет enum-width alternatives. Прежние `[0x2E, 1]` security-value fixtures заменены полным permission attribute с настоящим числовым named property. Security-регрессии используют MetadataBuilder, не выдаются за C# CAS compilation; пакетов CAS не добавлено.

Координатор подтвердил R3 green: **517/517 pass, 0 skips**, SDK **10.0.402**, Release — **288 fingerprint + 106 прежних IL + 123 SourceStructure** (`TestResults/task02-sdk10/review-3-green.log`, `review-3-green.trx`). Код и тесты заморожены для независимого ревью раунда 4; task-02 не принят этой записью.

D-01 ревью раунда 3 закрыт явным ответом владельца: **«Зафиксировать ограничение: перенумерация generated-членов может дать лишние изменения, как у анонимных типов (рекомендуется для MVP).»** Это разрешает записать консервативную границу, а не нормализовать generated names или cache fields. Production-код по D-01 не менялся; координатор записал решение в M-09/D-09, уточнил task-02 и обновил статусы после финальной validation.

Один deterministic compilation-тест `IlFingerprintTests.Generated_member_renumbering_after_lambda_method_reorder_conservatively_changes_owner` меняет только порядок M/N, возвращающих разные лямбды. Повторная компиляция первого исходника даёт точные те же байты; из метаданных доказывается смена generated method identities M и N. Snapshot comparison сообщает только A, B и assembly fingerprint неизменны. Это свидетель принятого лишнего выбора, а не нормализации generated identities. Координатор подтвердил его pass в focused 518/518 и повторном финальном main 2405/2405. Production fix и red-before-fix для разрешённого поведения не требуются.

`IlFingerprintMetadataFixture` задаёт атомарные изменения независимо: например, `field-rva` меняет только четыре байта начальных данных при неизменных именах, сигнатурах и IL. В `IlFingerprintTests` есть предварительные доказательства неравенства сырых IL для сдвига токенов и равенства сырых IL для смены catch-типа.

## Факт → тест

Обозначение `M(key)` — отдельный случай `IlFingerprintMetadataTests.Each_metadata_fact_changes_its_top_level_owner(key)`; `A(key)` — отдельный случай `Each_assembly_fact_changes_only_assembly_fingerprint(key)`; `S(key)` — отдельный случай `IlFingerprintTests.Each_source_fact_changes_its_owner_only`. Эти случаи проверяют изменение A и сохранение B. `M` дополнительно проверяет неизменность assembly hash (кроме общего изменения конструктора атрибутов).

| Атомарный факт | Свидетель |
| --- | --- |
| Имя типа с вложенностью | `M(nested-name)`; added/removed в `Comparison_separates_changed_added_removed_and_assembly_change` |
| Доступ типа | `Each_type_fact_changes_its_owner_only`: public → internal |
| Вид типа | `Each_type_fact_changes_its_owner_only`: class → struct |
| Sealed | `Each_type_fact_changes_its_owner_only`: sealed |
| Abstract | `Each_type_fact_changes_its_owner_only`: abstract |
| Базовый тип | `IlFingerprintReviewRegressionTests.Base_and_interface_metadata_change_without_member_or_il_changes(base-type)` — только BaseType, без constructor IL changes; прежний source-case остаётся дополнительным, не атомарным доказательством |
| Интерфейс | `IlFingerprintReviewRegressionTests.Base_and_interface_metadata_change_without_member_or_il_changes(interface-type)` — только InterfaceImpl.Interface, при неизменных методах, сигнатурах и IL; source-case с добавлением Dispose не считается атомарным доказательством |
| Вид раскладки | `S(layout-kind)` |
| Размер | `M(layout-size)`, `S(layout-size)` |
| Упаковка | `M(layout-pack)`, `S(layout-pack)` |
| Имя generic-параметра типа | `M(type-generic-name)` |
| Флаги generic-параметра типа | `M(type-generic-flags)` |
| Вариантность generic-параметра типа | `S(variance)` |
| Ограничение generic-параметра типа | `M(type-generic-constraint)`, `S(type-constraint)` |
| Имя generic-параметра метода | `M(method-generic-name)` |
| Флаги generic-параметра метода | `M(method-generic-flags)` |
| Ограничение generic-параметра метода | `M(method-generic-constraint)`, `S(method-constraint)` |
| Имя поля | `M(field-name)` |
| Сигнатура поля | `M(field-signature)` |
| Флаги поля | `M(field-flags)` |
| Константа поля | `M(field-constant)` |
| Смещение поля | `M(field-offset)`, `S(field-offset)` |
| Начальные данные поля | `M(field-rva)`; независимость имени/IL: `Rva_witness_changes_payload_without_changing_field_identity_or_il` |
| Native-sized RVA / вложенная native layout | `IlFingerprintReviewRegressionTests.Anycpu_runtime_dependent_rva_storage_is_rejected`; native int, pointer, function pointer, nested native field, обе версии с изменением старшего слова; `Fixed_bitness_rva_storage_uses_its_complete_extent` — PE32Plus, Requires32Bit и mixed-mode PE32 |
| Маршалинг поля | `M(field-marshal)`, `S(field-marshal)` |
| Порядок полей | `S(field-order)` |
| Имя метода | `M(method-name)` |
| Сигнатура метода | `M(method-signature)`, `S(method-signature)` |
| Флаги метода | `M(method-flags)`, `S(method-access)` |
| Флаги реализации | `M(method-impl-flags)`, `S(method-impl-flags)` |
| IL | `S(body)`, `S(string)`; отдельная проверка UTF-16: `String_tokens_preserve_unpaired_utf16_code_units` |
| Локальная сигнатура | `M(local-signature)` |
| InitLocals / MaxStack | `M(local-init)` / `M(max-stack)` |
| EH kind | `M(exception-kind)` |
| EH try start | `M(exception-try-offset)` |
| EH try length | `M(exception-try-length)` |
| EH handler start | `M(exception-handler-offset)` |
| EH handler length | `M(exception-handler-length)` |
| EH catch type | `Catch_type_changes_with_identical_il_are_detected` |
| EH filter offset | `M(exception-filter-offset)` |
| EH filter/finally behavior | `S(filter)` / `S(finally)` |
| MethodImpl body | `M(method-implementation-body)` |
| MethodImpl declaration | `M(method-implementation-declaration)` |
| P/Invoke module | `M(pinvoke-module)` |
| P/Invoke EntryPoint | `M(pinvoke-entry)` |
| P/Invoke flags | `M(pinvoke-flags)` |
| Имя параметра | `M(parameter-name)` |
| Флаги параметра | `M(parameter-flags)` |
| Default параметра | `M(parameter-default)`, `S(default)` |
| Marshal параметра | `M(parameter-marshal)` |
| Имя возвращаемого значения (sequence=0) | `M(return-name)` |
| Флаги возвращаемого значения | `M(return-flags)` |
| Default возвращаемого значения | `M(return-default)` |
| Marshal возвращаемого значения | `M(return-marshal)` |
| MarshalTypeRef версии solution-сборки на поле / параметре / return | `IlFingerprintMarshallingTests.Custom_marshaler_solution_version_preserves_consumer_fingerprints` — три отдельных случая с доказательством отличия сырых descriptors |
| Внешняя версия marshaler без AssemblyRef | `IlFingerprintMarshallingTests.External_marshaler_version_without_an_assembly_reference_is_detected` — три случая; отсутствие Library AssemblyRef проверено непосредственно по metadata; требуется изменение type либо assembly hash |
| Идентичность managed marshaler-типа | `IlFingerprintMarshallingTests.Custom_marshaler_type_identity_is_preserved` — три случая, меняется только C, B и assembly hash неизменны |
| Cookie custom marshaler как обычный текст | `IlFingerprintMarshallingTests.Custom_marshaler_cookie_preserves_ordinary_assembly_version_text` — три случая с cookie, содержащим assembly-qualified version text; нормализация cookie не допускается |
| SafeArrayUserDefinedSubType версии solution | `IlFingerprintMarshallingTests.Safe_array_solution_version_preserves_consumer_fingerprints` — поле/параметр/return, raw descriptor inequality |
| SafeArray external version без AssemblyRef | `Safe_array_external_version_without_an_assembly_reference_is_detected` — три владельца, отсутствие Library AssemblyRef доказано |
| SafeArray typed identity / variant subtype / descriptor без user type | `Safe_array_user_defined_type_identity_is_preserved`, `Safe_array_subtype_is_preserved`, `Safe_array_without_a_user_type_preserves_remaining_descriptor` — каждый на трёх владельцах |
| Имя свойства | `M(property-name)` |
| Сигнатура свойства | `M(property-signature)` |
| Флаги свойства | `M(property-flags)` |
| Default свойства | `M(property-default)` |
| MethodSemantics kind свойства | `M(property-semantics-kind)` |
| Getter / Setter / Other | `M(property-semantics-method)` / `M(property-setter-method)` / `M(property-other-method)` |
| Имя события | `M(event-name)` |
| Сигнатура события | `M(event-signature)` |
| Флаги события | `M(event-flags)` |
| MethodSemantics kind события | `M(event-semantics-kind)` |
| Adder / Remover / Raiser / Other | `M(event-semantics-method)` / `M(event-remover-method)` / `M(event-raiser-method)` / `M(event-other-method)` |
| CustomAttribute constructor на типе, nested type, поле, методе, параметре, return, свойстве, событии, generic типа/метода, InterfaceImpl, constraint типа/метода | Независимый `M(<owner>-attribute-constructor)` для каждого из перечисленных owner: `type`, `nested`, `field`, `method`, `parameter`, `return`, `property`, `event`, `type-generic`, `method-generic`, `interface`, `type-constraint`, `method-constraint` |
| CustomAttribute value на каждом из тех же владельцев | Независимый `M(<owner>-attribute)` для каждого из тех же 13 owner |
| Тип элемента пустого/null массива в named CustomAttribute argument | `IlFingerprintReviewRegressionTests.Named_empty_and_null_arrays_keep_their_element_type` — независимые int[]/long[] потребители одного `.ctor(int)`, field/property, empty/null; разные raw blobs при неизменных B и assembly hash |
| DeclSecurity type action / value | `M(type-security-action)` / `M(type-security-value)` |
| DeclSecurity method action / value | `M(method-security-action)` / `M(method-security-value)` |
| Атрибут DeclSecurity типа/метода: constructor / value | `M(type-security-attribute-constructor)` / `M(type-security-attribute)`; `M(method-security-attribute-constructor)` / `M(method-security-attribute)` |
| Атрибуты сборки/модуля: constructor / value | `A(assembly-attribute-constructor)` / `A(assembly-attribute)`; `A(module-attribute-constructor)` / `A(module-attribute)` |
| Assembly security action / value / attribute constructor / attribute value | `A(assembly-security-action)` / `A(assembly-security-value)` / `A(assembly-security-attribute-constructor)` / `A(assembly-security-attribute)` |
| Binary permission solution-version: outer SecurityAttribute / System.Type value / enum typed name | `IlFingerprintSecurityTests.Solution_version_in_binary_permission_typed_names_is_excluded` — девять независимых type/method/assembly × typed-kind случаев, raw payload inequality |
| Binary permission external typed version без AssemblyRef | `External_permission_typed_versions_without_assembly_references_are_detected` — те же девять owner/kind случаев, явная проверка отсутствия AssemblyRef |
| Security attribute identity / action / ordinary string / number | `Permission_identity_action_and_ordinary_values_keep_their_owner` — 12 отдельных owner/fact случаев; string содержит version-looking текст |
| Неполный бинарный permission payload | `Incomplete_binary_permission_sets_never_produce_snapshots` — missing entry, trailing data, short argument block, missing named value; требуется точный BadImageFormatException |
| Embedded resource name / content / flags | `A(resource-name)` / `A(resource-content)` / `A(resource-flags)` |
| ExportedType name / target / flags | `A(forwarder-name)` / `A(forwarder-target)` / `A(forwarder-flags)` |
| AssemblyRef name / external version | `A(assembly-reference-name)` / `A(assembly-reference-version)` |
| File name / hash / ContainsMetadata | `A(file-name)` / `A(file-hash)` / `A(file-metadata)` |
| Атрибуты reference/file/resource/exported rows | Отдельные `A(assembly-reference-attribute)`, `A(module-reference-attribute)`, `A(type-reference-attribute)`, `A(member-reference-attribute)`, `A(standalone-signature-attribute)`, `A(type-specification-attribute)`, `A(method-specification-attribute)`, `A(file-attribute)`, `A(resource-attribute)`, `A(exported-attribute)` |

Метаданные method generic variance не определяют корректную CLI-вариантность: ECMA-335 разрешает covariance/contravariance для параметров generic-интерфейсов и делегатов. Все байты GenericParam.Attributes сохраняются и для метода, отдельный тест его флагов есть; непозволительная CLI-вариантность не выдаётся за поддерживаемый пользовательский сценарий.

## Сквозные свойства

- Комментарий/пробелы/порядок методов/имя локальной: `Source_only_edits_preserve_fingerprints`.
- Повторный буфер/компиляция/путь/PDB/MVID: `Recompilation_path_pdb_and_mvid_are_excluded`, `Metadata_mvid_is_excluded`; путь всегда вымышленный, повторного открытия нет.
- Сдвиг token rows в IL с raw-IL witness: `New_member_shifts_raw_tokens_but_preserves_unrelated_type`.
- Сдвиг token rows сигнатур с raw-signature witness: `Signature_and_local_tokens_are_canonical_after_new_type_references`.
- Типы из разных сборок: `Same_full_name_in_different_assemblies_is_a_different_token`, обе ссылки уже есть; вложенность: `S(nested-token-identity)`.
- Перекомпиляция зависимого C: отдельные const/enum/overload/unused/version случаи `Recompiled_dependent_tracks_only_compiled_behavior`.
- Собственные версии: `Build_versions_are_excluded`; похожий пользовательский атрибут сохраняется в `A_lookalike_version_attribute_is_retained`.
- typeof(solution type) с изменившейся версией: `Typeof_attribute_values_normalize_solution_versions_and_keep_nested_generic_identity`; byte/short/int/long внешнего enum перед typeof: `External_enum_before_typeof_does_not_break_solution_version_normalization`.
- MarshalTypeRef(solution type) с изменившейся версией и внешняя версия без AssemblyRef: отдельные `IlFingerprintMarshallingTests`, поле/параметр/return; managed-type normalization отделена от побайтового сохранения cookie.
- SafeArrayUserDefinedSubType и binary permission typed names проверяются отдельно; CAS runtime enforcement не исполняется и не заявляется. Binary permission external typed values не зависят от наличия AssemblyRef.
- Обычные строки атрибутов не нормализуются: `Ordinary_attribute_strings_keep_assembly_version_text`; nested/generic typeof сохраняются: `Serialized_type_values_preserve_nested_generic_arguments`; boxed null/arrays: `Boxed_null_and_attribute_arrays_are_read_completely`.
- MethodSemantics/MethodImpl/GenericParamConstraint/CustomAttribute row order: `Semantics_implementation_constraint_and_attribute_row_order_is_excluded`; interfaces/constraints/attributes/TypeDef order: `Interface_constraint_attribute_and_type_row_orders_are_excluded`.
- Сравнение changed/added/removed и assembly flag, запрет сравнения разных сборок: `Comparison_separates_changed_added_removed_and_assembly_change`.
- Анонимные типы: `Anonymous_type_renumbering_selects_every_changed_user`; lambda/async/nested: `S(lambda)`, `S(async)`, `S(nested)`.
- Принятая владельцем консервативная граница generated renumbering при перестановке методов с лямбдами: `Generated_member_renumbering_after_lambda_method_reorder_conservatively_changes_owner`; metadata identity witness, только A changed, B/assembly stable.
- Повреждённый образ не даёт частичного результата: `Broken_images_do_not_produce_partial_snapshots`; произвольные ошибки обязательной секции не перехватываются с выдачей hash.

## Полнота таблиц ECMA-335

Все таблицы раздела II.22 ECMA-335 перечислены ниже. Строки таблиц и heap offsets не хешируются непосредственно.

| ID | Таблица | Назначение |
| --- | --- | --- |
| 00 | Module | Атрибуты → сборка; MVID, generation ID, имя файла исключаются как идентификаторы сборки/отладки |
| 01 | TypeRef | Каноническая пара простого имени сборки и полного metadata-имени с вложенностью по ResolutionScope |
| 02 | TypeDef | Тип: имя, флаги, база, содержимое; вложенные типы включены во владельца верхнего уровня |
| 03 | FieldPtr | Разыменование через MetadataReader; собственные номера строк не входят |
| 04 | Field | Тип; порядок полей сохраняется |
| 05 | MethodPtr | Разыменование через MetadataReader; собственный порядок исключён |
| 06 | MethodDef | Тип: имя, сигнатура, флаги, тело; по ссылке → канонический член |
| 07 | ParamPtr | Разыменование через MetadataReader |
| 08 | Param | Тип: sequence, имя, флаги, default, marshal, атрибуты, включая sequence=0 |
| 09 | InterfaceImpl | Тип: канонический интерфейс и атрибуты, сортированный набор |
| 0A | MemberRef | По ссылке → владелец, имя, каноническая сигнатура |
| 0B | Constant | Тип: type code и байты значения поля, параметра, свойства |
| 0C | CustomAttribute | Тип/сборка по владельцу: канонический конструктор и значение; CA на DeclSecurity остаётся у владельца security; остальные reference/file/resource annotations → сборка по каноническому parent; версия File/Informational исключена только для BCL assembly attributes |
| 0D | FieldMarshal | Тип: descriptor поля/параметра/возврата; typed managed-type имя CustomMarshaler и SafeArray user subtype канонизируются с исключением только solution/собственной версии и сохранением внешней версии; остальные байты, включая variant subtype и cookie, сохраняются |
| 0E | DeclSecurity | Тип/метод/сборка по владельцу: action и полный permission-set; binary outer attribute names и typed named values канонизируются с solution/external version policy; обычные значения и nonbinary legacy data сохраняются |
| 0F | ClassLayout | Тип: packing и размер; вид раскладки из TypeDef |
| 10 | FieldLayout | Тип: смещение поля |
| 11 | StandAloneSig | По ссылке → каноническая локальная/методная сигнатура |
| 12 | EventMap | Разыменование принадлежности событий типу |
| 13 | EventPtr | Разыменование через MetadataReader |
| 14 | Event | Тип: имя, event type, флаги, атрибуты, MethodSemantics |
| 15 | PropertyMap | Разыменование принадлежности свойств типу |
| 16 | PropertyPtr | Разыменование через MetadataReader |
| 17 | Property | Тип: имя, сигнатура, флаги, default, атрибуты, MethodSemantics |
| 18 | MethodSemantics | Тип через соответствующее свойство/событие: роль и канонический метод |
| 19 | MethodImpl | Тип: канонические пары body/declaration |
| 1A | ModuleRef | По ссылке: имя P/Invoke-модуля; область TypeRef внутри сборки |
| 1B | TypeSpec | По ссылке → полная каноническая сигнатура типа |
| 1C | ImplMap | Тип: модуль, EntryPoint, флаги |
| 1D | FieldRVA | Тип: начальные данные точного размера поля, без RVA/section padding |
| 1E | ENCLog | Исключён: журнал Edit and Continue, не содержимое итоговой сборки |
| 1F | ENCMap | Исключён: карта Edit and Continue, не содержимое итоговой сборки |
| 20 | Assembly | Сборка: идентичность без собственной версии |
| 21 | AssemblyProcessor | Исключён: устаревшая платформенная подсказка, современный CLR игнорирует |
| 22 | AssemblyOS | Исключён: устаревшая платформенная подсказка, современный CLR игнорирует |
| 23 | AssemblyRef | Сборка: имя; версия только для внешней сборки; ссылки на типы используют простое имя |
| 24 | AssemblyRefProcessor | Исключён: устаревшая платформенная подсказка, современный CLR игнорирует |
| 25 | AssemblyRefOS | Исключён: устаревшая платформенная подсказка, современный CLR игнорирует |
| 26 | File | Сборка: имя, hash, ContainsMetadata; связь linked resource/ExportedType |
| 27 | ExportedType | Сборка: полное имя с вложенностью, флаги и каноническая цель пересылки |
| 28 | ManifestResource | Сборка: имя, флаги и embedded-байты либо каноническая linked-цель |
| 29 | NestedClass | Владелец верхнего уровня и полная идентичность вложенного типа |
| 2A | GenericParam | Тип/метод: index, имя, флаги и атрибуты |
| 2B | MethodSpec | По ссылке → канонический метод и аргументы обобщения |
| 2C | GenericParamConstraint | Тип/метод: канонический тип ограничения и атрибуты |

Зарезервированные номера 2D–2F не определяют таблиц ECMA-335. Portable PDB tables 30–37 (Document, MethodDebugInformation, LocalScope, LocalVariable, LocalConstant, ImportScope, StateMachineMethod, CustomDebugInformation) исключены: PDB и debug directory не являются входом отпечатка.

## Границы принятого результата

- Итоговый расширенный набор и полная main suite прошли; карта фактов и реализация приняты независимым ревью раунда 4. Открытых MUST-FIX нет.
- Внешний enum: parser сохраняет все полные интерпретации, согласуя ширину по идентичности enum; не выбирает первую и не теряет байты значения. При более 4096 промежуточных неоднозначных интерпретаций образ явно отклоняется, частичного hash нет. Дополнительные dependency images не нужны. Byte/short/int/long версия-инвариантность проверяется отдельно.
- FieldRVA с native int/uint, unmanaged pointer или function pointer поддерживается при однозначной разрядности образа: PE32Plus — 8 байтов; PE32 с Requires32Bit либо без ILOnly — 4. Для ILOnly PE32 без Requires32Bit (в том числе AnyCPU/Prefer32Bit) содержащая native storage сборка явно отклоняется `BadImageFormatException`: выбор разрядности зависит от CLR процесса. Отказ применяется также при native field внутри вычисляемой layout; частичный четырёхбайтовый hash не возвращается. PE32 без native RVA storage продолжает поддерживаться.
- Metadata fixture изолирует метаданные и не исполняется; не все emitted signatures являются пригодным для выполнения C#-кодом. Основные пользовательские сценарии компилируются настоящим Roslyn deterministic=true.
- Известные ограничения: renumbering анонимных типов, принятая владельцем по D-01 перенумерация compiler-generated членов и недетерминированные компиляции могут дать лишние изменения. Generated names/cache fields не нормализуются.
- SourceStructure 123/123, Release build (0 errors, три прежних warnings) и main 2405/2405 подтверждены координатором на окончательном коде.
- Время расчёта измерено на пяти окончательных Release-сборках репозитория: медианы server 345.031 ms, tests 398.911 ms; полные размеры, число типов и samples — в [финальной validation](evidence/task-02-final-validation-sdk10-2026-11-18.md). Порога нет, результат на большие solution не переносится.

Замер использовал уже прочитанные `IlAssemblyImage`, отдельный прогрев и `Stopwatch` вокруг `IlAssemblyFingerprints.Compute`; файловое чтение исключено. Конкретный временный harness и команда — [evidence/task-02-timing-harness.md](evidence/task-02-timing-harness.md). AnalyzerLifecycle не запускался: lifecycle, workspace load/prepare/publication, host и их конфигурация не изменены. Linux, CAS/native execution и большие solution не проверялись.

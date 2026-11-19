# Task-02 — независимое ревью, раунд 4

Дата: 2026-11-18. Итоговый вердикт того же раунда после явного ответа владельца и проверки delta: **ACCEPT независимого ревью**. R1–R3 закрыты, D-01 разрешён владельцем и закреплён документацией/регрессией, новых технических MUST-FIX нет. Финальная приёмка задачи, Release build/main suite, timing и статусы остаются за координатором; этот вердикт не активирует инструмент.

Baseline — `f7e6d96`. Проверены фактические восемь production/model-файлов, включая untracked: `IlAssemblyFingerprints`, `IlFingerprintReader`, `IlFingerprintNames`, `IlFingerprintAttributes`, `IlFingerprintFieldData`, `IlFingerprintEncoding`, `IlFingerprintSnapshot`, `IlFingerprintChanges`; все семь `IlFingerprint*.cs` тестовых/helper-файлов. Прочитаны task-02, workflow MVP README, полный отчёт реализации, ревью раундов 1–3, AGENTS.md, полный `docs/code-style.md`, skill `epoch-subagent-loop` и Ponytail. Посторонние `.agents/skills/prepare-github-release/` и `impact-run/` исключены. Рецензент записал только этот отчёт; код, тесты, требования, статусы и индекс не менялись, build/test не запускались параллельно координатору.

## Закрытие D-01 — явное решение владельца и регрессия

Первоначальное обещание инвариантности при любой перестановке методов расходилось с сохранением точных имён скомпилированных членов и вложенных типов, а также порядка полей на подтверждённом Roslyn-сценарии:

```csharp
using System;
public class A
{
    public Func<int> M() => () => 1;
    public Func<int> N() => () => 2;
}
public class B {}
```

Перестановка только M/N меняет compiler-generated ordinals, имена и cache fields. Проверенный лог координатора `TestResults/task02-sdk10/lambda-order-and-marshal-probe.log` показывает `equal A fingerprints=False`, `equal B fingerprints=True`. Это лишнее изменение отпечатка, не свидетельство молчаливой потери теста. Прежний тест method reorder содержит обычные методы без лямбд и не использовался как доказательство для этого случая.

После первоначальной записи pending владелец явно ответил: **«Зафиксировать ограничение: перенумерация generated-членов может дать лишние изменения, как у анонимных типов (рекомендуется для MVP).»** Проверены фактические изменения task-02, [M-09 MVP](../README.md#решения-владельца-по-плану) и [D-09 канона](../../README.md): дата 2026-11-18, scope — консервативное ограничение отпечатков task-02; нормализация generated names/cache fields не требуется, точные имена и порядок полей сохраняются. Условие method-reorder invariance явно ограничено отсутствием generated renumbering. Activation permissions и task-03 не меняются. Это принятое решение владельца, а не предположение по рекомендованному варианту.

Проверен новый `IlFingerprintTests.Generated_member_renumbering_after_lambda_method_reorder_conservatively_changes_owner`: две deterministic компиляции одинакового первого исходника дают идентичные bytes; между переставленными версиями отдельно доказана смена имён generated lambda-методов M и N через MetadataReader. Сравнение снимков требует только A в Changed, пустых Added/Removed, неизменных B и assembly hash/flag. Production-код по D-01 не изменялся. Прочитаны полный новый тест, metadata-helper и обновлённая карта implementation report; замечаний к delta нет.

Фактический SDK10 Release прогон `TestResults/task02-sdk10/review-4-owner-green.trx` / `.log` содержит этот тест с outcome Passed: **518 executed, 518 pass, 0 fail, 0 skips**, состав непосредственно сверён — **289 fingerprint + 106 прежних IL + 123 SourceStructure**. **D-01 закрыт в этом же раунде 4.**

## Закрытие R3-F-01: SafeArray

`IlFingerprintReader.Marshal` теперь обрабатывает typed user subtype у native type `0x1D` в том же общем пути, который вызывают поле и Param, включая sequence=0. Variant subtype остаётся в побайтовом prefix, optional managed-type строка проходит через `SerializedType` с solution/external policy, остальные bytes сохраняются. Descriptor без user subtype остаётся полностью наблюдаемым. Не установлено очистки произвольных строк либо потери subtype.

Проверены отдельные Roslyn paired-compilation случаи `Safe_array_solution_version_preserves_consumer_fingerprints` для field/parameter/return: неизменный потребитель перекомпилируется против Library версий 1/2; тест доказывает различие сырых descriptors и равенство всех type/assembly hashes. Отдельные controls проверяют внешнюю версию при отсутствии Library AssemblyRef, другой record type, другой variant subtype и descriptor без user type. Identity/subtype controls требуют изменения только C, неизменных B и assembly hash. **R3-F-01 закрыто.**

## Закрытие R3-F-02: binary DeclSecurity

`IlFingerprintReader.Security` передаёт полный PermissionSet в `IlFingerprintAttributes.ReadSecurity`, сохраняя action и CustomAttribute владельца DeclSecurity. Общий путь используется type/method/assembly owners. Binary decoder проверяет compressed counts, длину argument block и полное потребление набора; typed outer SecurityAttribute и typed System.Type/enum values нормализуются с solution/external policy. Named argument сохраняет kind, тип, имя и значение; обычные string/number values не очищаются. Не-бинарный legacy payload сохраняется точными bytes.

Проверены девять solution-version случаев owner × typed-kind, девять внешних typed-version controls без Library AssemblyRef, двенадцать owner × identity/action/string/number controls и четыре точных отказа для неполного binary payload. Строковый control содержит version-looking текст и остаётся значимым. Security fixtures теперь содержат полный `'.' + count + qualified type + length + named arguments`; прежние неполные blobs не выдаются за полноценное доказательство значения.

Эти тесты используют MetadataBuilder и не заявляют парную C# CAS-компиляцию либо CAS runtime enforcement. Независимый старый probe координатора также был MetadataBuilder-свидетелем. Этого достаточно для проверяемого пути чтения конкретных метаданных; новых CAS-пакетов нет. **R3-F-02 закрыто.**

## Повторная проверка R1 и R2

- **R1-F-01 закрыто:** runtime-dependent native RVA storage в ILOnly PE32 без Requires32Bit явно отклоняется целиком; отказ распространяется на вложенную layout. Native int, pointer, function pointer и nested native field проверены на обеих версиях восьмибайтовых данных. Fixed-bitness PE32Plus, Requires32Bit и mixed-mode PE32 controls сохранены. Пригодный четырёхбайтовый hash для неопределённого восьмибайтового значения не возвращается.
- **R1-F-02 закрыто:** named argument кодирует объявленный тип; четыре int[]/long[] field/property × empty/null compilation-регрессии сохраняют различимость при одинаковом конструкторе и неизменных B/assembly hash.
- **R1-F-03 закрыто:** metadata-only BaseType и InterfaceImpl witnesses сохраняют методы, raw signatures и IL. В reader оба вклада присутствуют. История двух отдельных omission-прогонов и точного восстановления различена в отчёте; последующие исправления не удалили эти вклады.
- **R2-F-01 закрыто:** CustomMarshaler нормализует только managed-type строку; GUID/native-name prefix и cookie suffix остаются точными. Двенадцать controls по трём владельцам сохранены; проверенный повторный compilation-probe показывает равные C/assembly hashes при различающихся raw descriptors. Cookie с version-looking текстом не исключается.

## Полнота и границы доказательств

Сверены полные identities типов/членов с assembly owner и вложенностью, signature shape и канонизация IL/signature tokens, строковые литералы с точными UTF-16 code units, ownership вложенных типов и неизменяемые результаты сравнения changed/added/removed/assembly flag. Проверены сортировка предусмотренных наборов, сохранение порядка полей и EH, локальные сигнатуры, P/Invoke, MethodImpl/MethodSemantics, generics/constraints, constants, layout/RVA, attributes/security и assembly facts. Чтение идёт из переданного memory image; путь, PDB, MVID и собственные build versions не попадают в hash.

Карта «факт → тест» сверена с реальными fixtures, включая самостоятельные BaseType/InterfaceImpl witnesses. Все 45 таблиц ECMA-335 00–2C перечислены с назначением либо причиной исключения; пропущенной строки не установлено. Наличие карты и зелёных тестов само по себе не использовалось как доказательство корректности. Нового конкретного случая коллизии, загрязнения hash или явно отсутствующего обязательного атомарного теста не установлено; контрактный вопрос D-01 закрыт описанным выше решением.

Частичный снимок не публикуется: hashes возвращаются лишь после чтения всех владельцев и assembly facts; исключения обязательного чтения не подменяются пригодным результатом. Явный предел 4096 неоднозначных внешних enum-интерпретаций остаётся отказом целого образа, а не выбором первой интерпретации. Отказ на runtime-dependent native RVA также сохранён и описан в implementation report.

Проверены TRX и логи координатора под SDK **10.0.402**, Release:

- `TestResults/task02-sdk10/review-3-red.trx` / `.log`: **49 executed, 16 fail, 33 pass, 0 skips** до behavior fix; провалы относятся к трём SafeArray version cases, девяти permission typed-version cases и четырём malformed permission cases.
- `TestResults/task02-sdk10/review-3-green.trx` / `.log`: **517/517 pass, 0 skips**, непосредственно сверенный состав — **288 fingerprint + 106 прежних IL + 123 SourceStructure**.

Первоначальный red 141/140, дополнительные поздние случаи и omission-прогоны в implementation report различены; первоначальный red не считается индивидуальным red всего итогового набора. Новая D-01 regression фиксирует уже существовавшее разрешённое поведение и не заявляется как red-before-fix: production fix для принятой границы не нужен. Собственных конкурирующих прогонов у рецензента не было. Координатор сообщил о предыдущих Release build с 0 errors/тремя прежними warnings и main suite 2404/2404 без skips на коде до новой регрессии; это не финальная validation после delta. Координатор повторяет итоговые Release build/main suite и timing против окончательного состояния, затем согласует task/index/report statuses и решение о приёмке. Независимое ревью этого не заменяет.

Native execution и CAS enforcement не проверялись; задача читает метаданные и не активирует публичный инструмент. Допустимый по решению владельца остаточный риск — лишний выбор типов при compiler-generated renumbering; молчаливая потеря теста этим ограничением не заявляется.

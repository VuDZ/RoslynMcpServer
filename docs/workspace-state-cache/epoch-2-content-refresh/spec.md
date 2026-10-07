# E2 — Обновление существующих входов

Статус: **spec-v2 / revised-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Startup diff отличает existing content от graph/membership change. Content-only
разрешён только при проверяемом finite profile evidence, что изменяемые bytes
не определяют evaluated graph/options вне выбранного refresh mechanism.
Known Source role и имя SDK недостаточны. Unknown/non-admitted target/import/
condition → graph dirty и ordinary fallback. Анализ произвольных task bodies
не требуется; permanent exclusion всех пользовательских imports не вводится.
Imports inventory и restored roles не являются сами по себе content-independence proof.
Supported positive source/additional/config cases должны сохраняться.

## Правила dirty

- Existing `.cs` content с подтверждённой content-only eligibility: один physical
  read, все memberships и актуальная encoding; consumers получают новый snapshot.
- Existing AdditionalFiles/analyzer config content — existing non-CSharp mechanism
  при той же eligibility, отсутствии evaluation role и допустимом current execution.
  Новые/удалённые configs дают fallback. Unknown не становится UserInput после hydrate.
- Changed metadata refresh разрешён только при доказанном сохранении relevant
  resolution constraints и evaluated inventory выбранного профиля. Same path и
  даже same assembly identity сами по себе не predicate. Identity/resolution-
  sensitive или unknown change → ordinary fallback. Supported safe positive metadata
  refresh обязателен; all-fallback не закрывает заявленную часть E2.
- Analyzer/generator mutation проходит current reprepare/fallback/restart-required;
  sample hash не заменяет execution validation. Нет fabricated session provenance.
- Graph/membership/missing/unreadable/unknown-role inputs → whole-request ordinary
  load. Dirty set включает всех owners path/region, в том числе shared imports/links.

Одна map/session сохраняет evaluation precedence и confirmed producer bindings.
Нет второго live index. New ancestor config discovery задаётся actual profile
для project/linked sources; repo root не является универсальной границей.
Standing live coverage gaps не превращаются в новый blanket read ban.

## Capture после refresh

Восстановленный candidate проверяется перед publication и может заменить disk
envelope в рамках успешного load. Частая запись на каждый watcher event и
background checkpoint scheduler в scope не входят. Неуспех записи нового
envelope оставляет успешный load и прежний законченный envelope на диске.

Новый capture сохраняет evidence связи graph с фактически consumed generation
inputs по E1 contract. Одни post-load hashes не устраняют прежний evaluation gap;
при отсутствии binding usable replacement envelope не записывается.

## Приёмка

Source edit offline → disk graph reuse без Open*/DTB; shared source получает
одинаковый text во всех memberships. Additional/config и metadata edits имеют
положительные role-specific проверки. Create/delete/rename source, новый
Directory.Build.props, restore/import change и ambiguous role дают fallback.
Mutation сравнивается со свежей ordinary загрузкой по независимым semantic assertions.
Бенчмарки single/batch content edit → restart и build → restart различают
фактический hit/miss и реальные изменившиеся inputs.

## Profile и acceptance controls

До принятия content reuse profile заданы traceable eligibility evidence и его
границы: supported positive existing source mutation без DTB и negative target,
меняющий DefineConstants/AllowUnsafe из тех же source bytes. Unknown target body
не анализируется classifier-ом и не считается безопасным по default.

Metadata subset проверяется positive safe content update и negative exact-version
DLL replacement против fresh ordinary resolution/semantic result. Path/identity
equality не заменяют этот contract; normal MSBuild resolution не называется
no-DTB metadata reuse. Accepted U-001 reference semantics сохраняются, пока
U-001 не выбран, поддерживаемая fidelity не угадывается.

Create/delete ancestor `.editorconfig`/`.globalconfig` и explicit configs сравниваются
с ordinary discovery; known config content change тестируется отдельно.
Ни P-013, ни P-014 не меняют mvp-fast threshold/chunks или binary blind spot.
E1 whole-request fallback остаётся самостоятельным результатом; E2 predicates
не объявлены его prerequisites.

## XML boundary metadata refresh

[H-001](../human-decisions.md) сохраняется при metadata refresh: external documentation
text/provider equality не обязательны; source comments и остальные metadata/resolution
predicates обязательны. XML с evaluation/additional ролью не исключается.
Strong-name часть U-001 остаётся открытой; H-001 её не закрывает.

# Independent Review

Reviewer: Grok 4.7. Target: `docs/workspace-state-cache` at `67564de`. Каталог: `docs/workspace-state-cache/review-grok`.

## Verdict

Маршрут E0→E3 в целом собран так, чтобы ранний disk MVP жил без selective reopen, а отказ E3 не отменял E1/E2. В текущем виде E0 нельзя честно реализовать до конца: положительный ordinary capture реального проекта конфликтует с публичными пределами Roslyn 5.9.0. E1/E2 дополнительно не фиксируют, откуда берутся imports и какие ancestor/config изменения запрещают hit.

## Summary

Пакет правильно откладывает v2, не обещает полный MSBuild graph и явно описывает `mvp-fast` blind spot, fallback, watcher race, write boundary и no-go spike E3. Эти части выглядят согласованными с текущими production gates.

Блокер в E0: stock metadata reference не отдаёт `DocumentationProvider`, а `DesktopStrongNameProvider` не отдаёт search paths. Spec запрещает silent defaults и одновременно требует успешный capture проекта с реальными references.

Дальше три контрактных разрыва invalidation. Manifest требует evaluated imports и restore paths, но разрешённые `WorkspaceInputMapBuilder` и binlog с `ProjectImports=None` их не содержат. Новый ancestor `.editorconfig` / `.globalconfig` не входит в названный probe, хотя task E2 требует fallback на новый ancestor file. Content-only не определяет, как узнать target, который меняет options по bytes уже известного source.

Отдельно список полей disk key не доведён до аргументов `load_workspace`.

## Findings

| ID | Severity | Category | Title |
|----|----------|----------|-------|
| F-001 | blocker | correctness | Ordinary capture реального проекта не проходит публичные metadata/options |
| F-002 | major | correctness | Evaluated imports и restore inputs не из чего снять |
| F-003 | major | correctness | Новый ancestor analyzer config не входит в правило invalidation |
| F-004 | major | requirements | Content-only не определяет target, который зависит от bytes уже известного source |
| F-005 | minor | requirements | Состав disk request key не перечислен |

## Highest risks

Если реализовать пакет как написан, положительный E0 упрётся в отказ capture либо в подмену нечитаемых options. Если обойти это молча и включить disk hit, правка невидимого import, нового корневого `.editorconfig` или target, читающего уже известный source, переживёт restart и разойдётся с ordinary load. Сегодня новый PID эти изменения видит, потому что MSBuild выполняется заново.

## Questions / uncertainties

- Какой semantic subset считается положительным E0, если documentation provider и strong-name search paths ненаблюдаемы?
- Откуда manifest берёт evaluated import closure и фактический assets path, не меняя текущий analyzer binlog?
- Входит ли любой пользовательский `.targets` в content-only или такой проект всегда graph-dirty?
- `shadowCopyInSolutionAnalyzers` уже имеется в виду под «overlay mode» в ключе, или его нужно назвать отдельно?

## Suggested spikes

Новый эксперимент representability не нужен: отказ public API уже зафиксирован на Roslyn 5.9.0. Имеет смысл только короткая проверка, если автор считает, что import paths уже есть в properties текущего binlog с `ProjectImports=None`. Анализ тел MSBuild targets как spike не следует открывать: пакет сам не делает его prerequisite.

## Overall assessment

- Выглядит обоснованным: поэтапный scope, opt-in default, целый envelope, отказ от второго live index, сохранение write/admission gates, честный `mvp-fast`, E3 за spike с сохранением E1/E2 при no-go, benchmark не подменяет correctness.
- Требует изменения: контракт положительного capture (F-001), источник import/restore evidence (F-002), probe нового ancestor analyzer config (F-003), операционное правило content-only (F-004), состав request key (F-005).
- Требует проверки: только утверждение, что текущий binlog уже содержит imports, если автор его делает.
- Требует human decision: смысл положительного E0 при нечитаемых options; менять ли существующий provenance logger ради import closure; считать ли custom targets всегда вне content-only.

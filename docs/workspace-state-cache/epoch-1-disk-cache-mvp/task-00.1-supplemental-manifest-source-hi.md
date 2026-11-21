# E1/task-00.1 — Дополнительный collector через публичный MSBuild API

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — отдельная evaluation может описывать другие inputs или instances и разрешить ложный cache hit.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `проектирование_проверок`.
- Статус: **accepted, round 2, 2026-10-09 — bounded test-only public collector и retention/ABA controls; source partial / reusable capture insufficient; task-00 остаётся blocked; Release build/focused/main прошли**. [Review/validation](evidence/task-00.1-validation.md).
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md), [E1/task-11 — S-002](task-11-raw-import-restore-evidence-hi.md)
- Модель, реализовавшая задачу: Codex, текущая сессия; точный runtime model ID/reasoning не подтверждены инструментами.
- Модели, проводившие ревью: отдельная Codex reviewer subagent сессия, унаследованная модель; точный runtime model ID/reasoning не подтверждены инструментами.
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 2 (T001-R1-001/002), открытых must-fix: 0.
- Отчёт о реализации, ревью и validation: [source/generation evidence](evidence/task-00.1-supplemental-source.md), [review/validation](evidence/task-00.1-validation.md).

## Цель

Проверить один bounded isolated capture scenario: может ли дополнительный collector
через публичные MSBuild/NuGet API дать пригодный manifest выбранного реального request
и доказать его связь с graph ordinary load без изменения Roslyn BuildHost.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md), [task-00](task-00-input-manifest-hi.md),
[accepted S-002 / validation](evidence/task-11-recheck-validation.md) и outputs
Depends on обязательны. Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Исходный контроль — неизменённый SamplePlugin из E0/task-07. S-002 уже установил
missing root import evidence, несовпадение dependent evaluation/build contexts,
неполноту restore config/lock coverage и отдельный unproven P-004. Повторять
проверку current None channel без нового проверяемого предположения не требуется.

Один collector/prototype и связанные controls в isolated harness. Дополнительная
evaluation рассматривается как кандидат источника, а не как доказательство
ordinary consumption. Сопоставить effective globals, environment, SDK resolution,
Configuration/Platform/TFM, все consumers и actual options/memberships/references.
Применимость NuGet config/lock inputs подтверждать отдельно от наличия assets.

Для P-004 проверить минимальный механизм до consumption. Discovery → удержание
existing inputs → новый ordinary load допустим как гипотеза, а не назначенный
механизм. Newly discovered inputs, known absence, membership changes и generated
obj writes должны иметь явную границу подтверждения либо capture refusal.

Вариант с изменением/собственным fork Roslyn BuildHost исключён владельцем
2026-10-09. Private reflection, универсальная модель arbitrary MSBuild tasks,
production integration/store/watch/write и изменение current provenance gates
в scope не входят. Source/refs/csproj реального контроля не менять ради pass.
Raw secrets не сохранять. Unknown/incomplete → whole-request ordinary fallback.

## Конкретный результат

- `evidence/task-00.1-supplemental-source.md`: source/category/path/all-instance
  mapping, completeness boundary, generation contract и sufficient/partial/insufficient verdict.
- При исполнимой гипотезе — bounded test-only prototype и raw evidence для одного
  unchanged real positive, negative controls, capture overhead и ограничений.
- Рекомендация: продолжить task-00 с этим producer либо сохранить blocked с точным
  оставшимся препятствием. Source-only success при unproven P-004 остаётся partial.

## Проверки

- Root и referenced final instances имеют собственный traceable input evidence;
  одинаковый physical path не подменяет instance/evaluation binding.
- Nested/shared/conditional/external imports, redirected assets, применимые
  config/lock inputs и их all-owner coverage проверены по independent expected inventory.
- Ordinary и candidate graph сравниваются по заранее выбранным inventories,
  options/references и semantic assertions; повторная evaluation не считается oracle.
- Props mutation после consumption до первого post-load probe, включая ABA,
  не создаёт usable capture. Stable pre/post hashes сами по себе не pass.
- Новые inputs/ancestor configs, исчезновение входа, I/O/budget failure и
  неподдержанный custom input дают refusal/fallback; explicit obj/external inputs не теряются.
- Неизменённый real positive обязателен для sufficient verdict. Если его нет,
  исследование фиксирует negative/partial outcome, task-00 остаётся blocked.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Docs-only вывод требует link/dependency/diff checks; code/test prototype — полную
применимую validation. После ревью/validation обновить дату, принятый bounded scope,
evidence и фактические модели в шапке и README эпохи. Принятие исследования
не означает принятия task-00 adapter, reusable envelope или public activation.

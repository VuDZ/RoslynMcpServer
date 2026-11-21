# E1/task-00.3 — Пересмотр границы freshness и restore inputs

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — необоснованное исключение input ослабит semantic/write correctness под видом сужения MVP.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_требований`, `анализ_зависимостей`.
- Статус: **accepted, round 1, 2026-10-09 — docs-only отрицательный contract audit; verdict not-viable на текущем evidence; draft не применён, task-00 остаётся blocked**. [Review/validation](evidence/task-00.3-validation.md).
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md), [E1/task-11 — S-002](task-11-raw-import-restore-evidence-hi.md)
- Модель, реализовавшая задачу: Codex implementer subagent, унаследованная модель сессии; точный runtime model ID/reasoning не подтверждены инструментами.
- Модели, проводившие ревью: Codex independent reviewer subagent, новая сессия; точный runtime model ID/reasoning не подтверждены инструментами.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 0; открытых must-fix findings: 0.
- Отчёт о реализации, ревью и validation: [contract audit](evidence/task-00.3-contract-scope.md), [review round 1](evidence/task-00.3-review-round-1.md), [final docs validation](evidence/task-00.3-validation.md).

## Цель

Подготовить одну обоснованную поправку границы manifest: определить, может ли MVP
воспроизводить fresh ordinary load на уже подготовленных restore artifacts,
проверяя только действительно значимые для этой границы inputs.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md), [README серии](../README.md),
[правила исполнения](../execution.md), [task-00](task-00-input-manifest-hi.md),
[accepted S-002 / validation](evidence/task-11-recheck-validation.md),
[P-004/P-011 и change ledger](../change-ledger.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Contract audit и короткое предложение владельцу. Разделить inputs, фактически
влияющие на ordinary design-time load, inputs предшествующего restore и conservatively
tracked candidates. Наличие assets не доказывает ненужность NuGet configs/locks;
отсутствие raw события не доказывает отсутствие consumption или semantic influence.
SDK tasks/imports и существующие generated props/targets входят в анализ.

При необходимости допустимы focused isolated controls на owned real copy с
независимыми expected outcomes. Production код/поведение, canonical requirements,
task dependencies, activation и статус blocked task-00 автоматически не менять.
Поправку представить в evidence как current → proposed clauses и последствия;
применение канона требует выбора владельцем и применимого ревью.

Изменение/собственный fork Roslyn BuildHost исключён владельцем 2026-10-09.
H-001/H-002, all-instance binding, environment/config discovery и P-004 сохраняются,
если proposal не предъявляет отдельное явное изменение контракта с риском и причиной.
Игнорирование P-004 или разрешение ложного hit не считается решением этой задачи.
Общая модель arbitrary MSBuild tasks и переписывание всех эпох вне scope.

## Конкретный результат

- `evidence/task-00.3-contract-scope.md`: текущая и предлагаемая гарантия freshness,
  traceable обоснование inclusion/exclusion каждой рассматриваемой restore category.
- Короткий точечный draft изменения требований, если сужение оправданно: что
  пользователь получает, какие изменения дают miss/fallback и какие guarantees исключаются.
- Verdict viable/not-viable, остаточные source/generation blockers и перечень
  задач/controls, затронутых предложением. Принятые E0/S-002 результаты сохраняются.

## Проверки

- Изменение NuGet config или lock при прежних assets сравнивается с fresh ordinary
  load; отдельно рассматривается изменение после нового restore. Missing evidence
  не записывается как proof ненужности input.
- Project/package/import/SDK/environment changes, redirected intermediates и
  generated restore props/targets имеют явный результат в proposed contract.
- Условия появления/удаления configs и linked/external boundaries сохранены либо
  конкретно показаны как отдельное предлагаемое ограничение с последствиями.
- False-hit сценарии проверены по options/references/memberships и выбранной
  семантике; source-only hashes и успешный symbol query не доказывают весь scope.
- Proposal явно показывает, какие S-002 gaps он закрывает, а какие остаются.
  Сужение restore contract не объявляется решением missing imports или P-004.
- Existing fallback, safe write boundary и публичные обещания не ослаблены молча.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Docs-only audit требует consistency/link/dependency/diff checks; code/test controls
требуют полной применимой validation. После ревью/validation обновить дату,
принятый proposal scope, evidence и фактические модели в шапке и README эпохи.
Принятие исследования не применяет draft и не снимает blocked task-00.

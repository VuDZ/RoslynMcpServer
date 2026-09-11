# Неразрешённые вопросы v2

Все пять вопросов перенесены из авторитетного [arbitration/unresolved.md](../arbitration/unresolved.md).
Их статус — **UNRESOLVED**. Ни наличие v2, ни перечисленные условные варианты
не означают выбора реализации. E5-01 имеет verdict UNRESOLVED; остальные вопросы
являются открытыми gates внутри принятых с ограничениями решений.

## U-ARB-01 — Происхождение ссылок при отсутствующем пути

Связанные findings: E5-01, E5-02, E5-04, E5-05.

Нельзя одновременно требовать доказанную analyzer-to-project связь, пропускать
каждое недоказанное соответствие и гарантировать исправление исходного missing-path
repro, пока не установлен пригодный канал provenance.

Недостающие evidence:

- Фактически доступные evaluated Roslyn 5.9.0/MSBuild metadata для
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
- Надёжность связи analyzer item с загруженным проектом по поддержанной
  Configuration/TFM матрице.
- Стоимость и lifecycle дополнительной evaluation, если она нужна.
- Exact execution oracle для missing foreign analyzer с тем же filename,
  что и output in-solution проекта.

После evidence: если надёжная связь существует, использовать её и пропускать
недоказанные missing/inaccessible пути. Если её нет, владелец продуктового
требования должен выбрать между явно документированной opt-in unique-name эвристикой
для missing paths и сужением поддержки с отказом от исправления этой части исходного
repro. Inaccessible требует отдельного решения и не наследует missing-file правило.

До решения сохраняется выпущенный matcher, rollout эпохи 5 запрещён. Точки v2:
[E5-S1–S4](epoch-5-reference-provenance.md), E1-S2 и LC-S1.

## U-ARB-02 — Поддерживаемый режим обновления генератора

Связанные findings: E1-02, E1-03, E3-01, E3-05, E6-01.

Требования разрешают документированное ограничение restart и не требуют
in-process hot reload. Фактическое V1→V2 поведение текущего loader на .NET 10
ещё не измерено точным execution oracle.

Недостающие evidence:

- Exact markers для cached load, reset+load и process restart при неизменной
  assembly identity и изменённых bytes.
- Реальный loaded assembly path/identity и факт возврата stale assembly.
- Операционная стоимость и ожидаемая частота необходимого server restart.

После evidence выбрать точно ограниченный in-process update либо restart-required
контракт. В обоих вариантах unsupported in-process refresh не должен молча выдавать
known-stale или wrong semantics. Здесь вариант не выбран, даже если restart
может быть окончательным поддержанным результатом. Точки v2:
[E3-S1/S5](epoch-3-loader-contract-and-dependencies.md), E1-S2, LC-S1–S3.

## U-ARB-03 — Production discovery зависимостей и scope binding

Связанные findings: E2-01, E3-02, E3-03, E3-04.

Fixture с явным набором файлов может доказать preparation/binding mechanics,
но production источник полного набора private runtime dependencies не установлен.
Requester-scoped resolution конфликтующих helpers также не доказан.

Недостающие evidence:

- Стабильный источник зависимостей: evaluated build metadata, generated manifest
  либо иной проверенный механизм.
- Фактические вызовы `AddDependencyLocation` и requesting-assembly поведение
  закреплённой версии Roslyn.
- Type-identity результаты ALC prototype с перечисленными shared host contracts.
- Исполнение конфликтующих helpers и измерения unload/lifetime.

После evidence выбрать scoped dependency support и только при необходимости
ALC/resolver design. При недостаточности evidence явно ограничить поддержку
main-only генераторами и отклонять dependency scenarios с понятной диагностикой.
Эта ветка здесь не объявляется автоматически выбранной. Main-only файловая политика
эпохи 2 уже определена и сама не решает loader support. Точки v2:
[E2-S2](epoch-2-immutable-shadow-copies.md), [E3-S2–S5](epoch-3-loader-contract-and-dependencies.md).

## U-ARB-04 — Возможная загрузка analyzer из raw workspace

Связанные findings: E1-07, E4-03.

Write boundary предотвращает сохранение известных overlay references, но ни одна
сторона не показала конкретную операцию, которая загружает существующий analyzer
напрямую из `workspace.CurrentSolution` до overlay или вне него.

Недостающие evidence:

- Existing-correct-path tests с семантикой, всеми write paths и фактическим
  loaded assembly path.
- Forced rebuild после каждой операции, доказывающий либо опровергающий lock output.
- Временная последовательность загрузки до включения overlay и raw-workspace readers.

После evidence: если утечка воспроизведена, специфицировать отдельную semantic/load
boundary. Если не воспроизведена на поддержанной матрице, документировать измеренную
область без утверждения, что write workflow вызвал anti-lock гарантию. Эта v2
не выбирает новую load boundary и не сужает цель до missing paths. Точки v2:
[E1-S3/S4](epoch-1-lifecycle-verification.md), [E4-S3/S4](epoch-4-workspace-write-boundary.md).

## U-ARB-05 — Семантика флага при повторных вызовах

Связанные findings: E1-04, E6-03.

Публичный параметр — optional non-nullable bool: omission неотличим от явного
`false` на границе метода. На same-key cache hit действует session-sticky
поведение; reset или другая загрузка его очищает. Opt-in означает отсутствие
автоматического включения, но не определяет desired state каждого вызова.

Недостающие требования/evidence:

- Полагаются ли существующие клиенты на сохранение overlay при false/omitted.
- Включение относится к каждому load request или к workspace session.
- Политика совместимости при изменении sticky поведения.

Требуется выбрать и описать desired-state (`false`/omitted отключают) либо
session-sticky (отключение через reset). Tri-state или новый параметр — отдельное
публичное API-изменение с отдельным обоснованием. До решения сохранить и точно
документировать текущее поведение; старый mapping не отбрасывается неявно.
Точки v2: README, E1-S2, E2-S1, LC-S1–S3, E6-S3.

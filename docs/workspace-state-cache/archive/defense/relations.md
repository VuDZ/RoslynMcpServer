# Finding Relations

Дата: **2026-10-07**. Связи ниже информационные. Исходные findings не объединены,
не перенумерованы и не объявлены окончательными arbitration clusters.
Полные самостоятельные ответы — в [ledger](finding-ledger.md).

## Possible overlap: representability и ранний positive gate

- review-grok/F-001
- review-ds/F-003

Оба используют known provider getters limitations, но Grok ставит вопрос о
самом semantic fidelity contract, а DS — о последовательности capability
проверки и contingency. Один owner fidelity choice не заменяет проверку
выбранного реального контроля. Не объединять автоматически.

review-ds/F-006 дополняет их вопросом, тот ли executable artifact проверяется
в E0 и поставляется в E1. Это отдельный ownership/evidence concern.

## Possible overlap: request identity

- review-ds/F-001
- review-grok/F-005

DS рассматривает omitted/default lookup, Grok — closed parameter matrix и
overlay mode. Рекомендуемые corrections не совпадают: raw requested-only
против effective properties плюс overlay bit. Нужны разграничение normalized
host globals/evaluated values и отдельное решение base identity versus mode
admission. Defense предлагает это разграничение, не объявляет финальный key.

review-ds/F-005 добавляет config-driven entry и источник opt-in. Он не является
дубликатом cold lookup finding. review-astra/F-002 — отдельный ambient environment
input, который нельзя закрыть только равенством requested arguments.

## Complementary concerns: import source и original capture interval

- review-grok/F-002
- review-astra/F-001

Первый касается того, откуда получены dependency paths; второй — какие bytes
фактически породили graph. Даже правильный полный список paths с post-load hashes
не закрывает generation race. Нельзя объявлять capture безопасным после решения
только одного из этих вопросов.

## Complementary concerns: metadata bytes и content-only eligibility

- review-astra/F-003
- review-grok/F-004

Общая тема — observable diff не доказывает неизменность evaluated consequences.
У Astra это DLL resolution/SpecificVersion, у Grok — source bytes-dependent
options. Это разные support predicates и разные negative controls.

review-grok/F-002 нужен для target/import inventory, но наличие inventory само
по себе не доказывает content independence. review-ds/F-008 задаёт сохранение
document roles, но correct role также не доказывает отсутствие build dependency
на bytes.

## Complementary concerns: snapshot health и roles

- review-ds/F-004
- review-ds/F-008

LoadGraphComplete/coverage и occurrence role/provenance — разные свойства.
Сохранение diagnostics не восстанавливает IsGenerated, а role table не переносит
ordinary load verdict. Возможен единый comparative test с раздельными assertions;
это не основание схлопнуть findings.

## Complementary concerns: config discovery

- review-grok/F-003
- review-ds/F-008

Первый касается нового отсутствовавшего ancestor config; второй — интерпретации
известного документа после hydrate. Round-trip known roles не находит новые файлы.

## Complementary concerns: overlay support и storage identity

- review-ds/F-002
- review-grok/F-005

Отдельный overlay key не создаёт fresh provenance. Общий clean base key не
разрешает автоматически reuse mode admission. Original capture fidelity
(review-grok/F-001) и overlay new-session provenance — независимые препятствия.

## Различия reviewers, требующие рассмотрения

- review-grok/F-001 предлагает contract decision без нового getter spike;
  review-ds/F-003 предлагает ранний mandatory ordinary capture spike. Defense
  различает уже известный API limitation и ещё не проверенный concrete control:
  первый не исправляется повторным прогоном, второй нужно проверять после
  принятого fidelity contract. Это не решение за арбитра о стадии реализации.
- review-ds/F-001 советует requested-only matching, review-grok/F-005 — effective
  matching с mode flag. По текущему LoadAndPrepareAsync существуют независимо
  разрешаемые config/inherited globals и неизвестные до evaluation defaults.
  Defense не принимает ни одного blanket shortcut; финальный contract не выбран.
- review-ds/F-002 допускает постоянное отсутствие overlay hit в E1 как correction;
  исходный task-05 предусматривает adapter либо fallback. Требование к режиму
  остаётся owner choice, current missing adapter не доказывает невозможность
  любого будущего disk-only admission.

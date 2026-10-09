# E1/task-11 — продолжение после ревью прежних изменений

Дата: **2026-10-09**, Europe/Moscow. Исполнитель продолжения: отдельный Codex
implementer subagent; точный runtime model ID/reasoning недоступны.
Коммитов нет. Это отчёт об исправлении; независимый verdict и final validation
принадлежат координатору и рецензенту.

## Сохраняемый результат

Сохраняются три isolated test-only `RawImportEvidence*.cs`, прежние raw measurements
и [source report](task-11-raw-evidence.md): `ProjectImports=None` даёт partial
path evidence, но root/all-instance import и required restore config/lock closure
не доказаны. Finite supported real manifest отсутствует; readiness E1/task-00 и
reusable envelope остаётся blocked. P-004 consumed-generation binding этим
экспериментом не доказан. Production/default/provenance/activation не меняются.

Прежние [review round 1](task-11-review-round-1.md) и
[validation](task-11-validation.md) описывают прежние identities и результаты.
Они не являются validation нового исправления. Их measurements не переписывались.

## T11-R2-001 — CLOSED после независимого round 3

Исходный constructor `RawImportEvidenceFixture` без проверки перечислял файлы
как `obj/Debug/net10.0`, так и `obj/Release/net10.0`. Clean CI выполняет restore,
затем Release tests; Debug directory может отсутствовать. Experiment падал бы
с `DirectoryNotFoundException` до получения design-time evidence.

Добавлен `Directory.Exists` guard только для configuration-specific generated
directories: копируются реально существующие generated inputs. Обязательный
top-level `obj` сохраняет prerequisite preparation restore; assets/restore
inventory и source bytes не заменяются новыми fixtures ради positive verdict.

Добавлен theory `Owned_real_copy_accepts_a_single_configuration_build_tree`
с двумя sparse owned source trees: Debug-only и Release-only. Проверяется создание
контрольной копии при отсутствующей другой configuration, exact существующие
generated source bytes и top-level assets bytes. Case Release-only воспроизводит
clean CI boundary; case Debug-only проверяет симметричную configuration boundary.
Корпус этого regression не открывается через MSBuildWorkspace и не выдаётся за
дополнительный real manifest positive.

## Проверки нового исправления

До C# изменения полностью прочитан `docs/code-style.md`; applicable `.editorconfig`
в repo/ancestor directories не найден. Изменены только fixture constructor и
новый regression в `RawImportEvidenceTests`; capture не изменён.
`git diff --check` завершился с exit code 0 (warning о CRLF касается прежнего
постороннего dirty `DependencyEvidenceRunner.cs`).

Исполнитель build/test не запускал. Координатор после final C# изменений выполнил
Release solution build (**passed**), focused `RawImportEvidenceTests` (**3/3**:
два regression cases и actual raw experiment), полный main
`Category!=AnalyzerLifecycle` (**1362/1362**, 0 failed/skipped). SourceStructure входит в main;
production C# и exact structural limit inventory не менялись.
AnalyzerLifecycle not-run / not required для isolated test-only изменений,
не затрагивающих lifecycle host/configuration или production paths.
Независимый [round 3](task-11-review-round-3.md) принял исправление;
[final validation](task-11-recheck-validation.md) фиксирует accepted bounded scope.

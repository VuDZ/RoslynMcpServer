# BuildHost/task-02 — этап 1: часть A в форке (ссылки раньше проекта)

Дата: **2026-10-25**. Статус: **in progress** — реализация в форке, Linux: `RoslynMcpServer.sln` identical,
сценарии сбоев task-00 пройдены; на расширенной фикстуре известно одно расхождение диагностик (ниже). Ждут прогоны
владельца на Roslyn.slnx и в офисе. Контракт: [task-02](../task-02-buildhost-optimization-spike-hi.md);
обоснование и проба — [шаг 0](task-02-step-0.md).

## Что сделано

Форк ([README](../../../../forks/roslyn-buildhost/README.md)): `ProjectBuildManager_ReferencesFirst.cs`, включается
`ROSLYN_BUILDHOST_REFERENCES_FIRST=1`.

- Запрос `GetProjectFileInfosAsync` проекта P сначала обходит ProjectReference из evaluation P (только существующие
  `.csproj`/`.vbproj`), в глубину: каждую ещё не собранную ссылку загружает через `ProjectCollection` (поздняя
  загрузка клиентом получит тот же `Project`), собирает её ссылки, затем её саму, и хранит экземпляры сборки, её
  DiagnosticLog или исключение. Затем собирается P; вызовы `GetTargetFrameworks`/`GetTargetPath` из его сборки
  попадают в существующие конфигурации.
- Первый запрос клиента к собранной наперёд ссылке получает её результат: записи лога добавляются в лог её
  `ProjectFile`, исключение бросается заново. Извлечение `ProjectFileInfo` — при запросе клиента, с его языком;
  `ProjectInstanceReader` берёт у `Project` только путь.
- Клиент 5.9.0 сам загружает каждую ссылку C#/VB из результата сборки, в том числе `ReferenceOutputAssembly=false`
  (`MSBuildProjectLoader.Worker.ResolveReferencesAsync`), поэтому собранное наперёд он запрашивает.
- Ссылка, которая не оценилась, оставляется загрузке клиента — та сообщит ту же ошибку. Отмена во время сборки
  наперёд возвращает проекту его `TargetFramework` и снимает отметку «собран»; другая ошибка сохраняется как
  результат и оставляет проект в том состоянии, в котором его оставил бы запрос клиента.
- Запросы в этом режиме обслуживаются по одному; клиент 5.9.0 и так шлёт их по одному.

Стенд: ключ `-ReferencesFirst` у `Invoke-BuildHostForkBaseline.ps1` и `Invoke-BuildHostForkScenarios.ps1` ставит
переменную только на стороне форка; оригинал остаётся эталоном ответа. В фикстуру сценариев добавлен первый проект
решения `Consumer` со ссылками на `MultiTfm` и на `Noisy` (повторный импорт — предупреждение evaluation, цель с
`<Warning>` перед `CoreCompile`): клиент собирает `Consumer` раньше, чем загружает их, а форк с A собирает их
наперёд.

## Linux, 2 vCPU, 2026-10-25

| проверка | результат |
|---|---|
| `RoslynMcpServer.sln`, baseline с трассой, прогрев и 3 раунда | **identical**; 2 сборки наперёд за загрузку, обе отданы клиенту; каждый проект оценён и собран один раз; загрузка ×0,891 к оригиналу (с трассой, 5 проектов — о выигрыше не говорит) |
| сценарии task-00 на прежней фикстуре | **все пройдены** (cancel, kill, early cancel, отсутствующий dll) |
| сценарии на расширенной фикстуре | load, cancel, kill, early cancel пройдены; **фикстура не identical**: у форка нет одной диагностики |
| сборка решения и main-набор CI (Release, `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`) | 1 390 passed, 0 failed, 0 skipped |

### Известное расхождение: предупреждения evaluation ссылок

Оригинал на расширенной фикстуре выдаёт:

```text
Failure: Msbuild failed when processing the file '' with message: "<fixture>/Noisy/Noisy.props" cannot be imported again. It was already imported at "<fixture>/Noisy/Noisy.csproj (1,106)". This is most likely a build authoring error. This subsequent import will be ignored.
```

Это предупреждение evaluation `Noisy`. Upstream показывает его только через повторную evaluation `Noisy` внутри
сборки `Consumer`: логгер сборки ловит предупреждения этой evaluation. Корневая evaluation host идёт в
`ProjectCollection` без логгеров, поэтому у самого `Noisy` оно не показывается нигде. A убирает повторную
evaluation, и диагностика исчезает. Предупреждение сборки `Noisy` (`Noisy build warning`) отдаётся одинаково.
Проба шага 0 этого не видела: она сравнивала результаты сборок, а не логи.

Решение владельца (2026-10-25): сначала замер на Roslyn.slnx и в офисе — время A в форке и есть ли такое
расхождение там; затем точное воспроизведение upstream (вариант 1): host ловит предупреждения своих корневых
evaluation и переоценок по TFM, по событиям сборки видит, к каким конфигурациям ссылок она обращается, и при
отдаче результата вставляет предупреждения тем проектам, у которых upstream оценил бы ссылку внутри сборки, в те
же места лога. Comparator не ослабляется.

## Прогоны владельца

```powershell
pwsh spikes\BuildHostFork\Invoke-BuildHostForkBaseline.ps1 -Solution <путь>\Roslyn.slnx -Name CSharpCompilationOptions -ReferencesFirst
pwsh spikes\BuildHostFork\Invoke-BuildHostForkBaseline.ps1 -Solution <путь>\<офисное решение>.sln -ReferencesFirst
```

Для разбора достаточно `summary.md` и `environment.json` каждого прогона (для офиса — без имени решения). Без
`-Trace`: время A сравнивается с оригиналом, который по task-00 равен форку без изменений (×0,995–1,011).

## Pending

- Прогоны владельца на Roslyn.slnx и в офисе.
- Вариант 1 для предупреждений evaluation ссылок; затем повтор Linux-проверок и прогонов.
- Память: пик рабочего набора host в сводке baseline (порог task-02 — +10 %).

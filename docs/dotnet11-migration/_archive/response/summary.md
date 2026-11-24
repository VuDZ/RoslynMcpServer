# Summary: ответы автора на review dotnet11-migration

Дата: **2026-11-18**. Статус: **completed** — подготовлены ответы на все четыре ID
из одного каталога review; [карточки](README.md), [validation](validation.md).
Требования плана и исходное review не менялись. Это результат защиты, не арбитраж,
не новая версия спецификации и не приёмка реализации.

## Accepted

- **R-01 (Blocker)** — корневой global.json существует; предписание оставить его
  при переводе собственного TFM на net11 неверно. Минимальный предложенный путь —
  точный стабильный SDK 11 для репозитория и согласованный CI/release pin.
  Полное закрытие AR-18 этим не объявляется.
- **R-03 (Medium)** — к net11 языковому контролю нужен отдельный older-TFM
  compilation/binding случай невиртуального static interface member с LangVersion 15.0.

## Partially accepted

| ID | Принято | Не принято |
| --- | --- | --- |
| R-02 (Blocker) | Разделить SDK 10 по bands и MSBuild, проверить настоящий runtime/reference profile, явно допустить пересмотр support minimum либо no-go; для references 18.7.1 консервативный кандидат SDK 10 — 4xx | Объявлять конкретный runtime-сбой старых SDK доказанным только NuGet dependency; автоматически приравнивать references клиента к PackageDownload форка; молча переписывать исторические fixtures |
| G-01 (High) | Отдельный MSBuild Server gate; документированный child-only opt-out, если текущий node-reuse запрет не доказан достаточным | Считать утечку и блокировку уже воспроизведёнными; обязательное дублирование достаточного текущего запрета; глобальный shutdown чужих build servers в production |

Для R-02 рекомендация Locator о нижнем поддерживаемом MSBuild учитывается:
старые полосы нельзя объявить поддержанными по одному удачному smoke вопреки
documented support requirements. Неопределённость runtime-механизма не оправдывает
неограниченную строку «SDK 10».

## Rejected

Нет полных REJECT. Отдельные непринятые выводы и механизмы перечислены
в PARTIALLY ACCEPT, без переименования или слияния исходных ID.

## Needs clarification

Нет карточек с Verdict NEEDS CLARIFICATION.
Внутри R-02 остаются технические факты для этапа 0:
точный support/runtime floor нового client и host, реально используемые API/assets,
результат старых SDK controls и новый public profile.
Внутри G-01 остаётся проверка эффекта MSBUILDDISABLENODEREUSE на SDK 11.
Эти неизвестные явно сохранены; они не считаются PASS и не требуют выдумывать новый finding.

## Новые архитектурные риски

Новых ID NEW-D-* нет. Уточнения ownership MSBuild Server и разделения
NuGet/reference/runtime profiles относятся к уже предъявленным G-01 и R-02.
Общая причина R-01/R-02 — смешение сборки сервера, выбора SDK потребителя
и отдельного host; исходные карточки сохраняются раздельно.

## Изменения в спеке, если принять вердикты

Это предложения следующей ревизии; в этом проходе они не внесены.

- **docs/dotnet11-migration/README.md**:
  исправить фактический root pin; заменить «SDK 10» точными profiles и support minimum;
  различать workspace BuildHost и CLI MSBuild Server; добавить older-TFM C# 15 контроль.
  Сохранить отказ от preview и точные process-ownership границы.
- **task-00-baseline-and-dependencies-med.md**:
  baseline реального global.json/CLI/reader, отдельные feature bands,
  runtime/support requirements и точные ref-assets. Gate допускает явный пересмотр
  минимального SDK/MSBuild либо no-go, без маскировки результатов и смены fixture pin.
- **task-02-runtime-and-language-hi.md**:
  обновление существующего root global.json на точный SDK 11 и согласованный setup;
  отдельная проверка CLI server policy. Использовать общую расширенную языковую матрицу.
- **task-03-packaging-and-acceptance-hi.md**:
  MSBuild Server/process gate до тестового cleanup, own/foreign process distinction,
  child opt-out при необходимости; включить older-TFM semantic smoke.
- **task-01-roslyn-and-buildhost-hi.md**:
  отдельной карточки review нет, локальные требования не переписываются под новую находку.
  При исполнении применять уточнённый общий SDK/runtime/reference profile;
  это не свидетельство приёмки task-01.
- **docs/ARCHITECTURE.md**:
  в запланированной синхронизации reference-документации удалить устаревшее утверждение
  об отсутствии корневого global.json. Не объявлять AR-18 закрытым.
- **evidence/preparation-2026-11-18.md**:
  исторический отчёт не переписывать; исправление baseline-факта отразить
  новой ревизией плана и её evidence.

## На арбитраж

Безусловной продуктовой развилки для R-01, G-01 и R-03 нет:
исправления и проверки следуют исходной цели.

Условная развилка R-02 возникает, если новый набор действительно исключает
ранее подтверждённый поддерживаемый consumer SDK. Тогда выбрать:
явно поднять минимальную поддерживаемую полосу, приняв compatibility change,
либо отложить/пересмотреть обновление Roslyn ради прежнего диапазона.
Ни review, ни эта защита не являются решением владельца о снятии поддержки.
До получения evidence этапа 0 это условие, а не готовый нормативный verdict арбитра.
Две параллельные версии Roslyn/host или новая система SDK negotiation не предлагаются.

Технические неизвестные проверяются экспериментом/исходниками,
а не передаются арбитру как замена отсутствующим данным.
Оба исходных Blocker остаются открыты для интеграции/перепроверки;
карточки не закрыты одной записью ответов.

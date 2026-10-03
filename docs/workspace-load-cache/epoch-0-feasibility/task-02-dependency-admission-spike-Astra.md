# E0/task-02 — dependency-admission-spike

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — Неполный dependency profile способен разрешить reuse при изменившихся значимых входах.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `семантика_roslyn_msbuild`, `проверка_целостности_данных`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: [E0/task-00](task-00-current-baseline-and-execution-contract-Astra.md).

## Цель

Доказать замкнутость sdk-project-v1 и переносимую analyzer identity на выбранных источниках evidence.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Изолированные evaluation/binlog experiments; AnalyzerProvenanceCaptureService и SDK/Razor/Web targets как предмет исследования.

## Конкретный результат

Матрица positive/known-absent/region/toolset/target inputs, supported/unsupported/unknown для всего request, inner-TFM mapping и новый session-bound overlay snapshot после DLL recheck. Список реально непокрытых случаев.

## Проверки и evidence

V03–V06/V12–V14; custom Target, missing Exists, external linked input, явный Analyzer вне TaskOutput, Razor/Web assets вне SDK/project. Сверить compiler inputs с capture.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не считать binlog полным по умолчанию, не загружать DLL по DTO и не расширять профиль ради BTCPayServer без отдельного решения.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

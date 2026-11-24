# Roadmap

| Этап | Результат | Статус |
|---|---|---|
| 0. Фикстуры | `tests/Fixtures/LegacyShop`: Web API 2 + OWIN + Web Forms, WCF, EF6 + EDMX, чистая netstandard-библиотека | ✅ 0.1.0 |
| 1. Каркас | CLI `lms`, загрузка .sln/.slnx/.csproj без сборки, JSON-отчёт | ✅ 0.1.0 |
| 2. Детекторы | Project, FileArtifact, Config, Code (Roslyn syntax), Package; 40 правил | ✅ 0.1.0 |
| — Инженерная основа | CPM, analyzers (Recommended, warnings as errors), SourceLink, ADR, CI/CD, Dependabot | ✅ 0.2.0 |
| — Архитектура | Async DI-пайплайн, `IMigrationScanner`, LoggerMessage, параллельный анализ, Ctrl+C | ✅ 0.2.0 |
| 3. Пакеты онлайн | NuGet V3 (несколько фидов), ретраи, кеш с TTL, `--offline` | ✅ 0.2.0 |
| 5a. SARIF | SARIF 2.1.0 для GitHub Code Scanning | ✅ 0.2.0 |
| 4. Калибровка | Команда `lms calibrate`: подбор hoursPerPoint по фактическим часам прошлой миграции | ✅ 0.3.0 |
| 5b. HTML | Однофайловый HTML-дашборд (`-f html`): плитки, effort-бары P50/P90, фильтр находок, baseline | ✅ 0.3.0 |
| 6. Baseline | `--baseline <json>`: diff с прошлым прогоном по fingerprint, `--fail-on-new` для PR-гейтов | ✅ 0.3.0 |
| 7. Глубокий режим | MSBuildWorkspace + семантическая модель; анализ бинарных ссылок (HintPath DLL) | ⏳ |
| 8. AI-слой | `lms mcp`: MCP-сервер (stdio) с `scan_solution`, `get_findings`, `explain_finding`, `get_packages` и промптом `plan_migration` (LLM-план миграции) | ✅ Unreleased |

## Бэклог / известные ограничения

- Авторизация в приватных фидах (PAT / `nuget.config` credentials) не поддерживается.
- `[ServiceContract]` на клиентских прокси учитывается как серверный контракт (LMS210).
- Синтаксический анализ: `using System.Web;` + `HttpUtility` даёт LMS110, хотя `HttpUtility` есть в modern .NET.
- VB.NET-проекты пропускаются с предупреждением; MSBuild Condition/Import не вычисляются.
- Для пакетов без framework-групп в nuspec статус Unknown (нужен анализ lib/ в nupkg).

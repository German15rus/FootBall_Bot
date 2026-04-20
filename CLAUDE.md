# CLAUDE.md — FootballTg / PremierLeagueBot

## 0. Стиль работы с пользователем

- Пользователь учится программированию — предпочитает простые и понятные решения сложным
- Всегда объяснять что делается и почему, простым языком
- Не менять больше одного файла за раз без явного согласия пользователя
- Предупреждать, если изменение может сломать существующий код

---

## 1. Project Overview

Telegram-бот для фанатов Английской Премьер-Лиги (EPL), написанный на C# / .NET 8.
Основные функции:
- Таблица EPL в реальном времени, расписание матчей
- Уведомления за 15 минут до начала и сразу после окончания матча
- Состав любой команды, статистика игроков
- Новости BBC Sport о любимой команде

Весь пользовательский интерфейс — **на русском языке**.

---

## 2. Tech Stack

| Категория        | Технология                              |
|------------------|-----------------------------------------|
| Язык / фреймворк | C# 12, .NET 8.0, ASP.NET Core           |
| Telegram SDK     | `Telegram.Bot` v22.4                    |
| ORM / База данных| EF Core 8 + SQLite (dev) / PostgreSQL (prod) |
| Resilience       | `Polly` 8.3 — retry, circuit breaker   |
| Logging          | Serilog — console + rolling file        |
| Деплой           | Docker, Railway.app                     |

---

## 3. Project Structure

```
FootballTg/                          ← рабочая директория
└── FootBall_Bot/
    ├── PremierLeagueBot.sln         ← solution-файл
    ├── src/PremierLeagueBot/        ← ОСНОВНОЙ проект
    │   ├── Program.cs               ← точка входа
    │   ├── appsettings.json
    │   ├── Services/
    │   │   ├── Bot/                 ← BotHostedService, UpdateHandler
    │   │   ├── Football/            ← FootballApiClient (3 внешних API)
    │   │   ├── Background/          ← DataUpdateService, MatchNotificationService, NewsNotificationService
    │   │   ├── Notification/        ← NotificationService (рассылка)
    │   │   └── Emoji/               ← EmojiPackService
    │   ├── Data/                    ← AppDbContext, сущности, миграции
    │   ├── Formatters/              ← шаблоны сообщений пользователю
    │   └── Models/                  ← DTO для API-ответов, CallbackData
    └── FootBall_Bot-main/           ← резервная копия (не редактировать напрямую)
```

---

## 4. Build & Run

```bash
# Восстановление зависимостей
dotnet restore "FootBall_Bot/PremierLeagueBot.sln"

# Сборка
dotnet build "FootBall_Bot/src/PremierLeagueBot/PremierLeagueBot.csproj"

# Запуск (dev — SQLite)
dotnet run --project "FootBall_Bot/src/PremierLeagueBot/PremierLeagueBot.csproj"

# Применить миграции
dotnet ef database update --project "FootBall_Bot/src/PremierLeagueBot"

# Docker (PostgreSQL)
docker-compose up -d   # из FootBall_Bot/FootBall_Bot-main/
```

---

## 5. Configuration

- `appsettings.json` — `BotToken`, `ConnectionStrings`, URLs внешних API
- **`BotToken` НЕ должен коммититься в production** — передавать через env var `BotToken`
- Prod PostgreSQL: env var `ConnectionStrings__Default=Host=...;Database=plbot;...`
- `ASPNETCORE_ENVIRONMENT`:
  - `Development` → SQLite (`premier_league_bot.db`)
  - `Production` → PostgreSQL

---

## 6. External APIs

| API                           | Назначение              | Кэш TTL   |
|-------------------------------|-------------------------|-----------|
| `footballapi.pulselive.com`   | Standings, fixtures     | 15 / 10 мин |
| `thesportsdb.com`             | Squads / players        | 24 ч      |
| BBC Sport RSS                 | Новости о командах      | 1 ч       |

Клиент подделывает заголовки `Origin` / `Referer` для PL API (требует браузерный контекст).

---

## 7. Background Jobs

| Сервис                      | Интервал           | Задача                                      |
|-----------------------------|--------------------|---------------------------------------------|
| `DataUpdateService`         | 3 ч / 10 мин / 24 ч | Sync standings / матчи / составы в БД       |
| `MatchNotificationService`  | 30 с               | Уведомления за 15 мин до матча и после игры |
| `NewsNotificationService`   | 1 ч                | Рассылка новостей о любимой команде         |

---

## 8. Database Schema

- **User** — `TelegramId` (PK), `FavoriteTeamId` (FK → Team), `SessionToken` (для MiniApp)
- **Team** — `TeamId`, `Name`, `ShortName`, `EmblemUrl`
- **Match** — `MatchId`, `MatchDate`, `Status` (`scheduled`/`live`/`finished`), `PreMatchNotificationSent`, `PostMatchNotificationSent`, `CompetitionId`
- **Player** — `PlayerId`, `TeamId` (FK), `Name`, `Number`, `Position`
- **NotificationLog** — история отправленных сообщений (TelegramId, Message, SentAt)
- **Prediction** — предикты: `TelegramId` (FK), `MatchId` (FK), `PredictedHomeScore`, `PredictedAwayScore`, `PointsAwarded`, `IsScored`; уникальный индекс по (TelegramId, MatchId)
- **Achievement** — справочник ачивок (`Code` PK, `NameRu`, `Icon`, …)
- **UserAchievement** — связь пользователь↔ачивка (`TelegramId`, `AchievementCode`, `EarnedAt`)
- **Friendship** — заявки в друзья (`RequesterId`, `AddresseeId`, `Status`, `CreatedAt`)

Миграции в `Data/Migrations/`, применяются автоматически при старте через `MigrateAsync()`.

---

## 9. MiniApp / API

MiniApp — это веб-интерфейс внутри Telegram, добавленный поверх основного бота.

| Папка / файл                                      | Назначение                                      |
|---------------------------------------------------|-------------------------------------------------|
| `Controllers/`                                    | API-контроллеры (PredictionsController, etc.)   |
| `Infrastructure/TelegramAuthFilter.cs`            | Фильтр авторизации — SessionToken или initData  |
| `Infrastructure/TelegramInitDataValidator.cs`     | HMAC-валидация Telegram initData                |
| `wwwroot/`                                        | Фронтенд MiniApp (HTML/JS/CSS)                  |
| `Services/Background/PredictionScoringService.cs` | Начисление очков за предикты (каждую минуту)    |
| `Services/Achievements/AchievementService.cs`     | Выдача ачивок, seed данных при старте           |

**Авторизация API:** заголовок `X-Session-Token` (кэшируется после первого входа)
или `X-Telegram-Init-Data` (HMAC от Telegram). Фильтр создаёт нового User в БД если не найден.

**Background jobs (дополнение к разделу 7):**

| Сервис                     | Интервал | Задача                              |
|----------------------------|----------|-------------------------------------|
| `PredictionScoringService` | 1 мин    | Начисление очков за завершённые матчи |

---

## 10. Architecture Notes

**Long polling, не webhooks** — `BotHostedService` запускает `bot.ReceiveAsync()` в цикле
с авто-реконнектом (задержка 5 с). Выбрано для простоты деплоя без публичного HTTPS-endpoint.

**`IDbContextFactory<AppDbContext>`** — используется везде в фоновых сервисах, чтобы избежать
конкурентного доступа к одному `DbContext` из разных потоков (EF Core не thread-safe).

**Polly** — все HTTP-клиенты обёрнуты в retry (3×) + circuit breaker + timeout 30 с.
Это критично: внешние API (особенно PL) нестабильны.

**Два провайдера БД** — SQLite для локальной разработки (нет инфраструктуры), PostgreSQL
для продакшена. Переключение через `ConnectionStrings__Default` в env.

**Форматирование изолировано** — вся логика отображения (таблицы, составы, расписание)
живёт в `Formatters/`. `UpdateHandler` только маршрутизирует, не форматирует.

---

## 11. Health & Observability

- Endpoint: `GET /health` — проверяет подключение к БД (используется Docker health check)
- Logs: `logs/bot-YYYYMMDD.log` — ежедневная ротация, хранятся 7 дней
- Формат консоли: `[HH:mm:ss LEVEL] Message`

---

## 12. Deployment

| Среда   | Способ                                                    |
|---------|-----------------------------------------------------------|
| Local   | `dotnet run` + SQLite                                     |
| Docker  | `docker-compose up` — порт 8080, volume `/app/data`, `/app/logs` |
| Railway | Автодеплой через `railway.json` — DOCKERFILE build, restart on failure (max 10) |

---

## 13. Правила работы с миграциями и БД (КРИТИЧНО)

### Проблема: все миграции сгенерированы для SQLite

Все файлы в `Data/Migrations/` созданы локально (SQLite). Они содержат SQLite-специфичные типы:
- `type: "TEXT"` для DateTime-колонок
- `type: "INTEGER"` для bool-колонок
- `.Annotation("Sqlite:Autoincrement", true)` для int PK

**Критичное следствие:** аннотация `Sqlite:Autoincrement` **игнорируется Npgsql** →
в PostgreSQL колонки `Id` создаются без SERIAL/sequence → `INSERT` падает с ошибкой.

### Как проблема решена

В `Program.cs` есть функция `EnsurePostgresSerialSequences()`, которая запускается
при старте **после** `MigrateAsync()`. Она создаёт недостающие SERIAL-последовательности
для таблиц: `Predictions`, `NotificationLogs`, `UserAchievements`, `Friendships`.

**НИКОГДА не удалять этот вызов из Program.cs** — иначе INSERT в эти таблицы упадёт на Supabase.

### Правило: добавляешь новую таблицу с `int Id` (auto-increment)?

Обязательно добавь её имя в массив внутри `EnsurePostgresSerialSequences()` в `Program.cs`.

### Правило: НЕ делать сложные ALTER TABLE через EF миграции

Если `MigrateAsync()` падает — приложение крашится при старте, бот перестаёт отвечать.

- ✅ Можно в миграции: `CREATE TABLE`, `ADD COLUMN`, `CREATE INDEX`
- ❌ Нельзя: `ALTER COLUMN TYPE` (падает при наличии данных), multi-statement SQL в одном `Sql()`
- Для нестандартных исправлений: добавлять в `Program.cs` с `try-catch`, как `EnsurePostgresSerialSequences`

### Правило: как генерировать новые миграции

```bash
# Генерировать всегда локально (SQLite) — типы TEXT/INTEGER это ожидаемо
dotnet ef migrations add ИмяМиграции --project src/PremierLeagueBot

# После генерации проверить Up(): есть новая таблица с int Id + Autoincrement?
# → добавить имя таблицы в EnsurePostgresSerialSequences() в Program.cs
```

### Определение провайдера БД (Program.cs, строки ~88–106)

Провайдер определяется **по содержимому строки подключения**, не по `ASPNETCORE_ENVIRONMENT`:
- Содержит `"Host="` или `"postgresql://"` → Npgsql (PostgreSQL)
- Иначе → SQLite

В Railway **обязательно** должна быть переменная:
```
ConnectionStrings__Default = Host=...supabase.co;Database=postgres;Username=postgres;Password=...
```

Если переменной нет → SQLite → данные в файле контейнера → **теряются при каждом деплое**.

### Как проверить какая БД используется (Railway логи)

- Видно `PRAGMA journal_mode = 'wal'` → **SQLite** (переменная не установлена!)
- Видно только `Database migrations applied` без PRAGMA → **PostgreSQL** ✅

---

## 14. Известные проблемы

### Telegram 409 Conflict
```
Telegram Bot API error 409: Conflict: terminated by other getUpdates request
```
Railway перезапустил контейнер, старый инстанс ещё жив. Самоустраняется за ~30 с. Не критично.

### TheSportsDB HTTP 429
Ограничение частоты запросов при синхронизации составов. Обрабатывается Polly retry. Не критично.

### Клавиатура бота «пропала»
Reply-клавиатура обновляется только когда бот отправляет ответ с `replyMarkup`.
Если бот был недоступен → клавиатура у пользователя устаревшая.
**Решение:** написать боту `/start`.

---

## 15. Design Rationale

### Почему именно такая структура CLAUDE.md

**Выбранный подход: единый файл с акцентом на «почему»**

CLAUDE.md — это не документация для людей, а контекст для модели. Поэтому:
- Файл один, в корне — Клод читает его автоматически в каждом сеансе без дополнительных инструкций.
- Структура «от общего к частному»: обзор → команды → архитектурные решения.
- В «Architecture Notes» объясняются неочевидные решения (long polling, IDbContextFactory, Polly),
  а не просто перечисляются факты. Это помогает модели принимать корректные решения при изменении кода.

### Рассматривавшиеся альтернативы

| Вариант                                     | Почему отклонён                                                                 |
|---------------------------------------------|---------------------------------------------------------------------------------|
| Один раздел «Architecture» без обоснований  | Клод не поймёт, зачем Polly или IDbContextFactory — и может случайно упростить |
| Несколько файлов (`ARCHITECTURE.md` и т.д.) | Клод читает только CLAUDE.md по умолчанию; поддерживать несколько сложнее      |
| Только список команд                        | Команды есть в README; CLAUDE.md должен объяснять решения, а не дублировать    |
| Секция «TODO / Known issues»                | Устаревает быстро — лучше держать в issues/backlog, а не в CLAUDE.md           |

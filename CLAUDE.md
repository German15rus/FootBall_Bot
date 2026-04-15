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

- **User** — `TelegramId` (PK), `FavoriteTeamId` (FK → Team)
- **Team** — `TeamId`, `Name`, `ShortName`, `EmblemUrl`
- **Match** — `MatchId`, `MatchDate`, `Status` (`scheduled`/`live`/`finished`), `PreMatchNotificationSent`, `PostMatchNotificationSent`
- **Player** — `PlayerId`, `TeamId` (FK), `Name`, `Number`, `Position`
- **NotificationLog** — история отправленных сообщений (TelegramId, Message, SentAt)

Миграции в `Data/Migrations/`, применяются автоматически при старте через `MigrateAsync()`.

---

## 9. Architecture Notes

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

## 10. Health & Observability

- Endpoint: `GET /health` — проверяет подключение к БД (используется Docker health check)
- Logs: `logs/bot-YYYYMMDD.log` — ежедневная ротация, хранятся 7 дней
- Формат консоли: `[HH:mm:ss LEVEL] Message`

---

## 11. Deployment

| Среда   | Способ                                                    |
|---------|-----------------------------------------------------------|
| Local   | `dotnet run` + SQLite                                     |
| Docker  | `docker-compose up` — порт 8080, volume `/app/data`, `/app/logs` |
| Railway | Автодеплой через `railway.json` — DOCKERFILE build, restart on failure (max 10) |

---

## 12. Design Rationale

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

# SMTP-to-Telegram Forwarder

.NET 10 консольный сервис, который принимает входящие почтовые сообщения через SMTP и пересылает их содержимое в Telegram-чат. Все промежуточные данные хранятся в памяти (MemoryStream), temp-файлы не используются.

## Быстрый старт

```bash
dotnet restore && dotnet build
# Заполнить BotToken и ChatId в appsettings.json
dotnet run
```

Сервис слушает `localhost:2525`. Все входящие письма пересылаются в указанный Telegram-чат.

## Структура проекта

| Файл | Строк | Назначение |
|------|-------|------------|
| `SmtpToTelegram.csproj` | 19 | Скелет проекта, зависимости, копирование appsettings.json |
| `appsettings.json` | 9 | Конфигурация (SMTP порт, BotToken, ChatId) |
| `CLAUDE.md` | ~150 | Документация проекта |
| `Program.cs` | 81 | Точка входа, DI-контейнер, SMTP сервер, graceful shutdown, загрузка конфига |
| `ForwardingMessageStore.cs` | 50 | SmtpServer hook: IMessageStore + IMessageStoreFactory → MimeKit.MimeMessage → TelegramForwarder |
| `TelegramForwarder.cs` | 85 | Логика пересылки: извлечение тела, HTML→text (Textorizer), отправка в Telegram |
| `LoggingHelper.cs` | ~13 | Утилитный класс `Log`: Info/Error с префиксом `[INFO]` / `[ERROR]` |

**Итого: ~250 строк кода (без документации)**

## Зависимости

| Пакет | Версия | Назначение |
|-------|--------|------------|
| SmtpServer | 11.1.0 | SMTP сервер (IMessageStore, SmtpServerOptionsBuilder, ServiceProvider) |
| Telegram.Bot | 22.10.2 | Telegram Bot API клиент (SendMessage, SendDocument, InputFile, ChatId) |
| MimeKit | 4.17.0 | Парсинг MIME, HTML→text конвертация, декодирование вложений |
| Textorizer | 0.0.7.22 | Конвертер HTML → plain text (zero dependencies) |

## Конфигурация (`appsettings.json`)

```json
{
  "Smtp": {
    "Port": 2525
  },
  "Telegram": {
    "BotToken": "YOUR_BOT_TOKEN",
    "ChatId": 123456789
  }
}
```

Обязательно: **BotToken** (из @BotFather) и **ChatId** (numeric ID, long). Порт SMTP по умолчанию — **2525** (непривилегированный, работает без администратора на Windows).

### Поиск appsettings.json

Файл конфигурации ищется **только рядом с исполняемым файлом**:
```csharp
var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
```

Копируется в output автоматически через `<None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />` в `.csproj`. Фолбэков на проектную директорию нет.

## Архитектура пересылки

```
SMTP клиент ──► SmtpServer ──► IMessageStore.SaveAsync(buffer)
                                        │
                                  MimeKit.MimeMessage.LoadAsync(stream)
                                                 │
                                TelegramForwarder.ForwardEmailAsync()
                                         │                │           │
                                  text ≤ 4096   > 4096         attachments
                                  chars        chars           (MemoryStream)
                                      │          │                   │
                                 SendMessage Preview +            SendDocument
                                     (inline) email.txt         ("filename")
```

### Порядок обработки письма:

1. **Извлечение тела:** `TextBody` (string, MimeKit 4+) → `Textorize.HtmlToPlainText(HTML)` → "(no content)"
2. **Формирование payload:** header (`From/To/Subject`) + body text
3. **Отправка текста:**
   - ≤ 4096 → `SendMessage()` inline
   - > 4096 → первые 4096 символов `SendMessage()` (inline-превью) + полный текст через `SendDocument("email.txt")`
4. **Вложения:** каждый MimePart с ContentDisposition.FileName → MemoryStream → `SendDocument(fileName)`

## Логирование

Утилитный класс `Log` (LoggingHelper.cs) выводит сообщения с префиксом уровня:

```csharp
[INFO] Входящее письмо: From=test@example.com, To=admin@localhost, Subject=Hello
[INFO] Текст (111 символов) отправлен inline в Telegram.
[INFO] Вложение: report.pdf (45 123 байт) отправлено в Telegram.
[ERROR] Ошибка обработки письма: The remote name could not be resolved
```

- `Log.Info(msg)` → stdout, префикс `[INFO]`
- `Log.Error(ex, "context")` → stderr, префикс `[ERROR] context`

## SMTP обработчик ошибок

При исключении во время обработки письма (MimeKit парсинг или Telegram отправка) возвращается клиенту:

```csharp
return SmtpResponse.TransactionFailed;  // код 554 — транзакция не выполнена
```

В success-путе: `SmtpResponse.Ok` (код 250). **Нельзя** возвращать Ok при ошибке обработки.

## HTML-to-text конвертер

Используется **Textorizer** — легковесная библиотека без зависимостей:
- Убирает style/script блоки
- Декодирует HTML-сущности в Unicode (`&nbsp;` → пробел, `&#39;` → `'` и т.д.)
- Корректно обрабатывает невалидный HTML (часто во входящих письмах)

```csharp
Textorize.HtmlToPlainText(html);  // вместо ~85 строк кастомного парсера
```

## Kлючевые API детали

### SmtpServer 11.x
- `SmtpServerOptionsBuilder.Port(2525, false)` — no TLS
- `IMessageStore` + `IMessageStoreFactory` — интерфейс для обработки сообщений (НЕ MessageStore base class)
- `ServiceProvider.Add((IMessageStoreFactory)store)` — регистрация в DI
- `new SmtpServer(options, serviceProvider)` — конструктор сервера
- `StartAsync(ct)` блокируется до Shutdown. Для обнаружения ошибок привязки: `Task.WhenAny(startTask, Task.Delay(2000))`
- Доступные статические поля `SmtpResponse`: `Ok`, `TransactionFailed`, `MailboxUnavailable`, `SyntaxError`, `ServiceClosingTransmissionChannel`, `AuthenticationFailed`, и др.

### Telegram.Bot v22 (без Async!)
- Нет суффикса `Async` в именах методов: `SendMessage`, `SendDocument`
- `ChatId` = конструктор из long (`new ChatId(chatId)`) или string (`new ChatId("@name")`)
- `InputFile.FromStream(stream, "name")` для файлов из MemoryStream

### MimeKit 4+
- `message.TextBody` — plain text тело (string, НЕ TextPart в v4+)
- `message.HtmlBody` — HTML строка
- `attachment.Content.DecodeToAsync(MemoryStream)` — декодирование в память
- `attachment.ContentDisposition?.FileName` — имя файла вложения

### MemoryStream вместо temp-файлов
| Что | Реализация |
|-----|------------|
| Тело > 4096 → preview | SendMessage(header[..MaxMessageLength]) inline |
| Тело > 4096 → full | MemoryStream + StreamWriter → SendDocument("email.txt") |
| Вложения | part.Content.DecodeToAsync(MemoryStream) → SendDocument |
| Ресурсы | `using var ms = new MemoryStream()` — автоматическая очистка |

## Стиль кодирования

| Правило | Пример |
|---------|--------|
| Нет `var` — всегда явный тип | `CancellationTokenSource cts = new ();` |
| Max 120 символов на строку | Длинные выражения разбивать через `+` |
| Primary constructors | `class Foo(Bar dep) : IFoo { ... }` |
| Using по алфавиту | `SmtpServer < System.Buffers < Telegram.Bot` |

## Troubleshooting

| Проблема | Решение |
|----------|---------|
| BotToken пустой | Сервис не запустится, бросит ArgumentException. Проверить appsettings.json |
| ChatId = 0 | Отправка уйдёт на ID 0 (несуществующий чат), ошибка от Telegram. Проверить appsettings.json |
| Порт 2525 занят | Закрыть процесс, использующий порт. Или изменить в appsettings.json |
| Письмо не пришло в Telegram | Проверить stdout/stderr — ошибки помечены префиксом `[ERROR]` |
| Inline-вложения (CSS/JS) | MimeKit `attachments` включает inline-вложения. Фильтрация по ContentDisposition.FileName != null |

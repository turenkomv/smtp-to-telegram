# SMTP-to-Telegram Forwarder

Консольный сервис на .NET 10, который принимает входящие почтовые сообщения через SMTP и автоматически пересылает их содержимое в Telegram-чат.

## Как это работает

Отправьте письмо на `localhost:2525` — оно появится в вашем Telegram-чате как сообщение (или файл, если тело слишком длинное). Вложения тоже передаются.

## Быстрый старт

```powershell
# Восстановить зависимости и собрать
dotnet restore; dotnet build

# Заполнить BotToken и ChatId в appsettings.json
# См. ниже раздел "Конфигурация"

# Запустить
dotnet run
```

Сервис запустится на порту **2525** (непривилегированный, работает без администратора на Windows).

## Конфигурация

Отредактируйте `appsettings.json`:

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

### Получение BotToken и ChatId

1. **BotToken**: напишите @BotFather в Telegram, создайте бота командой `/newbot` — получите токен.
2. **ChatId** (numeric ID): отправьте ваше сообщение любому пользователю или в чат с вашим ботом, затем:
   ```powershell
   $token = "YOUR_BOT_TOKEN"
   ($Invoke-RestMethod "https://api.telegram.org/bot$token/getUpdates").result[0].chat.id
   ```
   Ищите поле `"id"` в ответе.

> Порт по умолчанию **2525** — работает без прав администратора на Windows.

## Как отправить письмо

Одной командой в PowerShell:

```powershell
Send-MailMessage `
    -SmtpServer "localhost" `
    -Port 2525 `
    -From "test@example.com" `
    -To "admin@localhost" `
    -Subject "SMTP test" `
    -Body "Hello!"
```

## Что происходит с письмом

| Длина заголовка + тела | Поведение |
|------------------------|-----------|
| ≤ 4096 символов | Сообщение приходит **inline** в Telegram |
| > 4096 символов | Первые 4096 — inline (превью) + полный текст как файл `email.txt` |
| Вложения | Каждый файл отправляется отдельным документом с оригинальным именем |

## Структура проекта

| Файл | Назначение |
|------|------------|
| `SmtpToTelegram.csproj` | Зависимости и настройки сборки |
| `appsettings.json` | Конфигурация (порт, токен бота, ID чата) |
| `Program.cs` | Точка входа, DI, SMTP-сервер, graceful shutdown |
| `ForwardingMessageStore.cs` | Обработка входящего SMTP → парсинг MimeKit |
| `TelegramForwarder.cs` | Извлечение тела, конвертация HTML→text, отправка в Telegram |
| `LoggingHelper.cs` | Утилитное логирование с префиксом `[INFO]` / `[ERROR]` |

## Зависимости

| Пакет | Версия | Назначение |
|-------|--------|------------|
| [SmtpServer](https://www.nuget.org/packages/SmtpServer/) | 11.1.0 | Встроенный SMTP-сервер |
| [Telegram.Bot](https://www.nuget.org/packages/Telegram.Bot/) | 22.10.2 | Telegram Bot API клиент |
| [MimeKit](https://www.nuget.org/packages/MimeKit/) | 4.17.0 | Парсинг MIME, декодирование вложений |
| [Textorizer](https://www.nuget.org/packages/Textorizer/) | 0.0.7.22 | Конвертер HTML → plain text |

## Troubleshooting

| Проблема | Решение |
|----------|---------|
| `BotToken` пустой | Сервис не запустится — бросит `ArgumentException`. Проверить `appsettings.json` |
| Порт 2525 занят | Закрыть процесс, использующий порт. Или изменить значение в `appsettings.json` |
| Письмо не пришло в Telegram | Посмотреть stdout/stderr сервиса — там лог с префиксом `[INFO]` / `[ERROR]` |
| Inline-вложения (CSS/JS) | MimeKit включает inline-вложения как обычные. Фильтрация по `ContentDisposition.FileName != null` |

## Ограничения Telegram API

- **Максимальный размер сообщения**: 4096 символов — длинные письма разбиваются на preview + файл
- **Поддержка формата текста**: `MarkdownV2` (автоматическое экранирование спецсимволов)
- **Максимальный размер вложения**: 50 МБ через Bot API

## Развёртывание на Alpine Linux (OpenRC)

### Публикация

```sh
dotnet publish -c Release -r linux-musl-x64 --self-contained -p:PublishSingleFile=true -o out
```

Скопировать `out/` на сервер:

```sh
scp -r out/* root@server:/app/
```

### Необходимые пакеты ICU

Для корректной работы .NET 10 на Alpine требуется установить библиотеки локализации:

```sh
apk add libstdc++ libgcc icu-libs
```

Без `icu-libs` сервис упадёт при попытке конвертировать символы (Unicode NFKC/NFKD).

### Служба OpenRC

Создать `/etc/init.d/smtp-to-telegram`:

```sh
#!/sbin/openrc-run

description="SMTP-to-Telegram Forwarder"
command="/app/SmtpToTelegram"
pidfile="/var/run/smtp-to-telegram.pid"
directory="/app"
supervisor=supervise-daemon
output_logger="logger -p user.info -t smtp-to-telegram"
error_logger="logger -p user.error -t smtp-to-telegram"
respawn_delay=2
respawn_max=10
respawn_period=60

depend() {
    need net
}
```

Включить и запустить:

```sh
chmod +x /etc/init.d/smtp-to-telegram
rc-update add smtp-to-telegram default
service smtp-to-telegram start
```

## Лицензия

MIT

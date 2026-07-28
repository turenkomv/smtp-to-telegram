using SmtpServer;
using SmtpServer.ComponentModel;
using SmtpServer.Storage;
using System.Text.Json;
using Telegram.Bot;

ConfigData config = LoadConfig();
Log.Info(
    $"Конфигурация: SMTP port={config.Smtp.Port}, " +
    $"Telegram BotToken={MaskToken(config.Telegram.BotToken)}, " +
    $"ChatId={config.Telegram.ChatId}");

CancellationTokenSource cts = new ();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    TelegramBotClient telegramClient = new (config.Telegram.BotToken);
    ForwardingMessageStore messageStore = new (telegramClient, config.Telegram.ChatId);
    Log.Info("Telegram бот подключён.");

    ISmtpServerOptions options = new SmtpServerOptionsBuilder()
        .ServerName("localhost")
        .Port(config.Smtp.Port, false)
        .MaxMessageSize(5242880, MaxMessageSizeHandling.Strict)
        .Build();

    ServiceProvider serviceProvider = new ();
    serviceProvider.Add((IMessageStoreFactory)messageStore);

    SmtpServer.SmtpServer smtpServer = new (options, serviceProvider);

    Log.Info($"SMTP сервер стартует на порту {config.Smtp.Port}...");
    Task startTask = smtpServer.StartAsync(cts.Token);
    await Task.WhenAny(startTask, Task.Delay(2000));
    if (startTask.IsCompleted)
    {
        try { await startTask; } catch (Exception ex) { Log.Error(ex.Message); }
        return;
    }

    Log.Info("SMTP сервер готов к приёму писем.");

    try
    {
        await smtpServer.ShutdownTask;
    }
    catch (OperationCanceledException)
    {
        // Expected during shutdown
    }
    finally
    {
        Log.Info("Завершение работы...");
        smtpServer.Shutdown();
        Log.Info("Остановлен.");
    }
}
catch (ArgumentException ex) when (ex.Message.Contains("BotToken") || ex.ParamName == "apiKey")
{
    Log.Error(ex, $"Telegram Bot: невалидный токен. Заполните appsettings.json — BotToken.");
    return;
}
catch (Exception ex)
{
    Log.Error(ex, "Ошибка инициализации: ");
    return;
}

static string MaskToken(string token) => token.Length > 10 ? token[..10] + "..." : token;

static ConfigData LoadConfig()
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (!File.Exists(path))
        throw new FileNotFoundException("appsettings.json not found at: " + path);
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<ConfigData>(json)!;
}

record ConfigData(SmtpSection Smtp, TelegramSection Telegram);
record SmtpSection(int Port);
record TelegramSection(string BotToken, long ChatId);

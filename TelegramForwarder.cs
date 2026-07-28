using MimeKit;
using Telegram.Bot;
using Telegram.Bot.Types;
using Textorizer;

public static class TelegramForwarder
{
    // https://core.telegram.org/bots/api#message-size — 4096 chars max
    private const int TelegramMaxMessageLength = 4096;

    public static async Task ForwardEmailAsync(
        TelegramBotClient client, long chatId, MimeMessage message)
    {
        string subject = message.Subject ?? "(no subject)";
        string from = MimeHelpers.MailboxFormat(message.From);
        string to = MimeHelpers.MailboxFormat(message.To);

        string text = ExtractBodyText(message);
        string header = $"From: {from}\nTo: {to}\nSubject: {subject}\n\n{text}";
        ChatId chat = new (chatId);

        if (header.Length <= TelegramMaxMessageLength)
        {
            Log.Info(
                $"Текст ({header.Length} символов) отправлен inline в Telegram.");
            await client.SendMessage(chat, header);
        }
        else
        {
            string preview = header[..TelegramMaxMessageLength];
            Log.Info(
                $"Тело длинное ({header.Length} символов): первые " +
                $"{TelegramMaxMessageLength} отправлены inline, " +
                "полный текст — файл email.txt");
            await client.SendMessage(chat, preview);

            using MemoryStream ms = new ();
            await using StreamWriter tw = new (ms, leaveOpen: true);
            await tw.WriteAsync(header);
            await tw.FlushAsync();

            ms.Position = 0;
            await client.SendDocument(
                chat, InputFile.FromStream(ms, "email.txt"));
        }

        int attachmentCount = 0;
        foreach (var attachment in message.Attachments)
        {
            if (attachment is not MimePart part ||
                string.IsNullOrEmpty(part.ContentDisposition?.FileName))
                continue;

            string fileName = part.ContentDisposition.FileName;
            if (part.Content == null)
            {
                Log.Error(
                    $"Вложение '{fileName}': Content отсутствует, пропускаем.");
                attachmentCount++;
                continue;
            }

            try
            {
                using MemoryStream ms = new ();
                await part.Content.DecodeToAsync(ms);
                long size = ms.Length;
                ms.Position = 0;
                Log.Info(
                    $"Вложение: {fileName} ({size:N0} байт) " +
                    "отправлено в Telegram.");
                attachmentCount++;
                await client.SendDocument(
                    chat, InputFile.FromStream(ms, fileName));
            }
            catch (Exception ex)
            {
                attachmentCount++;
                Log.Error(ex, $"Ошибка отправки вложения '{fileName}': ");
            }
        }

        Log.Info($"Пересылка завершена: тело + {attachmentCount} вложений.");
    }

    private static string ExtractBodyText(MimeMessage message)
    {
        if (!string.IsNullOrEmpty(message.TextBody))
            return message.TextBody;

        var html = message.HtmlBody;
        if (!string.IsNullOrEmpty(html))
            return Textorize.HtmlToPlainText(html);

        return "(no content)";
    }
}

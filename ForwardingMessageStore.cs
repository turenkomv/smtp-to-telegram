using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using Telegram.Bot;

public class ForwardingMessageStore(
    TelegramBotClient client,
    long chatId) : IMessageStore, IMessageStoreFactory
{
    public IMessageStore CreateInstance(ISessionContext context) => this;

    public async Task<SmtpResponse> SaveAsync(
        ISessionContext context,
        IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            using MemoryStream stream = new ();
            await stream.WriteAsync(buffer.ToArray(), cancellationToken);

            stream.Position = 0;
            MimeKit.MimeMessage message =
                await MimeKit.MimeMessage.LoadAsync(stream, cancellationToken);

            Log.Info(
                $"Входящее письмо: " +
                $"From={MimeHelpers.MailboxFormat(message.From)}, " +
                $"To={MimeHelpers.MailboxFormat(message.To)}, " +
                $"Subject={message.Subject ?? "(no subject)"}");

            await TelegramForwarder.ForwardEmailAsync(client, chatId, message);

            return SmtpResponse.Ok;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка обработки письма: ");
            return SmtpResponse.TransactionFailed;
        }
    }
}

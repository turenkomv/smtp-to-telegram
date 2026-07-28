using MimeKit;

public static class MimeHelpers
{
    public static string MailboxFormat(InternetAddressList? addresses) =>
        string.Join(", ",
            addresses?.OfType<MailboxAddress>().Select(a => a.Address) ?? Array.Empty<string>());
}

using System.Net;
using System.Net.Mail;
using Bladez.Core;

namespace BarberBooking.Web.Services;

public sealed class AccountMail(ShopClock clock, DataVault vault, ILogger<AccountMail> logger)
{
    public async Task<bool> SendAsync(string email, string subject, string body)
    {
        var settings = await clock.SettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpFrom)) return false;
        try
        {
            var secrets = vault.ReadSecrets();
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort) { EnableSsl = true, Timeout = 15_000 };
            if (!string.IsNullOrWhiteSpace(settings.SmtpUser)) client.Credentials = new NetworkCredential(settings.SmtpUser, secrets.SmtpPassword);
            using var message = new MailMessage(settings.SmtpFrom, email, subject, body);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.SendMailAsync(message, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SmtpException or IOException or OperationCanceledException or FormatException or System.Security.Cryptography.CryptographicException)
        { logger.LogWarning("Account email delivery failed ({ErrorType}).", exception.GetType().Name); return false; }
    }
}

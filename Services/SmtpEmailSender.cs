using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Infrastructure;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CandidatePortal.Api.Services;

public interface IEmailSender
{
    Task SendAsync(
        string recipientAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(
    PortalOptions options,
    IHttpClientFactory httpClientFactory,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedAccessToken;
    private DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

    public async Task SendAsync(
        string recipientAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(options.EmailDeliveryMode, "development", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "DEV EMAIL from {Sender} to {Recipient}. Subject: {Subject}. Body: {Body}",
                options.EmailSenderAddress,
                recipientAddress,
                subject,
                body);
            return;
        }

        if (!string.Equals(options.EmailDeliveryMode, "smtp", StringComparison.OrdinalIgnoreCase) ||
            !options.SmtpConfigured)
        {
            throw new ApiException(503, "SMTP email delivery is not configured");
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            var message = new MimeMessage
            {
                Subject = subject,
                Body = new TextPart("plain") { Text = body },
            };
            message.From.Add(new MailboxAddress(options.EmailSenderName, options.EmailSenderAddress));
            message.To.Add(MailboxAddress.Parse(recipientAddress));

            using var client = new SmtpClient
            {
                Timeout = options.SmtpTimeoutSeconds * 1000,
            };
            var socketOptions = options.SmtpEnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(options.SmtpHost, options.SmtpPort, socketOptions, cancellationToken);
            await client.AuthenticateAsync(
                new SaslMechanismOAuth2(options.SmtpUsername, accessToken),
                cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Office 365 SMTP delivery to {Recipient} failed", recipientAddress);
            throw new ApiException(503, "Email delivery failed. Please try again later.");
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (TokenIsValid())
        {
            return cachedAccessToken!;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (TokenIsValid())
            {
                return cachedAccessToken!;
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.SmtpClientId,
                ["client_secret"] = options.SmtpClientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "https://outlook.office365.com/.default",
            });
            var tokenEndpoint =
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(options.SmtpTenantId)}/oauth2/v2.0/token";
            using var response = await httpClientFactory.CreateClient().PostAsync(
                tokenEndpoint,
                content,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException("Microsoft identity platform returned an empty SMTP access token");
            }

            cachedAccessToken = token.AccessToken;
            accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60));
            return cachedAccessToken;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private bool TokenIsValid() =>
        !string.IsNullOrWhiteSpace(cachedAccessToken) &&
        accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);

    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

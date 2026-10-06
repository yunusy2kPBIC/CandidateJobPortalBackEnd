using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
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
        string purpose,
        CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailSender(
    PortalOptions options,
    IHttpClientFactory httpClientFactory,
    EmailAuditService emailAudit,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedAccessToken;
    private DateTimeOffset accessTokenExpiresAt = DateTimeOffset.MinValue;

    public async Task SendAsync(
        string recipientAddress,
        string subject,
        string body,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        var emailLogId = await emailAudit.StartAsync(recipientAddress, purpose, subject);
        if (!options.EmailEnabled)
        {
            logger.LogInformation(
                "EMAIL DISABLED: skipped message from {Sender} to {Recipient}. Subject: {Subject}",
                options.EmailSenderAddress,
                recipientAddress,
                subject);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Skipped, "Email delivery is disabled");
            return;
        }

        if (!options.SmtpConfigured)
        {
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, "SMTP email delivery is not configured");
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
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Sent);
        }
        catch (OperationCanceledException exception)
        {
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw;
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Microsoft OAuth token acquisition for SMTP failed");
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                "Microsoft OAuth authentication failed. Verify the SMTP tenant, client ID, and client secret.",
                exception);
        }
        catch (MailKit.Security.AuthenticationException exception)
        {
            logger.LogError(exception, "Office 365 SMTP authentication for {Username} failed", options.SmtpUsername);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                "SMTP authentication failed. Verify SMTP.SendAsApp permission, admin consent, and the SMTP username.",
                exception);
        }
        catch (SmtpCommandException exception) when (IsMailboxAccessFailure(exception))
        {
            logger.LogError(exception, "Office 365 could not open sender mailbox {Sender}", options.EmailSenderAddress);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                "The sender mailbox could not be opened. Verify that it is active and grant the Exchange service principal FullAccess to it.",
                exception);
        }
        catch (SmtpCommandException exception)
        {
            logger.LogError(
                exception,
                "Office 365 rejected SMTP delivery from {Sender} to {Recipient} with status {StatusCode}",
                options.EmailSenderAddress,
                recipientAddress,
                (int)exception.StatusCode);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                $"The email server rejected the message with SMTP status {(int)exception.StatusCode}. Verify the sender and recipient addresses.",
                exception);
        }
        catch (Exception exception) when (
            exception is SocketException or IOException or SmtpProtocolException)
        {
            logger.LogError(exception, "SMTP connection to {Host}:{Port} failed", options.SmtpHost, options.SmtpPort);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                "Could not communicate with the SMTP server. Verify the SMTP host, port, TLS settings, and network access.",
                exception);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Office 365 SMTP delivery to {Recipient} failed", recipientAddress);
            await emailAudit.CompleteAsync(emailLogId, DeliveryStatuses.Failed, exception.Message);
            throw new ApiException(
                503,
                "Email delivery failed unexpectedly. Review the backend log for the underlying SMTP error.",
                exception);
        }
    }

    private static bool IsMailboxAccessFailure(SmtpCommandException exception) =>
        (int)exception.StatusCode == 430 ||
        exception.Message.Contains("Cannot open mailbox", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("MapiExceptionLogonFailed", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("no rights on this session", StringComparison.OrdinalIgnoreCase);

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

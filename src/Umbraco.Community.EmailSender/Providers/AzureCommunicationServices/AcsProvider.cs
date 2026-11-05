using System.Text.RegularExpressions;
using Azure;
using Azure.Communication.Email;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.AzureCommunicationServices;

/// <summary>
/// Sends through the Azure Communication Services Email SDK (design Q3: the SDK, not hand-rolled request signing).
/// A singleton over <c>IHttpClientFactory</c> and <c>IOptionsMonitor</c> (pitfalls P1 and P2); the client is built on first
/// use so start-up makes no network call (P7).
/// </summary>
internal sealed partial class AcsProvider(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<EmailSenderOptions> options,
    TimeProvider clock,
    ILogger<AcsProvider> logger) : IEmailProvider
{
    public const string ClientName = "community.emailSender.acs";

    private const string ConnectionStringKey = "EmailSender:AzureCommunicationServices:ConnectionString";

    private readonly object _gate = new();
    private (string ConnectionString, EmailClient Client)? _cached;

    public string Name => "Azure Communication Services";

    public async Task SendAsync(OutboundEmail email, CancellationToken ct)
    {
        var current = options.CurrentValue;
        try
        {
            var client = ClientFor(current.AzureCommunicationServices.ConnectionString);
            var message = ToMessage(email, current);
            var operation = await ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct,
                // Started: the request thread must not poll until delivery (core would hold an invite request open).
                token => client.SendAsync(WaitUntil.Started, message, token));
            logger.LogInformation("Azure Communication Services accepted operation {OperationId}", operation.Id);
        }
        catch (RequestFailedException ex) when (ex.Status == 0)
        {
            // The SDK reports DNS, connection and TLS failures as status 0.
            throw new EmailSendException($"{Name} could not be reached.");
        }
        catch (RequestFailedException ex)
        {
            // No inner exception: the SDK's message and response can echo the request. Only the status and a short
            // service error code travel on.
            var code = ex.ErrorCode is { Length: > 0 } c && SafeCode().IsMatch(c) ? $", error {c}" : string.Empty;
            throw new EmailSendException($"{Name} rejected the email: HTTP {ex.Status}{code}.");
        }
        catch (Exception ex) when (ex is not (EmailSendException or OperationCanceledException))
        {
            throw new EmailSendException($"{Name} send failed: {ex.GetType().Name}.");
        }
    }

    public async Task<HealthCheckStatus> CheckAsync(CancellationToken ct)
    {
        var current = options.CurrentValue;
        if (!TryParse(current.AzureCommunicationServices.ConnectionString, out var endpoint, out _))
        {
            return Error($"The connection string is not valid. Check {ConnectionStringKey}.");
        }

        try
        {
            // Nothing secret is sent: any answer proves the endpoint resolves and speaks TLS. Sending itself cannot be
            // proven without sending an email.
            await ProviderTimeout.RunAsync(Name, current.TimeoutSeconds, clock, ct, async token =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                using var response = await httpClientFactory.CreateClient(ClientName)
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                return response.StatusCode;
            });
            return new HealthCheckStatus($"The {Name} endpoint answered. This check cannot prove that sending works without sending an email.")
            {
                ResultType = StatusResultType.Success,
            };
        }
        catch (EmailSendException ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error($"The {Name} endpoint could not be reached ({ex.GetType().Name}).");
        }

        static HealthCheckStatus Error(string message) => new(message) { ResultType = StatusResultType.Error };
    }

    private EmailClient ClientFor(string? connectionString)
    {
        if (!TryParse(connectionString, out var endpoint, out var accessKey))
        {
            throw new EmailSendException($"The Azure Communication Services connection string is not valid. Check {ConnectionStringKey}.");
        }

        lock (_gate)
        {
            if (_cached is { } cached && cached.ConnectionString == connectionString)
            {
                return cached.Client;
            }

            var clientOptions = new EmailClientOptions
            {
                Transport = new HttpClientTransport(httpClientFactory.CreateClient(ClientName)),
            };
            // The SDK's default retries would resend a POST that may already have been accepted, and send the same
            // invite twice.
            clientOptions.Retry.MaxRetries = 0;
            // ProviderTimeout (at most MaxTimeoutSeconds) is the bound; the SDK's own 100 s network timeout would
            // otherwise cancel first and bypass the timeout message.
            clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(EmailSenderOptionsValidator.MaxTimeoutSeconds + 100);
            clientOptions.Diagnostics.IsLoggingContentEnabled = false;

            // Built from the parts TryParse accepted, never from the raw string: the SDK's own parser is more lenient
            // about duplicate keys and echoes segments in its error text.
            var client = new EmailClient(endpoint, new AzureKeyCredential(accessKey), clientOptions);
            _cached = (connectionString!, client);
            return client;
        }
    }

    private static EmailMessage ToMessage(OutboundEmail email, EmailSenderOptions current)
    {
        // ACS takes a bare sender address; a display name from the From setting is dropped.
        var sender = MailboxAddress.Parse(email.From).Address;
        var recipients = new EmailRecipients(email.To.Select(a => new EmailAddress(a)));
        foreach (var cc in email.Cc)
        {
            recipients.CC.Add(new EmailAddress(cc));
        }

        foreach (var bcc in email.Bcc)
        {
            recipients.BCC.Add(new EmailAddress(bcc));
        }

        var content = new EmailContent(email.Subject) { Html = email.HtmlBody, PlainText = email.TextBody };
        var message = new EmailMessage(sender, recipients, content)
        {
            UserEngagementTrackingDisabled = current.DisableLinkTracking ? true : null,
        };

        foreach (var replyTo in email.ReplyTo)
        {
            message.ReplyTo.Add(new EmailAddress(replyTo));
        }

        foreach (var header in email.Headers)
        {
            message.Headers.Add(header.Key, header.Value);
        }

        foreach (var attachment in email.Attachments)
        {
            message.Attachments.Add(new EmailAttachment(attachment.Name, attachment.ContentType, BinaryData.FromBytes(Convert.FromBase64String(attachment.Base64))));
        }

        return message;
    }

    private static readonly string[] AllowedHostSuffixes = [".communication.azure.com", ".communication.azure.us", ".communication.azure.cn"];

    /// <summary>
    /// The single interpretation of the connection string: exactly <c>endpoint=https://&lt;resource&gt;.communication.azure.*/</c>
    /// and a base64 <c>accesskey</c>, each once. Anything else is refused and no part of the value is echoed.
    /// </summary>
    internal static bool TryParse(string? connectionString, out Uri endpoint, out string accessKey)
    {
        endpoint = null!;
        accessKey = string.Empty;
        string? endpointText = null;
        string? keyText = null;
        foreach (var part in (connectionString ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("endpoint", StringComparison.OrdinalIgnoreCase) && endpointText is null)
            {
                endpointText = value;
            }
            else if (key.Equals("accesskey", StringComparison.OrdinalIgnoreCase) && keyText is null)
            {
                keyText = value;
            }
            else
            {
                return false; // duplicate or unknown key
            }
        }

        if (string.IsNullOrEmpty(keyText)
            || !Convert.TryFromBase64String(keyText, new byte[keyText.Length], out _)
            || !Uri.TryCreate(endpointText, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || Uri.CheckHostName(parsed.Host) != UriHostNameType.Dns
            || !AllowedHostSuffixes.Any(suffix => parsed.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        endpoint = parsed;
        accessKey = keyText;
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$")]
    private static partial Regex SafeCode();
}

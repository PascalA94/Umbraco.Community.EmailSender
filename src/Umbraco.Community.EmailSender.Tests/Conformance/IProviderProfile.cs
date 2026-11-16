using System.Net;
using Umbraco.Community.EmailSender.Configuration;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>
/// One provider's view for the conformance suite (design 9.5): options with sentinel secrets, how to read a recorded request,
/// and what the provider is expected to do. Every scenario it cannot run is named in <see cref="NotApplicable"/> with a reason.
/// </summary>
internal interface IProviderProfile
{
    EmailProviderKind Kind { get; }

    /// <summary>The folder under <c>Fixtures/</c>, or null when the provider has no recorded examples (ACS, Notify): the
    /// shape match is then skipped and the label stays "Unverified" (design 9.7).</summary>
    string? FixtureFolder { get; }

    /// <summary>Fresh options with this provider selected and sentinel secrets set.</summary>
    EmailSenderOptions Options();

    /// <summary>Every secret in <see cref="Options"/>, for the leak sweep and the credential-location check.</summary>
    IReadOnlyList<string> Secrets { get; }

    RecordedEmail Normalise(HttpRequestMessage request, byte[] body);

    ProfileExpectations Expect { get; }

    /// <summary>Scenario id to the reason it cannot apply. A blank reason fails the suite.</summary>
    IReadOnlyDictionary<string, string> NotApplicable { get; }
}

/// <summary>A response the fake server sends, and a fragment the resulting error text must contain.</summary>
internal sealed record ResponseSignal(HttpStatusCode Status, string Body, string ExpectedText);

internal sealed record ProfileExpectations
{
    /// <summary>Where the secret may appear, for example <c>header:X-Postmark-Server-Token</c> or <c>body</c>.</summary>
    public required IReadOnlyList<string> CredentialLocations { get; init; }

    /// <summary>Paths (as <see cref="FixtureLoader.Shape"/> prints them) every request must carry.</summary>
    public IReadOnlyList<string> RequiredPaths { get; init; } = [];

    /// <summary>Path prefixes we send on purpose that the fixtures do not show.</summary>
    public IReadOnlyList<string> ShapeExtras { get; init; } = [];

    public bool KeepsDisplayName { get; init; } = true;

    /// <summary>False where attachments are refused before sending (Notify); scenarios then send none.</summary>
    public bool SupportsAttachments { get; init; } = true;

    /// <summary>The provider turns HTML into its own text body (Notify).</summary>
    public bool HtmlAsText { get; init; }

    public ReplyToStyle ReplyTo { get; init; }

    public HeaderPolicy Headers { get; init; }

    public TrackingStyle Tracking { get; init; }

    public IdempotencyCarriage Idempotency { get; init; }

    public InlineContentId Inline { get; init; }

    public bool FailClosedOn2xx { get; init; }

    public HttpStatusCode SuccessStatus { get; init; } = HttpStatusCode.OK;

    /// <summary>The success body: a fixture file name, or inline JSON when it starts with <c>{</c> or <c>[</c>.</summary>
    public string SuccessBody { get; init; } = "response-success.json";

    public IReadOnlyDictionary<string, string> SuccessHeaders { get; init; } = new Dictionary<string, string>();

    /// <summary>The id the success log must name, or "none".</summary>
    public required string MessageId { get; init; }

    public required ResponseSignal Auth { get; init; }

    /// <summary>The exact error text for the validation fixture.</summary>
    public required ResponseSignal Validation { get; init; }

    public ResponseSignal? UnverifiedSender { get; init; }

    public ResponseSignal? Quota { get; init; }

    public ResponseSignal? FailureIn2xx { get; init; }

    public int? MaxCc { get; init; }

    public int? MaxBcc { get; init; }

    public long? MaxRequestBytes { get; init; }
}

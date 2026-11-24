using Umbraco.Community.EmailSender.Providers;
using Umbraco.Community.EmailSender.Providers.Pipeline;

namespace Umbraco.Community.EmailSender.Tests.Pipeline;

/// <summary>Design 9.2 step 1: every cap is checked before any request, so an oversized email never reaches the provider.</summary>
public class PreCheckTests
{
    private static OutboundAttachment Attachment(int rawBytes)
        => new("a.pdf", "application/pdf", Convert.ToBase64String(new byte[rawBytes]));

    private static async Task<EmailSendException> RefusedAsync(PipelineRig rig, OutboundEmail email)
    {
        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync(email));
        Assert.Empty(rig.Handler.Calls);
        Assert.Null(ex.InnerException);
        return ex;
    }

    [Fact]
    public async Task RecipientCap_51_RefusedBeforeSend()
    {
        var ex = await RefusedAsync(new PipelineRig(), PipelineRig.Email(to: 40, cc: 6, bcc: 5));

        Assert.Equal("Fake accepts at most 50 recipients in To, Cc and Bcc together.", ex.Message);
    }

    [Fact]
    public async Task RecipientCap_50_IsSent()
    {
        var rig = new PipelineRig();

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(to: 40, cc: 5, bcc: 5)));
        Assert.Single(rig.Handler.Calls);
    }

    [Theory]
    [InlineData(11, 0, "Cc")]
    [InlineData(0, 11, "Bcc")]
    public async Task ProviderLowerCap_RefusedBeforeSend(int cc, int bcc, string field)
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxCc = 10, MaxBcc = 10 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(cc: cc, bcc: bcc));

        Assert.Equal($"Fake accepts at most 10 recipients in {field}.", ex.Message);
    }

    [Fact]
    public async Task AttachmentCount_RefusedBeforeSend()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxAttachments = 2 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(attachments: [Attachment(1), Attachment(1), Attachment(1)]));

        Assert.Equal("Fake accepts at most 2 attachments.", ex.Message);
    }

    [Fact]
    public async Task NoAttachmentsAllowed_SaysSo()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxAttachments = 0 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(attachments: [Attachment(1)]));

        Assert.Equal("Fake does not support attachments. Remove them or use another provider.", ex.Message);
    }

    [Fact]
    public async Task NoCcOrBccAllowed_SaysSo()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxCc = 0, MaxBcc = 0 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(bcc: 1));

        Assert.Equal("Fake does not support Cc or Bcc. Send to the recipients in To instead.", ex.Message);
    }

    [Fact]
    public async Task PerRecipient_CapCountsToOnly()
    {
        var rig = new PipelineRig(new ProviderCapabilities { Recipients = RecipientPolicy.PerRecipient, MaxCc = 0, MaxBcc = 0 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(to: 51));

        Assert.Equal("Fake accepts at most 50 recipients in To (one request each).", ex.Message);
    }

    [Fact]
    public async Task MaxRequestBytes_RefusedBeforeSend_NamesLimit()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxRequestBytes = 1024 * 1024 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(attachments: [Attachment(800 * 1024)]));

        Assert.Equal("The email is larger than Fake accepts in one request (1 MB after encoding). Remove or shrink the attachments.", ex.Message);
    }

    [Fact]
    public async Task MaxRequestBytes_JustUnder_IsSent()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxRequestBytes = 1024 * 1024 });

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(attachments: [Attachment(700 * 1024)])));
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task AttachmentEachCap_RefusedBeforeSend()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxAttachmentBytesEach = 3 * 1024 * 1024 });

        var ex = await RefusedAsync(rig, PipelineRig.Email(attachments: [Attachment(1), Attachment(3 * 1024 * 1024 + 1)]));

        Assert.Equal("Fake accepts attachments up to 3 MB each.", ex.Message);
    }

    [Fact]
    public async Task AttachmentEachCap_ExactlyAtCap_IsSent()
    {
        var rig = new PipelineRig(new ProviderCapabilities { MaxAttachmentBytesEach = 3 * 1024 * 1024 });

        Assert.Null(await rig.SendAdvancingAsync(PipelineRig.Email(attachments: [Attachment(3 * 1024 * 1024)])));
    }

    [Fact]
    public void Run_IsCallableWithoutThePipeline()
    {
        // ACS calls the pre-checks before its SDK (Task 16).
        var ex = Assert.Throws<EmailSendException>(() => ProviderPreChecks.Run("Acs", PipelineRig.Email(to: 51), new ProviderCapabilities()));

        Assert.Equal("Acs accepts at most 50 recipients in To, Cc and Bcc together.", ex.Message);
    }
}

/// <summary>M6 deferred security minor: a mapper that builds its host from options must never send the credential elsewhere.</summary>
public class AllowedHostTests
{
    [Fact]
    public async Task HostNotInAllowList_RefusedBeforeSend()
    {
        var rig = new PipelineRig(new ProviderCapabilities { AllowedHosts = ["other.example.test"] });

        var ex = Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());

        Assert.Equal("Fake built a request for a host it is not allowed to use. Nothing was sent.", ex.Message);
        Assert.Empty(rig.Handler.Calls);
    }

    [Fact]
    public async Task HostInAllowList_IsSent()
    {
        var rig = new PipelineRig(new ProviderCapabilities { AllowedHosts = ["fake.test"] });

        Assert.Null(await rig.SendAdvancingAsync());
        Assert.Single(rig.Handler.Calls);
    }

    [Fact]
    public async Task PlainHttp_RefusedBeforeSend()
    {
        var rig = new PipelineRig(new ProviderCapabilities { AllowedHosts = ["fake.test"] });
        rig.Mapper.Scheme = "http";

        Assert.IsType<EmailSendException>(await rig.SendAdvancingAsync());
        Assert.Empty(rig.Handler.Calls);
    }

    public static TheoryData<Umbraco.Community.EmailSender.Configuration.EmailProviderKind> PipelineKinds()
        => new(Conformance.Profiles.All.Where(p => p.Kind != Umbraco.Community.EmailSender.Configuration.EmailProviderKind.AzureCommunicationServices).Select(p => p.Kind));

    [Theory]
    [MemberData(nameof(PipelineKinds))]
    public void EveryPipelineMapperDeclaresHosts(Umbraco.Community.EmailSender.Configuration.EmailProviderKind kind)
    {
        using var rig = new Conformance.ConformanceRig(Conformance.Profiles.Get(kind.ToString()), (_, _, _) => throw new InvalidOperationException());
        var mapper = Assert.IsType<PipelineEmailProvider>(rig.Provider).Mapper;

        Assert.NotEmpty(mapper.Capabilities.AllowedHosts ?? []);
    }
}

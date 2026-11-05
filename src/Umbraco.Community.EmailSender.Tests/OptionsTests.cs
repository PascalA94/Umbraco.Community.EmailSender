using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Tests;

public class OptionsTests
{
    // gitleaks:allow
    private const string Sentinel = "pm-SENTINEL";

    private static ValidateOptionsResult Validate(EmailSenderOptions options)
        => new EmailSenderOptionsValidator().Validate(null, options);

    [Fact]
    public void MissingSecret_FailsNamingKey()
    {
        var result = Validate(new EmailSenderOptions { Provider = EmailProviderKind.Postmark });

        Assert.True(result.Failed);
        Assert.Contains("EmailSender:Postmark:ServerToken", result.FailureMessage);
        Assert.DoesNotContain(Sentinel, result.FailureMessage);
    }

    [Fact]
    public void MissingAcsConnectionString_FailsNamingKey()
    {
        var result = Validate(new EmailSenderOptions { Provider = EmailProviderKind.AzureCommunicationServices });

        Assert.Contains("EmailSender:AzureCommunicationServices:ConnectionString", result.FailureMessage);
    }

    [Fact]
    public void SecretPresent_Succeeds()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark };
        options.Postmark.ServerToken = Sentinel;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveTimeout_Fails(int seconds)
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark, TimeoutSeconds = seconds };
        options.Postmark.ServerToken = Sentinel;

        Assert.Contains("EmailSender:TimeoutSeconds", Validate(options).FailureMessage);
    }

    [Theory]
    [InlineData("AzureCommunicationServices", EmailProviderKind.AzureCommunicationServices)]
    [InlineData("postmark", EmailProviderKind.Postmark)]
    [InlineData("POSTMARK", EmailProviderKind.Postmark)]
    public void Provider_IsCaseInsensitive(string value, EmailProviderKind expected)
    {
        Assert.True(EmailProviderKindParser.TryParse(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("Postmark,AzureCommunicationServices")]
    [InlineData("")]
    public void Provider_NumbersAndLists_AreRejected(string value)
        => Assert.False(EmailProviderKindParser.TryParse(value, out _));

    [Theory]
    [InlineData(0L)]
    [InlineData(52428801L)]
    public void AttachmentCap_OutOfRange_FailsNamingKey(long cap)
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark, MaxTotalAttachmentBytes = cap };
        options.Postmark.ServerToken = Sentinel;

        Assert.Contains("EmailSender:MaxTotalAttachmentBytes", Validate(options).FailureMessage);
    }

    [Fact]
    public void BlankMessageStream_FailsNamingKey()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark };
        options.Postmark.ServerToken = Sentinel;
        options.Postmark.MessageStream = " ";

        Assert.Contains("EmailSender:Postmark:MessageStream", Validate(options).FailureMessage);
    }

    [Fact]
    public void UnknownProvider_ListsAllowedValues()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EmailProviderKindParser.Parse("Mailgun"));

        Assert.Contains("AzureCommunicationServices", ex.Message);
        Assert.Contains("Postmark", ex.Message);
        Assert.Contains("EmailSender:Provider", ex.Message);
    }

    [Fact]
    public void Defaults_MatchSpec()
    {
        var options = new EmailSenderOptions();

        Assert.Null(options.Provider);
        Assert.True(options.DisableLinkTracking);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(10485760, options.MaxTotalAttachmentBytes);
        Assert.Equal("outbound", options.Postmark.MessageStream);
    }

    [Fact]
    public void ToString_Redacts()
    {
        var options = new EmailSenderOptions { Provider = EmailProviderKind.Postmark };
        options.Postmark.ServerToken = Sentinel;
        options.AzureCommunicationServices.ConnectionString = "endpoint=https://x.test;accesskey=" + Sentinel;

        var text = options + " " + options.Postmark + " " + options.AzureCommunicationServices;

        Assert.DoesNotContain(Sentinel, text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void Secrets_AreNotSerialised()
    {
        var options = new EmailSenderOptions();
        options.Postmark.ServerToken = Sentinel;
        options.AzureCommunicationServices.ConnectionString = Sentinel;

        Assert.DoesNotContain(Sentinel, System.Text.Json.JsonSerializer.Serialize(options));
    }

    [Fact]
    public void Binds_FromConfigurationSection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailSender:Provider"] = "postmark",
            ["EmailSender:Postmark:ServerToken"] = Sentinel,
            ["EmailSender:TimeoutSeconds"] = "7",
        }).Build();
        var provider = new ServiceCollection()
            .AddOptions<EmailSenderOptions>().BindConfiguration("EmailSender").Services
            .AddSingleton<IConfiguration>(config)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EmailSenderOptions>>().Value;

        Assert.Equal(EmailProviderKind.Postmark, options.Provider);
        Assert.Equal(7, options.TimeoutSeconds);
    }
}

public class OptionsReviewFixTests
{
    private static Configuration.EmailSenderOptions Postmark(int timeout = 30, string token = "pm-ok") => new() // gitleaks:allow
    {
        Provider = Configuration.EmailProviderKind.Postmark,
        TimeoutSeconds = timeout,
        Postmark = { ServerToken = token },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    [InlineData(5_000_000)]
    public void TimeoutSeconds_MustBeBetween1And300(int seconds)
    {
        var result = new Configuration.EmailSenderOptionsValidator().Validate(null, Postmark(seconds));

        Assert.True(result.Failed);
        Assert.Contains("EmailSender:TimeoutSeconds", result.FailureMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void TimeoutSeconds_BoundsAreInclusive(int seconds)
        => Assert.True(new Configuration.EmailSenderOptionsValidator().Validate(null, Postmark(seconds)).Succeeded);

    [Theory]
    [InlineData("pm-SENTINEL\n")]
    [InlineData("pm-SENTINEL\r\nX: 1")]
    [InlineData("pm-SENTINEL-é")]
    public void ServerToken_WithControlOrNonAsciiCharacters_FailsNamingKeyNotValue(string token) // gitleaks:allow
    {
        var result = new Configuration.EmailSenderOptionsValidator().Validate(null, Postmark(token: token));

        Assert.True(result.Failed);
        Assert.Contains("EmailSender:Postmark:ServerToken", result.FailureMessage);
        Assert.DoesNotContain("SENTINEL", result.FailureMessage);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Api.Management.ViewModels.Security;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Infrastructure.Security;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.TestServerTest;
using Umbraco.Extensions;

namespace Umbraco.Community.EmailSender.IntegrationTests;

/// <summary>
/// Base for HTTP tests against the real Management API pipeline.
/// Authentication is real: password login, then the authorization-code-with-PKCE flow, exactly as the
/// backoffice does it. Nothing is faked, so the policies under test are the ones a site runs.
/// </summary>
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public abstract class ApiTestBase : UmbracoTestServerTestBase
{
    public const string ManagementApi = "/umbraco/management/api/v1/";
    public const string TokenEndpoint = "/umbraco/management/api/v1/security/back-office/token";
    private const string CodeVerifier = "pkg-tests-code-verifier-0123456789";

    protected TestUsers Users => new(Services);

    protected static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.That((int)response.StatusCode, Is.LessThan(300), text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Do not install the framework's fake auth handler: the real policies are under test.</summary>
    protected override void CustomTestAuthSetup(IServiceCollection services)
    {
    }

    protected override void CustomMvcSetup(Microsoft.Extensions.DependencyInjection.IMvcBuilder mvcBuilder)
        => mvcBuilder.AddApplicationPart(System.Reflection.Assembly.Load("Umbraco.Community.EmailSender"));

    protected HttpClient CreateAnonymousClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost/", UriKind.Absolute),
    });

    /// <summary>Returns a client authenticated as <paramref name="user"/> through login and PKCE.</summary>
    protected async Task<HttpClient> CreateAuthenticatedClientAsync(IUser user)
    {
        var (client, _) = await AuthenticateAsync(user);
        return client;
    }

    /// <summary>Authenticates and also returns the raw token response (access and refresh token cookies).</summary>
    protected async Task<(HttpClient Client, HttpResponseMessage TokenResponse)> AuthenticateAsync(IUser user)
    {
        await SetPasswordAsync(user);

        HttpClient client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost/", UriKind.Absolute),
        });
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Mirrors BackOfficeApplicationManager's (internal) backoffice descriptor: public client
        // "umbraco-back-office", redirect = host + Umbraco:CMS:Security:AuthorizeCallbackPathName.
        using IServiceScope serviceScope = Services.CreateScope();
        await serviceScope.ServiceProvider.GetRequiredService<IBackOfficeApplicationManager>()
            .EnsureBackOfficeApplicationAsync([client.BaseAddress!]);
        var callbackPath = serviceScope.ServiceProvider.GetRequiredService<IOptions<SecuritySettings>>().Value.AuthorizeCallbackPathName;
        var redirectUri = $"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/{callbackPath.TrimStart('/')}";
        const string clientId = Constants.OAuthClientIds.BackOffice;

        HttpResponseMessage login = await client.PostAsync(
            ManagementApi + "security/back-office/login",
            JsonContent.Create(new LoginRequestModel { Username = user.Username, Password = TestUsers.Password }));
        if (login.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login failed: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");
        }

        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(CodeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        HttpResponseMessage authorize = await client.GetAsync(
            ManagementApi + "security/back-office/authorize" +
            $"?client_id={clientId}&response_type=code&redirect_uri={WebUtility.UrlEncode(redirectUri)}&code_challenge_method=S256&code_challenge={challenge}");
        if (authorize.StatusCode != HttpStatusCode.Found)
        {
            throw new InvalidOperationException($"Authorize failed: {(int)authorize.StatusCode}");
        }

        var code = HttpUtility.ParseQueryString(authorize.Headers.Location!.Query).Get("code");
        HttpResponseMessage token = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = CodeVerifier,
            ["client_id"] = clientId,
            ["code"] = code!,
            ["redirect_uri"] = redirectUri,
        }));
        if (token.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Token failed: {(int)token.StatusCode} {await token.Content.ReadAsStringAsync()}");
        }

        // The backoffice keeps tokens in __Host- cookies and sends a placeholder bearer header; the server
        // swaps the cookie value in. Mirror that so the request shape matches a real browser.
        var accessToken = ReadCookie(token, "__Host-umbAccessToken")
            ?? throw new InvalidOperationException("No access token cookie.");
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", $"__Host-umbAccessToken={accessToken}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "[redacted]");
        return (client, token);
    }

    protected static string? ReadCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            return null;
        }

        var cookies = new CookieContainer();
        foreach (var header in values)
        {
            cookies.SetCookies(response.RequestMessage!.RequestUri!, header);
        }

        return cookies.GetCookies(response.RequestMessage!.RequestUri!).FirstOrDefault(c => c.Name == name)?.Value;
    }

    private async Task SetPasswordAsync(IUser user)
    {
        using IServiceScope scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<ICoreBackOfficeUserManager>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var reset = await userManager.GeneratePasswordResetTokenAsync(user);
        Attempt<PasswordChangedModel, UserOperationStatus> changed = await userService.ChangePasswordAsync(
            user.Key,
            new ChangeUserPasswordModel
            {
                NewPassword = TestUsers.Password,
                ResetPasswordToken = reset.Result!.ToUrlBase64(),
                UserKey = user.Key,
            });
        if (!changed.Success)
        {
            throw new InvalidOperationException($"Could not set password: {changed.Status}");
        }
    }
}

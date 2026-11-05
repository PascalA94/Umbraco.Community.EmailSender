using System.Net;
using System.Text.Json;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Umbraco.Community.EmailSender.IntegrationTests;

/// <summary>the integration harness boots and can authenticate a real backoffice user.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public class HarnessServiceTests : UmbracoIntegrationTest
{
    [Test]
    public void Harness_boots_and_resolves_IUserService()
        => Assert.That(GetRequiredService<IUserService>(), Is.Not.Null);
}

[TestFixture]
public class HarnessTests : ApiTestBase
{
    [Test]
    public async Task Harness_authenticates_backoffice_user_over_http()
    {
        IUser admin = await Users.CreateAdminAsync("Harness Admin");
        HttpClient client = await CreateAuthenticatedClientAsync(admin);

        HttpResponseMessage current = await client.GetAsync(ManagementApi + "user/current");
        Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK), await current.Content.ReadAsStringAsync());
        using JsonDocument body = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("id").GetGuid(), Is.EqualTo(admin.Key));

        HttpResponseMessage anonymous = await CreateAnonymousClient().GetAsync(ManagementApi + "user/current");
        Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}

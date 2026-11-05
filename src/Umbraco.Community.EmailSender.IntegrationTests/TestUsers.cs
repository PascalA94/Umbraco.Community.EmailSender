using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

namespace Umbraco.Community.EmailSender.IntegrationTests;

/// <summary>
/// Creates users for integration tests through core services only. Every user gets a unique email, so tests that
/// share a fixture database never collide.
/// </summary>
public sealed class TestUsers
{
    public const string Password = "Test-Password-1234567890!";

    private readonly IServiceProvider _services;

    public TestUsers(IServiceProvider services) => _services = services;

    private IUserService UserService => _services.GetRequiredService<IUserService>();

    private IUserGroupService UserGroupService => _services.GetRequiredService<IUserGroupService>();

    public async Task<IUser> CreateAsync(string name, string[] groupAliases)
    {
        var groupKeys = new HashSet<Guid>();
        foreach (var alias in groupAliases)
        {
            IUserGroup group = await UserGroupService.GetAsync(alias)
                ?? throw new InvalidOperationException($"No user group '{alias}'.");
            groupKeys.Add(group.Key);
        }

        var email = $"{Guid.NewGuid():N}@example.test";
        Attempt<UserCreationResult, UserOperationStatus> created = await UserService.CreateAsync(
            Constants.Security.SuperUserKey,
            new UserCreateModel { Email = email, UserName = email, Name = name, UserGroupKeys = groupKeys },
            approveUser: true);
        if (!created.Success || created.Result.CreatedUser is null)
        {
            throw new InvalidOperationException($"Could not create test user: {created.Status}");
        }

        IUser user = (await UserService.GetAsync(created.Result.CreatedUser.Key))!;
        user.LastLoginDate = DateTime.UtcNow.AddDays(-10);
        UserService.Save(user);
        return (await UserService.GetAsync(user.Key))!;
    }

    public Task<IUser> CreateAdminAsync(string name = "Admin") => CreateAsync(name, [Constants.Security.AdminGroupAlias]);

    public Task<IUser> CreateEditorAsync(string name = "Editor") => CreateAsync(name, ["editor"]);
}

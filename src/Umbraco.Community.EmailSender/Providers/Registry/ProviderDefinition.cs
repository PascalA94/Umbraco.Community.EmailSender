using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Providers.Registry;

/// <summary>
/// Everything one provider brings: its <c>EmailSender:Provider</c> value, its config section, its start-up validation and its
/// service registration (design 9.2 and 9.13 row 9). <see cref="Validate"/> runs only when this provider is selected and adds
/// failures that name config keys, never values.
/// </summary>
internal sealed record ProviderDefinition(
    EmailProviderKind Kind,
    string Section,
    Action<EmailSenderOptions, List<string>> Validate,
    Action<IServiceCollection> Register);

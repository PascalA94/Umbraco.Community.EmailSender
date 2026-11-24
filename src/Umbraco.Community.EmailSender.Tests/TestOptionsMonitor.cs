using Microsoft.Extensions.Options;
using Umbraco.Community.EmailSender.Configuration;

namespace Umbraco.Community.EmailSender.Tests;

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> whose value a test can replace, raising the change listeners as a reloaded config would.</summary>
internal sealed class TestOptionsMonitor(EmailSenderOptions initial) : IOptionsMonitor<EmailSenderOptions>
{
    private readonly List<Action<EmailSenderOptions, string?>> listeners = [];

    public EmailSenderOptions CurrentValue { get; private set; } = initial;

    public EmailSenderOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<EmailSenderOptions, string?> listener)
    {
        listeners.Add(listener);
        return new Subscription(() => listeners.Remove(listener));
    }

    public void Set(EmailSenderOptions options)
    {
        CurrentValue = options;
        foreach (var listener in listeners.ToArray())
        {
            listener(options, null);
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

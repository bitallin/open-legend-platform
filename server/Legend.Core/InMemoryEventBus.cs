using System.Collections.Concurrent;
using Legend.Contracts;

namespace Legend.Core;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        lock (_lock)
        {
            var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
            list.Add(handler);
        }
    }

    public void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var list))
            {
                list.Remove(handler);
            }
        }
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class
    {
        List<Delegate>? snapshot = null;
        lock (_lock)
        {
            if (_handlers.TryGetValue(typeof(TEvent), out var list) && list.Count > 0)
            {
                snapshot = new List<Delegate>(list);
            }
        }

        if (snapshot == null)
            return;

        foreach (var del in snapshot)
        {
            if (del is Func<TEvent, CancellationToken, Task> typedHandler)
            {
                await typedHandler(domainEvent, ct);
            }
        }
    }
}

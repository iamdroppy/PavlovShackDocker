using Microsoft.AspNetCore.Components;
using Pavlov.Manager.Services;

namespace Pavlov.Manager.Components.UI;

/// <summary>Base for components that show the live server state from <see cref="ServerMonitor"/>.</summary>
public abstract class LiveComponent : ComponentBase, IDisposable
{
    private IDisposable? _subscription;

    [Inject] protected ServerMonitor Monitor { get; set; } = default!;

    protected ServerSnapshot Snapshot => Monitor.Snapshot;

    protected override void OnInitialized() => _subscription = Monitor.Subscribe(OnSnapshotChanged);

    protected virtual void OnSnapshotChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        _subscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}

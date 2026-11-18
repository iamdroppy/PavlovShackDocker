namespace Pavlov.Manager.Services;

public enum ToastKind
{
    Success,
    Error,
    Info,
}

public sealed record Toast(Guid Id, ToastKind Kind, string Title, string? Detail);

/// <summary>Per-circuit toast queue.</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_toasts)
            {
                return [.. _toasts];
            }
        }
    }

    public void Success(string title, string? detail = null) => Show(ToastKind.Success, title, detail);

    public void Error(string title, string? detail = null) => Show(ToastKind.Error, title, detail);

    public void Info(string title, string? detail = null) => Show(ToastKind.Info, title, detail);

    public void Dismiss(Guid id)
    {
        lock (_toasts)
        {
            _toasts.RemoveAll(toast => toast.Id == id);
        }

        Changed?.Invoke();
    }

    private void Show(ToastKind kind, string title, string? detail)
    {
        var toast = new Toast(Guid.NewGuid(), kind, title, detail);
        lock (_toasts)
        {
            _toasts.Add(toast);
            if (_toasts.Count > 4)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id, kind == ToastKind.Error ? TimeSpan.FromSeconds(7) : TimeSpan.FromSeconds(3.5));
    }

    private async Task DismissLaterAsync(Guid id, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(id);
    }
}

public sealed record ConfirmRequest(string Title, string Message, string ConfirmText, bool Danger, TaskCompletionSource<bool> Result);

/// <summary>Per-circuit confirmation dialogs rendered by DialogHost.</summary>
public sealed class DialogService
{
    public event Action? Changed;

    public ConfirmRequest? Current { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", bool danger = false)
    {
        Current?.Result.TrySetResult(false);
        Current = new ConfirmRequest(title, message, confirmText, danger, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        Changed?.Invoke();
        return Current.Result.Task;
    }

    public void Close(bool confirmed)
    {
        var current = Current;
        Current = null;
        current?.Result.TrySetResult(confirmed);
        Changed?.Invoke();
    }
}

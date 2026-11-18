using Microsoft.AspNetCore.Components;
using Pavlov.Rcon;

namespace Pavlov.Manager.Services;

/// <summary>
/// Runs RCON commands for a page: shows a toast with the outcome, refreshes the
/// live state and sends the browser to sign-in when the session is gone.
/// </summary>
public sealed class RconActions(RconSession session, ServerMonitor monitor, ToastService toasts, NavigationManager navigation, ILogger<RconActions> logger)
{
    public bool Busy { get; private set; }

    /// <summary>Runs an action command. Returns true when the server reported success.</summary>
    /// <param name="noReplyMeansSuccess">For commands like ShutdownServer, where the server may drop the connection instead of answering.</param>
    public async Task<bool> RunAsync(string successTitle, Func<PavlovRconClient, Task<RconResponse>> action, string? successDetail = null, bool noReplyMeansSuccess = false)
    {
        Busy = true;
        try
        {
            if (noReplyMeansSuccess)
            {
                try
                {
                    await action(session.Client);
                }
                catch (Exception ex) when (ex is RconTimeoutException or RconConnectionException)
                {
                }

                toasts.Success(successTitle, successDetail);
                monitor.RequestRefresh();
                return true;
            }

            var response = await action(session.Client);
            if (response.Successful)
            {
                toasts.Success(successTitle, successDetail ?? response.Comment);
            }
            else
            {
                toasts.Error("The server refused that", response.Comment ?? $"'{response.CommandLine}' wasn't successful.");
            }

            monitor.RequestRefresh();
            return response.Successful;
        }
        catch (Exception ex) when (Handle(ex))
        {
            return false;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Runs a query. Returns <paramref name="fallback"/> and shows a toast if it fails.</summary>
    public async Task<T> QueryAsync<T>(Func<PavlovRconClient, Task<T>> query, T fallback)
    {
        try
        {
            return await query(session.Client);
        }
        catch (Exception ex) when (Handle(ex))
        {
            return fallback;
        }
    }

    private bool Handle(Exception ex)
    {
        switch (ex)
        {
            case RconSessionExpiredException:
                navigation.NavigateTo("login?expired=1", forceLoad: true);
                return true;
            case RconAuthenticationException:
                _ = session.InvalidateAsync();
                navigation.NavigateTo("login?expired=1", forceLoad: true);
                return true;
            case ArgumentException argument:
                toasts.Error("Check the value", argument.Message.Split(" (Parameter", 2)[0]);
                return true;
            case RconException rcon:
                toasts.Error("RCON error", rcon.Message);
                return true;
            default:
                logger.LogError(ex, "RCON action failed");
                toasts.Error("Something went wrong", ex.Message);
                return true;
        }
    }
}

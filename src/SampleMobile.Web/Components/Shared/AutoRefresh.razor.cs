using Microsoft.AspNetCore.Components;

namespace SampleMobile.Web.Components.Shared;

/// <summary>
/// A switch that raises <see cref="OnTick"/> periodically while on, so pages can show the worker's
/// progress without a manual reload. Owns its timer and cancels it on dispose.
/// </summary>
public partial class AutoRefresh : IAsyncDisposable {
    private readonly string _id = $"auto-refresh-{Guid.NewGuid():N}";
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _enabled;

    [Parameter]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    [Parameter]
    public bool StartEnabled { get; set; } = true;

    [Parameter]
    public EventCallback OnTick { get; set; }

    protected override void OnInitialized() {
        _enabled = StartEnabled;
    }

    protected override void OnAfterRender(bool firstRender) {
        // Only once interactive: during prerendering there is no circuit to push updates through.
        if (firstRender && _enabled) {
            Start();
        }
    }

    private async Task ToggleAsync(ChangeEventArgs args) {
        _enabled = args.Value is true;
        if (_enabled) {
            Start();
        } else {
            await StopAsync();
        }
    }

    private void Start() {
        if (_loop is not null) {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken) {
        using var timer = new PeriodicTimer(Interval);
        try {
            while (await timer.WaitForNextTickAsync(cancellationToken)) {
                await InvokeAsync(() => OnTick.InvokeAsync());
            }
        } catch (OperationCanceledException) {
            // Switched off or disposed.
        } catch (Exception exception) {
            await DispatchExceptionAsync(exception);
        }
    }

    private async Task StopAsync() {
        if (_cancellation is null) {
            return;
        }

        await _cancellation.CancelAsync();
        if (_loop is not null) {
            await _loop;
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;
    }

    public async ValueTask DisposeAsync() {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}

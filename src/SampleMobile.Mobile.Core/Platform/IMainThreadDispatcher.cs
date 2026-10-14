namespace SampleMobile.Mobile.Core.Platform;

/// <summary>Runs UI-bound work on the UI thread. Service events can fire on any thread.</summary>
public interface IMainThreadDispatcher {
    void Dispatch(Action action);
}

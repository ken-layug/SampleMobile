namespace SampleMobile.Mobile.Core.Platform;

/// <summary>Screen changes the core logic asks the UI to perform. Implementations marshal to the UI thread.</summary>
public interface INavigationService {
    /// <summary>Replaces the current UI with a fresh signed-in shell.</summary>
    Task ShowSignedInAsync();

    /// <summary>Replaces the current UI with the login screen, optionally showing <paramref name="message"/>.</summary>
    Task ShowSignedOutAsync(string? message);

    Task OpenNotificationAsync(Guid notificationId);

    Task OpenInboxAsync();
}

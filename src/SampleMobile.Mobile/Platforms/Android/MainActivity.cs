using Android.App;
using Android.Content.PM;

namespace SampleMobile.Mobile;

// SingleTop: a notification tap while the app is open arrives in OnNewIntent instead of stacking a second activity.
// Tap intents are handled by the lifecycle hooks registered in UseAndroidNotifications().
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity {
}

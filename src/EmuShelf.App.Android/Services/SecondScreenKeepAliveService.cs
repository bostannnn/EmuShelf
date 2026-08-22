using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Pins the EmuShelf process while an emulator owns the primary panel. The service is started only for
/// an active game session and stopped as soon as EmuShelf returns, avoiding a permanent notification
/// while the user merely browses the library.
/// </summary>
[Service(
    Name = "com.emushelf.app.SecondScreenKeepAliveService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class SecondScreenKeepAliveService : Service
{
    private const string ChannelId = "emushelf-second-screen";
    private const int NotificationId = 4402;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        StartForeground(NotificationId, BuildNotification());
        global::Android.Util.Log.Info("EmuShelfSecondScreen", "Keep-alive foreground service started.");
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        global::Android.Util.Log.Info("EmuShelfSecondScreen", "Keep-alive foreground service stopped.");
        base.OnDestroy();
    }

    internal static void Start(Context context)
    {
        using var intent = new Intent(context, typeof(SecondScreenKeepAliveService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
    }

    internal static void Stop(Context context)
    {
        using var intent = new Intent(context, typeof(SecondScreenKeepAliveService));
        context.StopService(intent);
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager?.GetNotificationChannel(ChannelId) is not null)
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "Second screen",
            NotificationImportance.Low)
        {
            Description = "Keeps the Thor companion screen visible while a game is running.",
        };
        manager?.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);

        return builder
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle("EmuShelf companion screen")
            .SetContentText("Keeping the second screen available while the game runs")
            .SetOngoing(true)
            .SetCategory(Notification.CategoryService)
            .Build();
    }
}

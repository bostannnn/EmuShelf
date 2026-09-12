using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Tests;

public class StartupViewModelTests
{
    [Fact]
    public async Task CorruptDatabaseShowsRecoveryWithoutReplacingTheLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "emushelf-startup-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        var database = Path.Combine(root, "Data", "library.db");
        var original = System.Text.Encoding.UTF8.GetBytes(new string('x', 4096));
        File.WriteAllBytes(database, original);
        try
        {
            var vm = new StartupViewModel(async () =>
                { await Task.Run(() => new AppBootstrapper(root)); },
                () => { }, () => Task.FromResult<string?>(null), _ => { });
            await vm.StartCommand.ExecuteAsync(null);
            Assert.True(vm.HasError);
            Assert.Equal(original, File.ReadAllBytes(database));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReattachedViewDoesNotRepeatInitialization()
    {
        var calls = 0;
        var vm = new StartupViewModel(() => { calls++; return Task.CompletedTask; },
            () => { }, () => Task.FromResult<string?>(null), _ => { });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FailureIsRecoverableEvenWhenLoggingFails_AndDoesNotExposeExceptionText()
    {
        var retries = 0;
        var picks = 0;
        var vm = new StartupViewModel(() => throw new IOException("secret-in-provider-error"),
            () => retries++, () => { picks++; return Task.FromResult<string?>(null); },
            _ => throw new IOException("Logger unavailable"));
        vm.DispatchGamepadAction(GamepadAction.Confirm);
        Assert.Equal(0, retries);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsLoading);
        Assert.Contains("IOException", vm.Diagnostics);
        Assert.DoesNotContain("secret", vm.Diagnostics + vm.Status);
        vm.DispatchGamepadAction(GamepadAction.Confirm);
        Assert.Equal(1, retries);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.Equal(1, picks);
        Assert.True(vm.HasError); // A canceled picker must leave recovery available.
    }

    [Fact]
    public async Task FolderPickFailureLeavesRetryAvailable()
    {
        var vm = new StartupViewModel(() => throw new IOException(), () => { },
            () => throw new UnauthorizedAccessException("private diagnostic"), _ => { });
        await vm.StartCommand.ExecuteAsync(null);
        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.False(vm.IsChoosingFolder);
        Assert.True(vm.HasError);
        Assert.DoesNotContain("private diagnostic", vm.Status);
    }

    [AvaloniaFact]
    public async Task LoadingAndRecoveryRenderAtSmallHandheldSize()
    {
        var vm = new StartupViewModel(() => throw new IOException(), () => { },
            () => Task.FromResult<string?>(null), _ => { });
        var window = new Window { Width = 833, Height = 468, Content = new StartupView { DataContext = vm } };
        window.Show();
        try
        {
            window.UpdateLayout();
            Save(window, "android-startup.png");
            await vm.StartCommand.ExecuteAsync(null);
            window.UpdateLayout();
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.Equal(3, buttons.Length);
            foreach (var button in buttons)
            {
                Assert.True(button.Bounds.Height >= 48);
                var point = button.TranslatePoint(default, window)!.Value;
                Assert.InRange(point.Y + button.Bounds.Height, 0, 468);
            }
            Save(window, "android-startup-recovery.png");
        }
        finally { window.Close(); }
    }

    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var output = File.Create(Path.Combine(directory, name));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
}

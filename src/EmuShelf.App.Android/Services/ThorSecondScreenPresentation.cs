using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Android.Services;

/// <summary>The native Android-Views companion surface hosted on the Thor's Presentation display.</summary>
internal sealed class ThorSecondScreenPresentation : Presentation
{
    private static readonly Color Background = Color.Rgb(10, 14, 22);
    private static readonly Color Surface = Color.Rgb(24, 31, 44);
    private static readonly Color Muted = Color.Rgb(151, 163, 181);
    private static readonly Color Accent = Color.Rgb(99, 179, 237);

    private readonly SecondScreenController _controller;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly FrameLayout _content;
    private readonly LinearLayout _bottomBar;
    private readonly LinearLayout _dock;
    private readonly ImageView _idleArtwork;
    private readonly TextView _idleTitle;
    private readonly FrameLayout _idleLayer;
    private readonly Action _redim;
    private readonly Dictionary<ImageView, Bitmap> _panelBitmaps = [];
    private Bitmap? _idleBitmap;
    private bool _idle;
    private bool _released;

    public ThorSecondScreenPresentation(
        Context outerContext,
        Display display,
        SecondScreenController controller)
        : base(outerContext, display)
    {
        _controller = controller;
        Window?.SetDimAmount(0);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.TurnScreenOn);

        var root = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetBackgroundColor(Background);

        _content = new FrameLayout(Context);
        root.AddView(_content, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1));

        _bottomBar = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
        };
        _bottomBar.SetGravity(GravityFlags.CenterVertical);
        _bottomBar.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
        _bottomBar.SetBackgroundColor(Surface);
        root.AddView(_bottomBar, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            Dp(112)));

        _bottomBar.AddView(CreateChromeButton("☰", "Toggle all apps", (_, _) => _controller.ToggleDrawer()),
            Weighted(width: 0.7f));

        _dock = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
        };
        _dock.SetGravity(GravityFlags.Center);
        _bottomBar.AddView(_dock, Weighted(width: 5));

        _bottomBar.AddView(CreateChromeButton("★", "Toggle achievements", (_, _) => _controller.ToggleAchievements()),
            Weighted(width: 0.7f));

        _idleLayer = new FrameLayout(Context);
        _idleLayer.SetBackgroundColor(Color.Black);
        _idleArtwork = new ImageView(Context);
        _idleArtwork.SetScaleType(ImageView.ScaleType.CenterInside);
        _idleLayer.AddView(_idleArtwork, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.Center,
            LeftMargin = Dp(160),
            RightMargin = Dp(160),
            TopMargin = Dp(120),
            BottomMargin = Dp(120),
        });
        _idleTitle = Text("", 38, Color.White, GravityFlags.Center);
        _idleTitle.SetTypeface(null, TypefaceStyle.Bold);
        _idleLayer.AddView(_idleTitle, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Center,
            LeftMargin = Dp(80),
            RightMargin = Dp(80),
        });

        _redim = RedimIdle;
        root.Touch += (_, e) =>
        {
            if (!_idle || e.Event?.Action != MotionEventActions.Down)
                return;
            RevealIdleControls();
        };

        SetContentView(root);
        RenderDock(SecondScreenDock.Empty, new Dictionary<string, SecondScreenApp>());
        ShowBrowseHome();
    }

    internal void ShowBrowseHome()
    {
        _idle = false;
        _handler.RemoveCallbacks(_redim);
        _bottomBar.Visibility = ViewStates.Visible;
        ClearContent();
        ClearIdleArtwork();

        var stack = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
        };
        stack.SetGravity(GravityFlags.Center);
        var title = Text("EmuShelf", 48, Color.White, GravityFlags.Center);
        title.SetTypeface(null, TypefaceStyle.Bold);
        stack.AddView(title);
        var subtitle = Text("COMPANION", 15, Muted, GravityFlags.Center);
        subtitle.LetterSpacing = 0.22f;
        stack.AddView(subtitle, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = Dp(10),
        });
        _content.AddView(stack, Match());
    }

    internal void ShowGameIdle(string title)
    {
        _idle = true;
        ClearContent();
        _content.AddView(_idleLayer, Match());

        _idleTitle.Text = title;
        _idleArtwork.Visibility = _idleBitmap is null ? ViewStates.Gone : ViewStates.Visible;
        _idleTitle.Visibility = _idleBitmap is null ? ViewStates.Visible : ViewStates.Gone;
        RevealIdleControls();
    }

    internal void UpdateIdleArtwork(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var previous = _idleBitmap;
        _idleBitmap = bitmap;
        _idleArtwork.SetImageBitmap(bitmap);
        if (_idle)
        {
            _idleArtwork.Visibility = ViewStates.Visible;
            _idleTitle.Visibility = ViewStates.Gone;
        }
        if (previous is not null && !ReferenceEquals(previous, bitmap))
            previous.Dispose();
    }

    internal void RenderDock(SecondScreenDock dock, IReadOnlyDictionary<string, SecondScreenApp> apps)
    {
        _dock.RemoveAllViews();
        for (var slot = 0; slot < SecondScreenDock.SlotCount; slot++)
        {
            var capturedSlot = slot;
            var component = dock[slot];
            apps.TryGetValue(component ?? string.Empty, out var app);

            var button = new ImageButton(Context)
            {
                ContentDescription = app?.Label ?? $"Empty dock slot {slot + 1}",
            };
            button.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
            button.SetBackgroundColor(Color.Transparent);
            if (app?.Icon is { } icon)
                button.SetImageDrawable(icon);
            else
                button.SetImageDrawable(new ColorDrawable(Color.Transparent));

            button.Click += (_, _) => _controller.ActivateDockSlot(capturedSlot);
            button.LongClick += (_, e) =>
            {
                e.Handled = true;
                _controller.EditDockSlot(capturedSlot);
            };
            _dock.AddView(button, Weighted(width: 1));
        }
    }

    internal void ShowDrawer(
        IReadOnlyList<SecondScreenApp> apps,
        int? pickSlot,
        Action<SecondScreenApp> selected,
        Action? clearSlot,
        Action close)
    {
        _idle = false;
        _handler.RemoveCallbacks(_redim);
        _bottomBar.Visibility = ViewStates.Visible;
        ClearContent();

        var vertical = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        var heading = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
        };
        heading.SetGravity(GravityFlags.CenterVertical);
        heading.SetPadding(Dp(28), Dp(18), Dp(28), Dp(12));
        heading.AddView(
            Text(pickSlot is null ? "All apps" : $"Choose app for dock slot {pickSlot + 1}", 24, Color.White),
            Weighted(width: 1));
        if (clearSlot is not null)
            heading.AddView(CreateTextButton("Clear slot", (_, _) => clearSlot()));
        heading.AddView(CreateTextButton("Close", (_, _) => close()));
        vertical.AddView(heading);

        var grid = new GridView(Context)
        {
            NumColumns = 5,
            StretchMode = StretchMode.StretchColumnWidth,
            Adapter = new AppGridAdapter(Context, apps),
        };
        grid.SetPadding(Dp(18), Dp(8), Dp(18), Dp(24));
        grid.SetVerticalSpacing(Dp(4));
        grid.ItemClick += (_, e) => selected(apps[e.Position]);
        vertical.AddView(grid, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1));
        _content.AddView(vertical, Match());
    }

    internal void ShowAchievementsMessage(
        string title,
        string message,
        bool canRefresh = false,
        Action? close = null)
    {
        _idle = false;
        _handler.RemoveCallbacks(_redim);
        _bottomBar.Visibility = ViewStates.Visible;
        ClearContent();

        var stack = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
        };
        stack.SetGravity(GravityFlags.Center);
        stack.SetPadding(Dp(64), Dp(40), Dp(64), Dp(40));
        var heading = Text(title, 30, Color.White, GravityFlags.Center);
        heading.SetTypeface(null, TypefaceStyle.Bold);
        stack.AddView(heading);
        stack.AddView(Text(message, 18, Muted, GravityFlags.Center),
            new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = Dp(18),
            });
        if (canRefresh)
            stack.AddView(CreateTextButton("Refresh", (_, _) => _controller.RefreshAchievements()),
                new LinearLayout.LayoutParams(Dp(180), Dp(56))
                {
                    Gravity = GravityFlags.Center,
                    TopMargin = Dp(24),
                });
        if (close is not null)
            stack.AddView(CreateTextButton("Close", (_, _) => close()),
                new LinearLayout.LayoutParams(Dp(180), Dp(56))
                {
                    Gravity = GravityFlags.Center,
                    TopMargin = Dp(12),
                });
        _content.AddView(stack, Match());
    }

    internal void ShowAchievements(
        string title,
        RetroAchievementsDetailsSnapshot snapshot,
        string? status,
        bool canRefresh,
        Action close,
        long surfaceRevision)
    {
        _idle = false;
        _handler.RemoveCallbacks(_redim);
        _bottomBar.Visibility = ViewStates.Visible;
        ClearContent();

        var details = snapshot.Details;
        var vertical = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        var heading = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
        };
        heading.SetGravity(GravityFlags.CenterVertical);
        heading.SetPadding(Dp(28), Dp(14), Dp(28), Dp(8));
        var headingText = Text(title, 25, Color.White);
        headingText.SetTypeface(null, TypefaceStyle.Bold);
        heading.AddView(headingText, Weighted(width: 1));
        heading.AddView(Text(
            $"{details.UnlockedAchievements}/{details.TotalAchievements}  •  {details.EarnedPoints} pts",
            17,
            Accent,
            GravityFlags.Center));
        if (canRefresh)
            heading.AddView(CreateTextButton("Refresh", (_, _) => _controller.RefreshAchievements()),
                new LinearLayout.LayoutParams(Dp(150), Dp(52)) { LeftMargin = Dp(18) });
        heading.AddView(CreateTextButton("Close", (_, _) => close()),
            new LinearLayout.LayoutParams(Dp(120), Dp(52)) { LeftMargin = Dp(8) });
        vertical.AddView(heading);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusView = Text(status, 14, Muted);
            statusView.SetPadding(Dp(30), 0, Dp(30), Dp(8));
            vertical.AddView(statusView);
        }

        var achievements = details.Achievements
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.AchievementId)
            .ToArray();
        var list = new ListView(Context)
        {
            Adapter = new AchievementListAdapter(this, Context, achievements, _controller, surfaceRevision),
            DividerHeight = 0,
        };
        vertical.AddView(list, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            0,
            1));
        _content.AddView(vertical, Match());
    }

    private void RevealIdleControls()
    {
        _bottomBar.Visibility = ViewStates.Visible;
        _idleArtwork.Alpha = 0.88f;
        _idleTitle.Alpha = 0.88f;
        _handler.RemoveCallbacks(_redim);
        _handler.PostDelayed(_redim, 3_000);
    }

    private void RedimIdle()
    {
        if (!_idle)
            return;
        _bottomBar.Visibility = ViewStates.Gone;
        _idleArtwork.Alpha = 0.3f;
        _idleTitle.Alpha = 0.3f;
    }

    internal void SetPanelBitmap(ImageView image, Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(bitmap);

        if (_released || !image.IsAttachedToWindow)
        {
            bitmap.Dispose();
            return;
        }

        if (_panelBitmaps.Remove(image, out var previous))
            previous.Dispose();
        image.SetImageBitmap(bitmap);
        _panelBitmaps[image] = bitmap;
    }

    internal void ReleaseResources()
    {
        if (_released)
            return;

        _released = true;
        _handler.RemoveCallbacks(_redim);
        ClearContent();
        ClearIdleArtwork();
        _handler.Dispose();
    }

    private void ClearContent()
    {
        _content.RemoveAllViews();
        foreach (var bitmap in _panelBitmaps.Values)
            bitmap.Dispose();
        _panelBitmaps.Clear();
    }

    private void ResetPanelBitmap(ImageView image)
    {
        if (_panelBitmaps.Remove(image, out var bitmap))
            bitmap.Dispose();
        image.SetImageDrawable(new ColorDrawable(Surface));
    }

    private void ClearIdleArtwork()
    {
        _idleArtwork.SetImageDrawable(null);
        _idleBitmap?.Dispose();
        _idleBitmap = null;
    }

    private TextView CreateChromeButton(string text, string description, EventHandler click)
    {
        var button = Text(text, 31, Color.White, GravityFlags.Center);
        button.ContentDescription = description;
        button.SetBackgroundColor(Color.Transparent);
        button.Click += click;
        return button;
    }

    private TextView CreateTextButton(string text, EventHandler click)
    {
        var button = Text(text, 15, Accent, GravityFlags.Center);
        button.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        button.SetBackgroundColor(Surface);
        button.Click += click;
        return button;
    }

    private TextView Text(string text, float size, Color color, GravityFlags gravity = GravityFlags.Left) =>
        new TextView(Context)
        {
            Text = text,
            TextSize = size,
            Gravity = gravity,
        }.Also(view => view.SetTextColor(color));

    private LinearLayout.LayoutParams Weighted(float width) =>
        new(0, ViewGroup.LayoutParams.MatchParent, width);

    private static FrameLayout.LayoutParams Match() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    private int Dp(int value) => (int)Math.Round(value * (Context.Resources?.DisplayMetrics?.Density ?? 1));

    private sealed class AppGridAdapter(Context context, IReadOnlyList<SecondScreenApp> apps)
        : BaseAdapter<SecondScreenApp>
    {
        public override int Count => apps.Count;

        public override SecondScreenApp this[int position] => apps[position];

        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var tile = convertView as AppTile ?? new AppTile(context);
            tile.Bind(apps[position]);
            return tile;
        }
    }

    private sealed class AppTile : LinearLayout
    {
        private readonly ImageView _icon;
        private readonly TextView _label;

        internal AppTile(Context context) : base(context)
        {
            Orientation = Orientation.Vertical;
            SetGravity(GravityFlags.Center);
            SetPadding(Dp(context, 8), Dp(context, 12), Dp(context, 8), Dp(context, 12));
            SetMinimumHeight(Dp(context, 132));

            _icon = new ImageView(context);
            AddView(_icon, new LayoutParams(Dp(context, 64), Dp(context, 64)));

            _label = new TextView(context)
            {
                TextSize = 13,
                Gravity = GravityFlags.Center,
            };
            _label.SetTextColor(Color.White);
            AddView(_label, new LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(context, 42))
            {
                TopMargin = Dp(context, 5),
            });
        }

        internal void Bind(SecondScreenApp app)
        {
            ContentDescription = app.Label;
            _icon.SetImageDrawable(app.Icon);
            _label.Text = app.Label;
        }

        private static int Dp(Context context, int value) =>
            (int)Math.Round(value * (context.Resources?.DisplayMetrics?.Density ?? 1));
    }

    private sealed class AchievementListAdapter(
        ThorSecondScreenPresentation owner,
        Context context,
        IReadOnlyList<RetroAchievementsAchievement> achievements,
        SecondScreenController controller,
        long surfaceRevision) : BaseAdapter<RetroAchievementsAchievement>
    {
        public override int Count => achievements.Count;

        public override RetroAchievementsAchievement this[int position] => achievements[position];

        public override long GetItemId(int position) => achievements[position].AchievementId;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var row = convertView as AchievementTile ?? new AchievementTile(owner, context, controller);
            row.Bind(achievements[position], surfaceRevision);
            return row;
        }
    }

    private sealed class AchievementTile : LinearLayout
    {
        private readonly ThorSecondScreenPresentation _owner;
        private readonly SecondScreenController _controller;
        private readonly ImageView _badge;
        private readonly TextView _title;
        private readonly TextView _description;
        private readonly TextView _points;

        internal AchievementTile(
            ThorSecondScreenPresentation owner,
            Context context,
            SecondScreenController controller) : base(context)
        {
            _owner = owner;
            _controller = controller;
            Orientation = Orientation.Horizontal;
            SetGravity(GravityFlags.CenterVertical);
            SetPadding(owner.Dp(26), owner.Dp(10), owner.Dp(26), owner.Dp(10));

            _badge = new ImageView(context);
            AddView(_badge, new LayoutParams(owner.Dp(64), owner.Dp(64)));

            var copy = new LinearLayout(context) { Orientation = Orientation.Vertical };
            _title = owner.Text("", 17, Color.White);
            _title.SetTypeface(null, TypefaceStyle.Bold);
            copy.AddView(_title);
            _description = owner.Text("", 14, Muted);
            copy.AddView(_description);
            AddView(copy, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1)
            {
                LeftMargin = owner.Dp(15),
                RightMargin = owner.Dp(15),
            });

            _points = owner.Text("", 15, Muted, GravityFlags.Center);
            AddView(_points);
        }

        internal void Bind(RetroAchievementsAchievement achievement, long surfaceRevision)
        {
            Alpha = achievement.IsEarned ? 1f : 0.58f;
            _owner.ResetPanelBitmap(_badge);
            _badge.Tag = new Java.Lang.String(achievement.BadgeName);
            _title.Text = achievement.Title;
            _description.Text = achievement.Description;
            _points.Text = achievement.IsHardcore
                ? $"{achievement.Points}  HC"
                : achievement.IsEarned
                    ? $"{achievement.Points}  ✓"
                    : achievement.Points.ToString();
            _points.SetTextColor(achievement.IsEarned ? Accent : Muted);
            _controller.LoadBadge(_badge, achievement.BadgeName, surfaceRevision);
        }
    }
}

internal static class AndroidViewExtensions
{
    internal static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

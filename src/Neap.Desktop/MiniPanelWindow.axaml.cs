using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Neap.Core;
using Neap.Core.Connection;
using Neap.Core.Diagnostics;
using Neap.Core.Mix;
using Neap.Desktop.Controls;

namespace Neap.Desktop;

/// <summary>
/// The mini panel: the five things changed mid-game, in a small window by the
/// notification area that goes away when it loses focus.
/// </summary>
/// <remarks>
/// <para>
/// The mix, the microphone, noise cancellation, spatial sound and the equaliser preset are
/// the controls Home has, with the same logic behind them; this only lays
/// them out small. <see cref="MiniPanel"/> decides where it sits.
/// </para>
/// <para>
/// It closes on Escape and when another window takes focus, as the system's
/// volume flyout does. Opening the equaliser's presets takes focus to their
/// list without closing the panel; it closes once the list does, if focus
/// went elsewhere meanwhile.
/// </para>
/// <para>
/// It fades up into place as it opens and fades away as it closes, as the
/// system's flyouts do, unless animation is off; see <see cref="Motion"/>.
/// A close waits for the fade, except when the app is stopping.
/// </para>
/// <para>
/// It is drawn as a card like Home's or as frosted glass, as chosen in
/// Settings; see <see cref="Frost"/>.
/// </para>
/// <para>
/// A pretend run with <see cref="Pretend.Mini"/> shows it alone as the app's
/// only window, which stays open so it can be looked at or pictured.
/// </para>
/// </remarks>
public partial class MiniPanelWindow : Window
{
    /// <summary>The gap kept between the panel and the edge of the work area, in device-independent pixels.</summary>
    private const double EdgeGap = 12;

    /// <summary>How far the card moves as it fades in or out, in device-independent pixels.</summary>
    private const double Rise = 12;

    private static readonly TimeSpan Arrive = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(120);

    private readonly MainWindow? _main;
    private readonly BatteryMeter _battery = new("caption");
    private IDisposable? _recheck;
    private bool _painting;
    private bool _leaving;
    private bool _left;

    public MiniPanelWindow()
    {
        InitializeComponent();
        Title = Strings.Get("Main_TrayMini");
        Classes.Set("touch", Pretend.Touch || OnSmallScreen());
        BatteryHolder.Content = _battery;
        if (MiniPanel.Look(AppSettings.Current.MiniPanelLook, HighContrast.Variant(Application.Current!) is not null) == PanelLook.FrostyGlass) Frost();
        if (Pretend.Behind) ShowActivated = false;

        Dial.ValueChanged += (_, args) =>
        {
            if (!_painting) AppServices.Mix.Apply((int)Math.Round(args.NewValue));
        };
        OpenNeap.Click += (_, _) => OpenWindow(null);
        ChooseApps.Click += (_, _) => OpenWindow("home");
        Preset.Flyout!.Closed += (_, _) => CloseIfAway();
        Activated += (_, _) => WindowPresence.Set(this, true);
        Closed += (_, _) => WindowPresence.Set(this, false);
        Deactivated += (_, _) =>
        {
            WindowPresence.Set(this, false);
            Dispatcher.UIThread.Post(CloseIfAway);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            Close();
        };
        SizeChanged += (_, _) => Place();
        ScalingChanged += (_, _) => Place();
    }

    /// <summary>Gets the window it opens when asked; null for a pretend run showing it alone.</summary>
    public MainWindow? Main
    {
        get => _main;
        init => _main = value;
    }

    /// <summary>Whether the screen it opens on is small enough for the roomier touch size.</summary>
    /// <remarks>The screen, as the main window does; see <see cref="Density"/>.</remarks>
    private bool OnSmallScreen() =>
        Screens.Primary is { } screen
        && Density.For(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling) == DensityMode.Touch;

    /// <summary>Gets where the pointer was when the icon was clicked, in physical pixels, or null when that is not known.</summary>
    public PixelPoint? Click { get; init; }

    /// <summary>Gets when it closed, as a <see cref="Stopwatch"/> timestamp, so the icon's next click can tell whether that click was the one that did it.</summary>
    public long ClosedAt { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AppServices.Headset.Changed += Paint;
        AppServices.Headset.StatusChanged += OnStatus;
        AppServices.Mix.Changed += Paint;
        Paint();
        Place();
        if (Motion.Enabled) _ = Fade(0, 1, Rise, 0, Arrive, new CubicEaseOut());

        // Focus begins on the dial, on the way to choose chat apps while the dial waits for
        // them, or else on the first thing that can take it. Given without the focus ring,
        // since the panel is mostly opened with a click; the ring shows from the first Tab.
        InputElement? first = Dial.IsEffectivelyVisible && Dial.IsEffectivelyEnabled ? Dial
            : ChooseApps.IsEffectivelyVisible ? ChooseApps
            : this.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        Dispatcher.UIThread.Post(() => first?.Focus());

        if (Pretend.Snapshot is { } file) _ = SaveAndExit(file);
    }

    /// <summary>Holds the close until the card has faded out, unless animation is off or the app is stopping.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_left || !Motion.Enabled || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown) return;
        e.Cancel = true;
        if (!_leaving) _ = FadeAndClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        AppServices.Headset.Changed -= Paint;
        AppServices.Headset.StatusChanged -= OnStatus;
        AppServices.Mix.Changed -= Paint;
        StopRechecking();
        _behind?.Dispose();
        ClosedAt = Stopwatch.GetTimestamp();
    }

    private void OnStatus(HeadsetStatus status) => Paint();

    private async Task FadeAndClose()
    {
        _leaving = true;
        Card.IsHitTestVisible = false;
        await Fade(1, 0, 0, Rise / 2, Leave, new CubicEaseIn());
        _left = true;
        Close();
    }

    /// <summary>Moves the card's opacity and its offset below its place from one value to another.</summary>
    private Task Fade(double fromOpacity, double toOpacity, double fromOffset, double toOffset, TimeSpan duration, Easing easing)
    {
        Card.RenderTransform ??= new TranslateTransform();
        return new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, fromOpacity), new Setter(TranslateTransform.YProperty, fromOffset) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, toOpacity), new Setter(TranslateTransform.YProperty, toOffset) } },
            },
        }.RunAsync(Card);
    }

    /// <summary>Opens the main window, on a page if one is given, and puts this away.</summary>
    private void OpenWindow(string? page)
    {
        if (_main is null) return;
        _main.Reveal();
        if (page is not null) _main.Open(page);
        Close();
    }

    /// <summary>Closes the panel once focus has gone elsewhere, unless the presets are open, which hold it.</summary>
    private void CloseIfAway()
    {
        if (Pretend.Mini || IsActive || Preset.Flyout!.IsOpen) return;
        Close();
    }

    /// <summary>Puts the panel by the icon, on the screen the icon was clicked on, and keeps it there as it grows or shrinks.</summary>
    private void Place()
    {
        var clicked = Click is { } click ? Screens.ScreenFromPoint(click) : null;
        if ((clicked ?? Screens.Primary) is not { } screen || Bounds.Height <= 0) return;
        double scale = RenderScaling;
        (int, int)? at = clicked is not null && Click is { } c ? (c.X, c.Y) : null;
        var box = MiniPanel.Place(Of(screen.Bounds), Of(screen.WorkingArea),
            (int)Math.Ceiling(Bounds.Width * scale), (int)Math.Ceiling(Bounds.Height * scale), (int)Math.Round(EdgeGap * scale), at);
        // Capped by the work area, never by the height it had: a row that grows after
        // it opens, such as a switch moving under its name, needs the room.
        MaxHeight = (screen.WorkingArea.Height / scale) - (2 * EdgeGap);
        var position = new PixelPoint(box.X, box.Y);
        if (Position != position) Position = position;
        if (_frosted) Ground(screen.WorkingArea, box);

        static Box Of(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void Paint()
    {
        var headset = AppServices.Headset;
        HeadsetName.Text = HeadsetTitle.Current();

        if (headset.Battery is { } battery)
        {
            if (battery.Settling) _battery.Show(null, false, Strings.Get("Home_BatterySettling"));
            else _battery.Show(battery.Percent, battery.Charging, Strings.Format("Home_BatteryLevel", battery.Percent));

            // Nothing says when a settling reading has settled, so look again until it has.
            if (battery.Settling) _recheck ??= Platform.Current.Ui.Every(TimeSpan.FromSeconds(30), Paint);
            else StopRechecking();
        }
        else StopRechecking();
        BatteryHolder.IsVisible = headset.Battery is not null && headset.Status.Link == Link.Connected;

        NoiseRow.IsVisible = HeadsetModels.Shows(headset.Model, Feature.NoiseControl);

        bool chosen = AppServices.Mix.ChatApps.Count > 0;
        SetupArea.IsVisible = !chosen;
        DialArea.IsEnabled = chosen;

        _painting = true;
        try
        {
            int mix = AppServices.Mix.Mix;
            int game = MixLevels.Game(mix), chat = MixLevels.Chat(mix);
            Dial.Value = mix;
            GameLevel.Text = game.ToString(CultureInfo.CurrentCulture);
            ChatLevel.Text = chat.ToString(CultureInfo.CurrentCulture);
            LeanText.Text = MixLevels.Lean(mix) switch
            {
                MixLean.GameOnly => Strings.Get("Mix_GameOnly"),
                MixLean.TowardGame => Strings.Get("Mix_TowardGame"),
                MixLean.Balanced => Strings.Get("Mix_Balanced"),
                MixLean.TowardChat => Strings.Get("Mix_TowardChat"),
                _ => Strings.Get("Mix_ChatOnly"),
            };
            AutomationProperties.SetHelpText(Dial, Strings.Format("Mix_Levels", LeanText.Text, game, chat));
        }
        finally { _painting = false; }
    }

    private void StopRechecking()
    {
        _recheck?.Dispose();
        _recheck = null;
    }

    /// <summary>A pretend run's picture of the panel, once the headset has answered and it has settled.</summary>
    /// <remarks>Drawn on the app's ground rather than on nothing, so the card's rounded corners can be judged.</remarks>
    private async Task SaveAndExit(string file)
    {
        this.Bind(BackgroundProperty, this.GetResourceObservable("NeapGroundBrush"));
        await Task.Delay(Pretend.SettleTime);
        var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var picture = new RenderTargetBitmap(size, new Vector(96, 96));
        picture.Render(this);
        picture.Save(file, new PngBitmapEncoderOptions());
        AppLog.Write($"saved a picture of the mini panel to {file}");
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}

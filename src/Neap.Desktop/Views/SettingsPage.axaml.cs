using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Neap.Core;
using Neap.Core.Audio;
using Neap.Core.Connection;
using Neap.Core.Updates;
using Neap.Desktop.Controls;

namespace Neap.Desktop.Views;

public partial class SettingsPage : UserControl
{
    /// <summary>The guide for someone with a headset Neap does not support yet.</summary>
    private const string MappingGuide = "https://github.com/lykosapps/Neap/blob/main/docs/MAPPING.md";

    /// <summary>How wide Neap's card has to be for its buttons to sit beside its name rather than under it.</summary>
    private const double CardBesideWidth = 680;

    private readonly DispatcherTimer _ticking = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _painting;
    private double _stackedGap;
    private bool _beside;

    public SettingsPage()
    {
        InitializeComponent();

        // Updating, like starting with the system, is left out where there is
        // nothing to do it with: no release for this system yet, or no sign-in entry.
        bool updating = UpdateService.Supported;
        UpdateActions.IsVisible = updating;
        CheckForUpdatesCard.IsVisible = updating;
        StartCard.IsVisible = Startup.Supported;
        if (!TrayAvailability.Exists) StartCard.Description = Strings.Get("Settings_StartMinimised");

        MiniCard.IsVisible = TrayAvailability.Exists;
        TrayIconCard.IsVisible = TrayAvailability.Exists;
        NeapControls.AsGroup(TrayIconOptions);
        SoundDevices.IsVisible = SoundFollowsHeadset.Supported;
        LookCard.IsVisible = TrayAvailability.Exists;
        Advanced.IsVisible = Core.Firmware.FirmwareEdit.Released;

        _painting = true;
        MiniPanelSwitch.IsChecked = AppSettings.Current.MiniPanel;
        FirmwareSwitch.IsChecked = AppSettings.Current.ShowFirmwareEdits;
        PaintTrayIcon();
        SoundFollows.IsChecked = AppServices.SoundFollow.Enabled;
        (AppSettings.Current.MiniPanelLook == PanelLook.FrostyGlass ? LookFrosty : LookStandard).IsChecked = true;
        (AppSettings.Current.Theme switch { ThemeChoice.Light => ThemeLight, ThemeChoice.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        StartWhenSignedIn.IsChecked = Startup.Enabled;
        CheckForUpdates.IsChecked = AppServices.Updates.Automatic;
        _painting = false;

        // The sign-in entry belongs to the real copy of the app.
        StartWhenSignedIn.IsEnabled = !Pretend.Active;
        if (Pretend.Active) StartCard.Description = Strings.Get("Settings_StartPractice");

        StartWhenSignedIn.IsCheckedChanged += (_, _) =>
        {
            if (_painting) return;
            if (Startup.Set(StartWhenSignedIn.IsChecked == true)) return;
            // The change failed: put the switch back rather than leave it claiming something untrue.
            _painting = true;
            StartWhenSignedIn.IsChecked = Startup.Enabled;
            _painting = false;
        };

        MiniPanelSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_painting) return;
            bool on = MiniPanelSwitch.IsChecked == true;
            AppSettings.Update(s => s.MiniPanel = on);
            AppLog.Write(on ? "mini panel: on, a click on the icon opens it" : "mini panel: off, a click on the icon opens the window");
            (TopLevel.GetTopLevel(this) as MainWindow)?.MiniPanelSettingChanged();
        };

        TrayIconLogo.IsCheckedChanged += (_, _) => ChooseTrayIcon(TrayIconLogo, TrayIconStyle.Logo);
        TrayIconBattery.IsCheckedChanged += (_, _) => ChooseTrayIcon(TrayIconBattery, TrayIconStyle.Battery);
        TrayIconPercentage.IsCheckedChanged += (_, _) => ChooseTrayIcon(TrayIconPercentage, TrayIconStyle.Percentage);
        TrayIconHeadsetBattery.IsCheckedChanged += (_, _) => ChooseTrayIcon(TrayIconHeadsetBattery, TrayIconStyle.HeadsetBattery);
        TrayIconHeadsetPercentage.IsCheckedChanged += (_, _) => ChooseTrayIcon(TrayIconHeadsetPercentage, TrayIconStyle.HeadsetPercentage);
        SoundFollows.IsCheckedChanged += (_, _) =>
        {
            if (_painting) return;
            AppServices.SoundFollow.Enabled = SoundFollows.IsChecked == true;
            PaintBackTo();
        };
        SoundBackTo.SelectionChanged += (_, _) =>
        {
            if (!_painting && SoundBackTo.SelectedItem is ComboBoxItem { Tag: string id })
                AppSettings.Update(s => s.SoundBackTo = id);
        };
        MicrophoneBackTo.SelectionChanged += (_, _) =>
        {
            if (!_painting && MicrophoneBackTo.SelectedItem is ComboBoxItem { Tag: string id })
                AppSettings.Update(s => s.MicrophoneBackTo = id);
        };

        LookFrosty.IsCheckedChanged += (_, _) =>
        {
            if (_painting) return;
            var look = LookFrosty.IsChecked == true ? PanelLook.FrostyGlass : PanelLook.Standard;
            AppSettings.Update(s => s.MiniPanelLook = look);
            AppLog.Write($"mini panel look: {look}");
        };

        ThemeSystem.IsCheckedChanged += OnThemeChosen;
        ThemeLight.IsCheckedChanged += OnThemeChosen;
        ThemeDark.IsCheckedChanged += OnThemeChosen;

        FirmwareSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_painting) return;
            bool on = FirmwareSwitch.IsChecked == true;
            AppSettings.Update(s => s.ShowFirmwareEdits = on);
            AppLog.Write(on ? "firmware edits: the Firmware page is in the menu" : "firmware edits: the Firmware page is out of the menu");
            (TopLevel.GetTopLevel(this) as MainWindow)?.FirmwareSettingChanged();
        };

        CheckForUpdates.IsCheckedChanged += (_, _) =>
        {
            if (!_painting) AppServices.Updates.Automatic = CheckForUpdates.IsChecked == true;
        };
        UpdateButton.Click += async (_, _) =>
        {
            var look = UpdateLook.Of(AppServices.Updates.Shown);
            if (look.Busy) return;
            if (look.Action == UpdateAction.Update) await AppServices.Updates.Update();
            else await AppServices.Updates.Check();
        };
        WhatsNew.Click += async (_, _) => await UpdateDialogs.ShowWhatsNew(this);
        DownloadPage.Click += async (_, _) =>
        {
            if (AppServices.Updates.Newer is { } release) await UpdateDialogs.OpenPage(release);
        };

        RecordButton.Click += async (_, _) =>
        {
            if (AppServices.Recorder.Since is null) AppServices.Recorder.Start();
            else await AppServices.Recorder.Stop();
        };
        ReportRecording.Click += async (_, _) =>
        {
            // Someone with another headset sees what Neap found before sending.
            if (AppServices.Recorder.Findings is { } findings
                && !await RecordingDialogs.ConfirmFindings(this, findings)) return;
            await AppServices.Recorder.Report();
        };
        RecordHelp.Click += async (_, _) => await RecordingDialogs.ShowHowTo(this);
        _ticking.Tick += (_, _) => PaintRecording();

        AutomationProperties.SetName(MapHeadset, MapCard.Header);
        MapHeadset.Click += async (_, _) =>
        {
            try { await Platform.Current.Open(new Uri(MappingGuide)); }
            catch (Exception ex) { AppLog.Write($"could not open the mapping guide: {ex.Message}"); }
        };

        AppName.Text = AppInfo.Name;
        if (AppInfo.Version is { } version)
        {
            AppVersion.Text = Strings.Format("Settings_AboutVersion", Release.Display(version));
            AppVersion.IsVisible = true;
        }

        _stackedGap = NeapCard.RowSpacing;
        NeapCard.SizeChanged += (_, e) => OnCardSizeChanged(e.NewSize.Width);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AppServices.Recorder.Changed += PaintRecording;
        PaintRecording();
        AppServices.Updates.Changed += PaintUpdates;
        PaintUpdates();
        if (SoundFollowsHeadset.Supported) PaintBackTo();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        AppServices.Recorder.Changed -= PaintRecording;
        AppServices.Updates.Changed -= PaintUpdates;
        _ticking.Stop();
    }

    /// <summary>Keeps the theme chosen and draws the app in it at once.</summary>
    /// <remarks>Each of the three reports its own change, so only the one now chosen acts.</remarks>
    private void OnThemeChosen(object? sender, RoutedEventArgs e)
    {
        if (_painting || sender is not RadioButton { IsChecked: true } chosen) return;
        var theme = chosen == ThemeLight ? ThemeChoice.Light : chosen == ThemeDark ? ThemeChoice.Dark : ThemeChoice.System;
        AppSettings.Update(s => s.Theme = theme);
        AppLog.Write($"theme: {theme}");
        (Application.Current as App)?.ChooseTheme();
    }

    /// <summary>
    /// Puts the card's buttons beside its name when there is room, and under
    /// it when there isn't.
    /// </summary>
    /// <remarks>Decided from the card's own width, since a desktop window has no width trigger to do it in the markup.</remarks>
    private void OnCardSizeChanged(double width)
    {
        bool beside = width >= CardBesideWidth;
        if (beside == _beside) return;
        _beside = beside;
        Grid.SetRow(UpdateActions, beside ? 0 : 1);
        Grid.SetColumn(UpdateActions, beside ? 1 : 0);
        UpdateActions.VerticalAlignment = beside ? VerticalAlignment.Center : VerticalAlignment.Top;
        NeapCard.RowSpacing = beside ? 0 : _stackedGap;
    }

    /// <summary>Ticks what the icon shows.</summary>
    private void PaintTrayIcon()
    {
        var settings = AppSettings.Current;
        bool painting = _painting;
        _painting = true;
        TrayIconLogo.IsChecked = settings.TrayIcon == TrayIconStyle.Logo;
        TrayIconBattery.IsChecked = settings.TrayIcon == TrayIconStyle.Battery;
        TrayIconPercentage.IsChecked = settings.TrayIcon == TrayIconStyle.Percentage;
        TrayIconHeadsetBattery.IsChecked = settings.TrayIcon == TrayIconStyle.HeadsetBattery;
        TrayIconHeadsetPercentage.IsChecked = settings.TrayIcon == TrayIconStyle.HeadsetPercentage;
        _painting = painting;
    }

    /// <summary>Keeps the person's choice of what the icon shows, and draws the icon again.</summary>
    private void ChooseTrayIcon(RadioButton option, TrayIconStyle style)
    {
        if (_painting || option.IsChecked != true) return;
        AppSettings.Update(s => s.TrayIcon = style);
        AppLog.Write($"notification area: the icon shows {style}");
        PaintTrayIcon();
        (TopLevel.GetTopLevel(this) as MainWindow)?.TrayIconSettingChanged();
    }

    /// <summary>
    /// Shows the recording's state under the card's header only while there
    /// is one to show.
    /// </summary>
    private void PaintRecording()
    {
        var recorder = AppServices.Recorder;
        bool recording = recorder.Since is not null;

        // Left enabled while saving, where a press does nothing: disabling it
        // would throw keyboard focus on to the next card.
        RecordButton.Content = Strings.Get(recording ? "Settings_RecordStop" : "Settings_RecordStart");

        // Once a recording is saved, reporting it is the next step, so that
        // button leads instead of starting another.
        bool reportable = !recording && recorder.Issue is not null;
        ReportRecording.IsVisible = reportable;
        ReportRecording.Classes.Set("accent", reportable);
        RecordButton.Classes.Set("accent", !reportable);

        Record.Description =
            recorder.Saving ? Strings.Get("Settings_RecordSaving")
            : recorder.Since is { } since ? Strings.Format("Settings_RecordRecording", Elapsed(DateTime.Now - since))
            : recorder.Trouble is { } trouble ? Strings.Format("Settings_RecordFailed", trouble)
            : recorder.Saved is { } saved ? Strings.Format("Settings_RecordSaved", Path.GetFileName(saved))
            : null;

        if (recording && !recorder.Saving) _ticking.Start();
        else _ticking.Stop();
    }

    private void PaintUpdates()
    {
        var updates = AppServices.Updates;

        // The buttons follow the offer, so a check that fails doesn't take
        // Update and restart away; the sentence says what the check did.
        var look = UpdateLook.Of(updates.Shown);

        UpdateButton.Content = Strings.Get(look.Action == UpdateAction.Update ? "Settings_UpdateInstall" : "Settings_UpdateCheck");
        UpdateButton.Classes.Set("accent", look.Leads);
        WhatsNew.IsVisible = UpdateDialogs.HasNotes;
        DownloadPage.IsVisible = look.Download;
        DownloadPage.Content = Strings.Get("Settings_UpdateDownload");

        string? state = UpdateService.Supported ? UpdateCopy.Of(updates) : null;
        UpdateStatus.Text = state ?? "";
        UpdateStatus.IsVisible = state is not null;

        // Said when it changes, not as a download's percentage ticks over.
        AutomationProperties.SetLiveSetting(UpdateStatus, AutomationLiveSetting.Polite);
    }

    /// <summary>Fills the lists of devices to go back to, and offers them only while the defaults follow the headset.</summary>
    /// <remarks>
    /// Filled when the page opens rather than kept current: a device plugged
    /// in while the page is open shows the next time it is.
    /// </remarks>
    private void PaintBackTo()
    {
        bool on = AppServices.SoundFollow.Enabled;
        SoundBackCard.IsEnabled = on;
        MicrophoneBackCard.IsEnabled = on;
        _painting = true;
        try
        {
            Fill(SoundBackTo, AudioRoute.Devices(output: true), AppSettings.Current.SoundBackTo);
            Fill(MicrophoneBackTo, AudioRoute.Devices(output: false), AppSettings.Current.MicrophoneBackTo);
        }
        finally { _painting = false; }
    }

    /// <summary>
    /// The devices that are not the headset's, after the device before the
    /// headset's, with the chosen one selected.
    /// </summary>
    /// <remarks>A chosen device that is not plugged in stays chosen, and says so.</remarks>
    private static void Fill(ComboBox list, IReadOnlyList<AudioDevice> devices, string chosen)
    {
        list.Items.Clear();
        list.Items.Add(new ComboBoxItem { Content = Strings.Get("Settings_BackToBefore"), Tag = "" });
        var offered = devices.Where(d => d.Product.Length == 0).ToList();
        foreach (var device in offered)
            list.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
        if (chosen.Length > 0 && offered.All(d => d.Id != chosen))
            list.Items.Add(new ComboBoxItem { Content = Strings.Get("Settings_BackToMissing"), Tag = chosen });
        list.SelectedItem = list.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag! == chosen);
    }

    private static string Elapsed(TimeSpan length) => $"{(int)length.TotalMinutes}:{length.Seconds:00}";
}

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Breakpoint.Core;
using Forms = System.Windows.Forms;

namespace Breakpoint.Desktop;

public partial class MainWindow : Window
{
    private readonly IslandWindow island;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly HashSet<string> warned = [];
    private Status? snapshot;
    private bool polling, exitAllowed;
    private long displayedRevision = -1;

    public MainWindow(bool renderingCheck = false)
    {
        InitializeComponent();
        RenderSchedule(new Schedule()); SelectPanel(SchedulePanel); if (renderingCheck) { island = null!; tray = null!; return; }
        ModeLabel.Text = App.Preview ? "Preview · networking and shutdown are simulated." : "Runs in the background · no taskbar button";
        PreviewWarning.Visibility = App.Preview ? Visibility.Visible : Visibility.Collapsed;
        LightTheme.IsChecked = App.CurrentTheme == "Light";
        island = new IslandWindow(); island.SkipRequested += Skip; island.Show();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Shield, Text = "Breakpoint", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("Show island", null, (_, _) => Dispatcher.Invoke(() => { ShowIsland.IsChecked = true; island.Show(); }));
        menu.Items.Add("Skip this curfew · PIN", null, (_, _) => Dispatcher.Invoke(Skip));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit interface · PIN", null, (_, _) => Dispatcher.Invoke(QuitInterface));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        Closing += OnClosing;
        poll.Tick += async (_, _) => await RefreshAsync(); poll.Start();
        Loaded += async (_, _) => await RefreshAsync();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exitAllowed) { e.Cancel = true; Hide(); }
        else { poll.Stop(); island.Close(); tray.Visible = false; tray.Dispose(); }
    }
    private void OpenSettings() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => Hide();
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void RenderSchedule(Schedule schedule)
    {
        void TimeDisplay(TextBlock block, string value)
        {
            var time = TimeOnly.ParseExact(value, "HH:mm");
            block.Inlines.Clear();
            block.Inlines.Add(new System.Windows.Documents.Run(time.ToString("h:mm", System.Globalization.CultureInfo.InvariantCulture)));
            var suffix = new System.Windows.Documents.Run(time.ToString(" tt", System.Globalization.CultureInfo.InvariantCulture)) { FontSize = 12 };
            suffix.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Dim"); block.Inlines.Add(suffix);
        }
        TimeDisplay(StartDisplay, schedule.Start); TimeDisplay(EndDisplay, schedule.End);
        EnabledLabel.Text = schedule.Enabled ? "● Curfew enabled" : "● Curfew off";
        DaysSummary.Text = schedule.Days.Length == 7 ? "Every day" : string.Join(", ", schedule.Days.Select(d => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[d]));
        Weekdays.Children.Clear();
        foreach (var day in new[] { 1, 2, 3, 4, 5, 6, 0 })
        {
            var tile = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(10,7,10,7), Margin = new Thickness(0,0,6,0), MinWidth = 33, Opacity = schedule.Days.Contains(day) ? 1 : .4 };
            tile.SetResourceReference(Border.BackgroundProperty, "Raised");
            tile.Child = new TextBlock { Text = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[day][..1], FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(tile, ((DayOfWeek)day) + (schedule.Days.Contains(day) ? " selected" : " not selected"));
            Weekdays.Children.Add(tile);
        }
    }
    private void SelectPanel(StackPanel panel)
    {
        SchedulePanel.Visibility = panel == SchedulePanel ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = panel == TimerPanel ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = panel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;
        foreach (var pair in new[] { (ScheduleNav, SchedulePanel), (TimerNav, TimerPanel), (SettingsNav, SettingsPanel) })
        {
            if (pair.Item2 == panel) pair.Item1.SetResourceReference(Button.BackgroundProperty, "Raised");
            else pair.Item1.Background = System.Windows.Media.Brushes.Transparent;
            pair.Item1.SetResourceReference(Button.ForegroundProperty, pair.Item2 == panel ? "Ink" : "Dim");
        }
    }
    private void ScheduleTab(object sender, RoutedEventArgs e) => SelectPanel(SchedulePanel);
    private void TimerTab(object sender, RoutedEventArgs e) => SelectPanel(TimerPanel);
    private void SettingsTab(object sender, RoutedEventArgs e) => SelectPanel(SettingsPanel);
    private void IslandVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (island is null) return;
        if (ShowIsland.IsChecked == true) island.Show(); else island.Hide();
    }
    private void ThemeChanged(object sender, RoutedEventArgs e)
    {
        if (LightTheme is null) return;
        App.ApplyTheme(LightTheme.IsChecked == true ? "Light" : "Dark");
        Directory.CreateDirectory(Path.GetDirectoryName(App.PreferencePath)!);
        File.WriteAllText(App.PreferencePath, App.CurrentTheme);
    }
    private async Task RefreshAsync()
    {
        if (polling) return;
        polling = true;
        try
        {
            var response = await App.Client.SendAsync(new Request());
            if (response.Status is { } status) ApplyStatus(status);
        }
        catch (Exception)
        {
            StatusLabel.Text = "Service unavailable — enforcement status is unknown.";
            ErrorLabel.Text = App.Preview ? "Preview worker is not connected. Reopen the app if this persists." : "Install/start the background service using the included installer. The interface cannot enforce curfew alone.";
            ErrorLabel.Visibility = Visibility.Visible; island.Unavailable(); snapshot = null;
        }
        finally { polling = false; }
    }
    private void ApplyStatus(Status status)
    {
        snapshot = status; island.Update(status);
        StatusLabel.Text = $"{status.Phase} · {status.Network}";
        ErrorLabel.Text = status.Error ?? ""; ErrorLabel.Visibility = status.Error is null ? Visibility.Collapsed : Visibility.Visible;

        SetupButton.IsEnabled = !status.Configured;
        if (displayedRevision != status.Schedule.Revision)
        {
            RenderSchedule(status.Schedule);
            displayedRevision = status.Schedule.Revision;
        }
        TimerStatus.Text = status.TimerStart is { } timer ? $"Timer curfew starts {timer.ToLocalTime():ddd, h:mm tt}" : "No timer running.";
        if (!status.Configured) SelectPanel(SettingsPanel);
        NotifyWarnings(status);
    }
    private void NotifyWarnings(Status status)
    {
        string? id = null, message = null;
        if (status.Active is { } active && status.Shutdown == "Countdown")
        { id = active.Id + ":shutdown"; message = "Networking is restricted. Save your work: graceful shutdown begins in one minute. Skipping requires your owner PIN."; }
        else if (status.Next is { } next)
        {
            var seconds = (next.Start - status.Now).TotalSeconds;
            var threshold = seconds <= 60 ? 60 : seconds <= 300 ? 300 : seconds <= 900 ? 900 : 0;
            if (threshold > 0) { id = next.Id + ":" + threshold; message = $"Curfew begins in approximately {threshold / 60} minute(s). Save your work before networking is restricted."; }
        }
        if (id is null || !warned.Add(id)) return;
        tray.ShowBalloonTip(10000, "Breakpoint curfew", (status.Preview ? "PREVIEW: " : "") + message, Forms.ToolTipIcon.Warning);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(StatusLabel)
            ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(StatusLabel);
        peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        if (WarningSound.IsChecked == true) System.Media.SystemSounds.Exclamation.Play();
    }
    private Response? Authorize(string title, string description, Request request, bool newPin = false, bool recovery = false)
    {
        if (snapshot is null) { System.Windows.MessageBox.Show(this, "Reconnect to the service before changing settings.", "Breakpoint"); return null; }
        var dialog = new OwnerDialog(this, title, description, request, newPin, recovery);
        if (dialog.ShowDialog() != true) return null;
        var result = dialog.Result!;
        if (result.Status is { } status) ApplyStatus(status);
        if (result.RecoveryCode is { } code) OwnerDialog.ShowRecovery(this, code);
        return result;
    }
    private void SaveSchedule(object sender, RoutedEventArgs e)
    {
        if (snapshot is null) return;
        var editor = new ScheduleEditorDialog(snapshot.Schedule) { Owner = this };
        if (editor.ShowDialog() != true || editor.Edited is null) return;
        var schedule = editor.Edited;
        Authorize("Save Start – End schedule", $"Start {schedule.Start}: block all non-loopback networking, including LAN, then warn for 60 seconds before graceful shutdown. End {schedule.End}: remove Breakpoint restrictions. This affects every user of this PC.", new Request { Action = "SaveSchedule", Revision = snapshot.Schedule.Revision, Schedule = schedule });
    }
    private void StartTimer(object sender, RoutedEventArgs e)
    {
        if (snapshot is null) return;
        if (!int.TryParse(TimerMinutes.Text, out var minutes) || minutes is < 1 or > 1440) { System.Windows.MessageBox.Show(this, "Enter 1–1,440 minutes.", "Check timer"); return; }
        Authorize("Start timer", $"Curfew begins in {minutes} minutes. Graceful shutdown follows after a 60-second warning. An earlier recurring curfew still wins. Manual restriction ends eight hours after its Start unless you skip it.", new Request { Action = "StartTimer", Minutes = minutes, Revision = snapshot.Schedule.Revision });
    }
    private void CancelTimer(object sender, RoutedEventArgs e)
    {
        if (snapshot is null) return;
        Authorize("Cancel timer", "Your recurring schedule stays enabled.", new Request { Action = "CancelTimer", Revision = snapshot.Schedule.Revision });
    }
    private void Skip()
    {
        OpenSettings();
        if (snapshot is null) return;
        var occurrence = snapshot.Active ?? snapshot.Next;
        if (occurrence is null) return;
        Authorize("Skip this curfew?", "Shutdown will be cancelled before its deadline, and Breakpoint network restrictions removed. Future recurring schedules stay enabled.", new Request { Action = "Skip", OccurrenceId = occurrence.Id });
    }
    private void TestWarning(object sender, RoutedEventArgs e) => Authorize("Test shutdown warning", "Preview only: no real networking changes or shutdown will occur.", new Request { Action = "PreviewWarning" });
    private void SetupOwner(object sender, RoutedEventArgs e)
    {
        if (!App.Preview && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            var executable = Environment.ProcessPath!;
            try { Process.Start(new ProcessStartInfo(executable, "--setup") { UseShellExecute = true, Verb = "runas" }); exitAllowed = true; Close(); App.Current.Shutdown(); }
            catch (Win32Exception) { ErrorLabel.Text = "Administrator approval was cancelled."; }
            return;
        }
        Authorize("Set up owner PIN", "Choose a private PIN with 6–12 digits. Keep the recovery code somewhere your brother cannot access. Curfew stays off until you enable a schedule.", new Request { Action = "Setup" }, true);
    }
    private void ChangePin(object sender, RoutedEventArgs e) => Authorize("Change owner PIN", "Enter your current PIN and choose a replacement. You will receive a new recovery code.", new Request { Action = "ChangePin" }, true);
    private void RecoverPin(object sender, RoutedEventArgs e) => Authorize("Recover owner access", "Use your privately stored recovery code to choose a new PIN. The previous recovery code becomes invalid.", new Request { Action = "RecoverPin" }, true, true);
    private void QuitInterface()
    {
        OpenSettings();
        if (Authorize("Quit Breakpoint interface", "The live background service will keep enforcing curfew. Closing this interface does not disable your schedule.", new Request { Action = "AuthorizeQuit" }) is not null)
        { exitAllowed = true; Close(); App.Current.Shutdown(); }
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(1000, "Light", false)]
    [InlineData(1000, "Dark", false)]
    [InlineData(1440, "Light", false)]
    [InlineData(1440, "Dark", false)]
    [InlineData(1000, "Light", true)]
    [InlineData(1000, "Dark", true)]
    [InlineData(1440, "Light", true)]
    [InlineData(1440, "Dark", true)]
    public async Task ProfileFileScanExplainsPurposeStaysResponsiveAndReportsResults(int width, string theme, bool config)
    {
        using var fixture = new ManagedProfileFixture(theme);
        var window = fixture.Window;
        window.Width = width; window.Height = width == 1000 ? 640 : 850;
        OpenProfileManagement(window);
        if (config) { Click(Find<Button>(window, "ProfilesBackToConfig")); Dispatcher.UIThread.RunJobs(); }
        var origin = Find<Button>(window, config ? "ProfileRescan" : "ProfileManagementRescan");
        Assert.Equal("Scan profile files", origin.Content);
        Assert.Contains("matching settings and favorites files", Find<TextBlock>(window,
            config ? "ProfileScanPurpose" : "ProfileManagementScanPurpose").Text);
        Assert.Contains(fixture.DirectoryPath, ToolTip.GetTip(origin)!.ToString());
        Capture(window, $"profile-scan-ready-{width}-{theme}-{config}");

        // Hold only discovery; exercise the production catalog commit and UI path.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int scans = 0;
        window.ProfileFileScanner = async () => { scans++; await release.Task; return await fixture.Store.ScanProfileFilesAsync(); };
        File.Copy(Path.Combine(fixture.DirectoryPath, "Session-settings.json"), Path.Combine(fixture.DirectoryPath, "External-settings.json"));
        File.Copy(Path.Combine(fixture.DirectoryPath, "Session-favorites.json"), Path.Combine(fixture.DirectoryPath, "External-favorites.json"));
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "Incomplete-settings.json"), "{}");
        origin.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        var scanning = window.ProfileScanTask!;
        Assert.NotNull(scanning); Assert.False(scanning.IsCompleted);
        Assert.Equal("Scanning…", origin.Content); Assert.False(origin.IsEnabled);
        Assert.False(Field<RadioButton>(window, "_darkThemeRadio").IsEnabled);
        Assert.All(Field<List<Button>>(window, "_profileScanButtons"), button => Assert.False(button.IsEnabled));
        Assert.Contains("Scanning saved profile files", Find<TextBlock>(window, "ProfileNoticeTitle").Text);
        Assert.Contains(fixture.DirectoryPath, Find<TextBlock>(window, "ProfileNoticeDetail").Text);
        Assert.False(Find<ProgressBar>(window, "ProfileNoticeProgress").IsVisible);
        Assert.False(Find<Button>(window, "ProfileNoticeClose").IsVisible);
        Assert.DoesNotContain("External", fixture.Settings.Profiles);
        if (!config)
        {
            SelectManagedProfile(window, "Session");
            Assert.False(Find<Button>(window, "ProfileUse").IsEnabled);
            Assert.False(Find<Button>(window, "ProfileRename").IsEnabled);
            SelectManagedProfile(window, "Default");
        }
        bool processedUiWork = false;
        Dispatcher.UIThread.Post(() => processedUiWork = true); Dispatcher.UIThread.RunJobs();
        Assert.True(processedUiWork); Assert.True(Find<Button>(window, "NavConfig").IsEnabled);
        await Task.Delay(1100); Dispatcher.UIThread.RunJobs();
        Assert.True(Find<ProgressBar>(window, "ProfileNoticeProgress").IsVisible);
        Assert.Equal("Scanning saved profile files", AutomationProperties.GetName(Find<ProgressBar>(window, "ProfileNoticeProgress")));
        Capture(window, $"profile-scan-running-{width}-{theme}-{config}");

        release.SetResult(); await scanning.WaitAsync(TimeSpan.FromSeconds(5)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, scans); Assert.Contains("External", fixture.Settings.Profiles);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Contains("4 saved profiles found; 1 new profile; 1 incomplete", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
        Assert.Equal("Profile scan complete", Find<TextBlock>(window, "ProfileNoticeTitle").Text);
        Assert.False(Find<ProgressBar>(window, "ProfileNoticeProgress").IsVisible);
        Assert.True(origin.IsEnabled); Assert.Equal("Scan profile files", origin.Content);
        Assert.True(Field<RadioButton>(window, "_darkThemeRadio").IsEnabled);
        Assert.True(Find<Button>(window, "ProfileNoticeClose").IsVisible);
        Capture(window, $"profile-scan-complete-{width}-{theme}-{config}");
        Find<Button>(window, "ProfileNoticeClose").Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.False(Find<Border>(window, "ProfileNotice").IsVisible); Assert.True(origin.IsFocused);
        window.ProfileFileScanner = null; Click(origin);
        await window.ProfileScanTask!.WaitAsync(TimeSpan.FromSeconds(5)); Dispatcher.UIThread.RunJobs();
        Assert.Contains("0 new profiles", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public async Task FailedProfileFileScanKeepsCatalogAndCanBeRetried()
    {
        using var fixture = new ManagedProfileFixture();
        OpenProfileManagement(fixture.Window);
        fixture.Window.ProfileFileScanner = () => Task.FromException<(List<string>, int)>(new IOException("The profile folder could not be read."));
        var scan = Find<Button>(fixture.Window, "ProfileManagementRescan"); scan.Focus(); Click(scan);
        await fixture.Window.ProfileScanTask!; Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Default", "Session", "Spare"], fixture.Store.ListProfiles());
        Assert.Contains("could not be read", Find<TextBlock>(fixture.Window, "ProfileNoticeDetail").Text);
        Assert.True(scan.IsEnabled); Assert.Equal("Scan profile files", scan.Content);
        Assert.False(Find<ProgressBar>(fixture.Window, "ProfileNoticeProgress").IsVisible);
        Assert.Equal("Try again", Find<Button>(fixture.Window, "ProfileNoticeAction").Content);
        fixture.Window.ProfileFileScanner = null;
        Click(Find<Button>(fixture.Window, "ProfileNoticeAction"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Find<TextBlock>(fixture.Window, "ProfileNoticeTitle").Text != "Profile scan complete") { await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs(); Assert.Equal(0, fixture.Midi.TotalSendCount);
    }
}

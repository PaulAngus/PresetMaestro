using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    private static Task ImportProfile(MainWindow window) =>
        (Task)typeof(MainWindow).GetMethod("TransferProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false])!;

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1200, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1200, "Dark")]
    public async Task ImportPromptPrefillsSourceOffersRenameAndNoticeShowsHowToUseFavorites(int width, string theme)
    {
        using var fixture = new ManagedProfileFixture(theme, realDialogs: true);
        var window = fixture.Window;
        window.Width = width; window.Height = 640;
        fixture.Store.Export("Session", fixture.ArchivePath);
        OpenProfileManagement(window);
        var origin = Find<Button>(window, "ProfileImport"); origin.Focus();
        Task importing = ImportProfile(window); Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(window.OwnedWindows);
        var input = Find<TextBox>(dialog, "ProfileName");
        Assert.Equal("Session", input.Text);
        Assert.Same(input, dialog.FocusManager!.GetFocusedElement());
        Assert.False(Find<Button>(dialog, "ProfileNameSave").IsEnabled);
        Assert.True(Find<Button>(dialog, "ProfileImportOverwrite").IsVisible);
        Assert.Contains("already exists", Find<TextBlock>(dialog, "ProfileImportConflict").Text);
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.False(importing.IsCompleted);
        Capture(dialog, $"profile-import-conflict-{width}-{theme}");
        input.Text = "Rehearsal"; Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Button>(dialog, "ProfileNameSave").IsEnabled);
        Assert.False(Find<Button>(dialog, "ProfileImportOverwrite").IsVisible);
        Capture(dialog, $"profile-import-new-name-{width}-{theme}");
        Click(Find<Button>(dialog, "ProfileNameSave")); await importing; Dispatcher.UIThread.RunJobs();
        Assert.Same(origin, window.FocusManager!.GetFocusedElement());
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Rehearsal", Find<TextBlock>(window, "ManagedProfile").Text);
        Assert.Contains("1 favorite imported", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
        Capture(window, $"profile-import-result-{width}-{theme}");
        Click(Find<Button>(window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Border>(window, "ProfileNotice").IsEffectivelyVisible);
        Capture(window, $"profile-import-favorites-notice-{width}-{theme}");
        Click(Find<Button>(window, "ProfileNoticeAction")); Dispatcher.UIThread.RunJobs();
        var confirmation = Assert.Single(window.OwnedWindows);
        Click(Find<Button>(confirmation, "ProfileConfirm")); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Rehearsal", fixture.Settings.ActiveProfile);
        Assert.Equal("Session favorite", Field<List<Favorite>>(window, "_favorites").First().Name);
        Assert.Contains("favorite is ready", Find<TextBlock>(window, "ProfileNoticeDetail").Text);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaTheory]
    [InlineData("Escape")]
    [InlineData("Cancel")]
    [InlineData("Close")]
    public async Task CancellingImportPromptPreservesFilesAndFocus(string dismissal)
    {
        using var fixture = new ManagedProfileFixture(realDialogs: true);
        fixture.Store.Export("Session", fixture.ArchivePath);
        OpenProfileManagement(fixture.Window);
        var files = Directory.GetFiles(fixture.DirectoryPath, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        var origin = Find<Button>(fixture.Window, "ProfileImport"); origin.Focus();
        Task importing = ImportProfile(fixture.Window); Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        if (dismissal == "Escape") { dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); }
        else if (dismissal == "Cancel") { Click(Find<Button>(dialog, "ProfileNameCancel")); }
        else { dialog.Close(); }
        await importing; Dispatcher.UIThread.RunJobs();
        foreach (var (path, bytes) in files) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        Assert.Equal(["Default", "Session", "Spare"], fixture.Store.ListProfiles());
        Assert.Same(origin, fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitOverwriteReloadsActiveFavoritesOrKeepsInactiveImportSelected(bool active)
    {
        using var fixture = new ManagedProfileFixture(realDialogs: true);
        fixture.Store.Export("Session", fixture.ArchivePath);
        OpenProfileManagement(fixture.Window);
        Task importing = ImportProfile(fixture.Window); Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(fixture.Window.OwnedWindows);
        Find<TextBox>(dialog, "ProfileName").Text = active ? "default" : "Spare";
        Click(Find<Button>(dialog, "ProfileImportOverwrite")); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        if (active)
        {
            dialog = Assert.Single(fixture.Window.OwnedWindows);
            Assert.Equal("Device not connected", dialog.Title);
            Click(Find<Button>(dialog, "ProfileConfirm"));
        }
        await importing; Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Default", "Session", "Spare"], fixture.Store.ListProfiles());
        Assert.Equal("Session favorite", fixture.Store.LoadFavorites(active ? "Default" : "Spare").First().Name);
        if (active)
        {
            Assert.Equal("Session favorite", Field<List<Favorite>>(fixture.Window, "_favorites").First().Name);
            Assert.Equal(6, fixture.Settings.MidiChannel);
            Assert.Equal("Default", fixture.Settings.ActiveProfile);
            Assert.Contains("ready in Favorites", Find<TextBlock>(fixture.Window, "ProfileNoticeDetail").Text);
            Click(Find<Button>(fixture.Window, "ProfileManagementRescan")); Dispatcher.UIThread.RunJobs();
            await fixture.Window.ProfileScanTask!.WaitAsync(TimeSpan.FromSeconds(5)); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Session favorite", fixture.Store.LoadFavorites("Default").First().Name);
        }
        else { Assert.Equal("Clean", Field<List<Favorite>>(fixture.Window, "_favorites").Single().Name); }
        Assert.Single(Directory.GetDirectories(Path.Combine(fixture.DirectoryPath, "OverwrittenProfiles")));
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1200, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1200, "Dark")]
    public async Task ProfileChecksStayVisibleAcrossPagesAndCancelWithoutChangingFavorites(int width, string theme)
    {
        using var fixture = new ProfileValidationFixture(theme: theme);
        fixture.Window.Width = width; fixture.Window.Height = 640;
        fixture.Connect();
        var pending = new TaskCompletionSource<PresetScenes>();
        fixture.SceneQuery = (_, token) => pending.Task.WaitAsync(token);
        var files = Directory.GetFiles(fixture.DirectoryPath, "*.json").ToDictionary(path => path, File.ReadAllBytes);
        Task switching = fixture.Switch(); Dispatcher.UIThread.RunJobs();
        Click(Find<Button>(fixture.Window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
        var notice = Find<Border>(fixture.Window, "ProfileNotice");
        Assert.True(notice.IsEffectivelyVisible);
        Assert.Contains("before switching favorites", Find<TextBlock>(fixture.Window, "ProfileNoticeDetail").Text);
        Assert.Equal("Cancel switch", Find<Button>(fixture.Window, "ProfileNoticeAction").Content);
        Assert.True(Find<ProgressBar>(fixture.Window, "ProfileNoticeProgress").IsVisible);
        Capture(fixture.Window, $"profile-check-progress-{width}-{theme}");
        Click(Find<Button>(fixture.Window, "ProfileNoticeAction")); await switching; Dispatcher.UIThread.RunJobs();
        Assert.Empty(fixture.Dialogs);
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Equal("Clean", Field<List<Favorite>>(fixture.Window, "_favorites").Single().Name);
        foreach (var (path, bytes) in files) { Assert.Equal(bytes, File.ReadAllBytes(path)); }
        Assert.False(Find<ProgressBar>(fixture.Window, "ProfileNoticeProgress").IsVisible);
        Capture(fixture.Window, $"profile-check-cancelled-{width}-{theme}");
        Assert.Equal(0, fixture.Midi.TotalSendCount);
    }

    [AvaloniaFact]
    public async Task ProfileNoticeKeyboardActionDoesNotSendPresetAndEscapeRestoresVisibleFocus()
    {
        using var fixture = new ManagedProfileFixture();
        fixture.Store.Export("Session", fixture.ArchivePath);
        fixture.NameResult = "Gig";
        OpenProfileManagement(fixture.Window);
        Find<Button>(fixture.Window, "ProfileImport").Focus();
        await ImportProfile(fixture.Window);
        Click(Find<Button>(fixture.Window, "NavFavorites")); Dispatcher.UIThread.RunJobs();
        SetField(fixture.Window, "_enteredDigits", "123");
        Find<Button>(fixture.Window, "ProfileNoticeAction").Focus();
        fixture.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Gig", fixture.Settings.ActiveProfile);
        Assert.Equal(0, fixture.Midi.TotalSendCount);
        Assert.Same(Find<Button>(fixture.Window, "ProfileNoticeClose"), fixture.Window.FocusManager!.GetFocusedElement());
        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.False(Find<Border>(fixture.Window, "ProfileNotice").IsVisible);
        Assert.Same(Find<Button>(fixture.Window, "NavConfig"), fixture.Window.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task ProfileCheckNoticeAndCancellationSurviveThemeChange()
    {
        using var fixture = new ProfileValidationFixture();
        fixture.Connect();
        var pending = new TaskCompletionSource<PresetScenes>();
        fixture.SceneQuery = (_, token) => pending.Task.WaitAsync(token);
        Task switching = fixture.Switch(); Dispatcher.UIThread.RunJobs();
        Field<RadioButton>(fixture.Window, "_darkThemeRadio").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(Find<Border>(fixture.Window, "ProfileNotice").IsEffectivelyVisible);
        Assert.True(Find<ProgressBar>(fixture.Window, "ProfileNoticeProgress").IsVisible);
        Click(Find<Button>(fixture.Window, "ProfileNoticeAction")); await switching; Dispatcher.UIThread.RunJobs();
        Assert.Equal("Default", fixture.Settings.ActiveProfile);
        Assert.Empty(fixture.Dialogs);
        Assert.Contains("cancelled", Find<TextBlock>(fixture.Window, "ProfileNoticeTitle").Text);
    }
}

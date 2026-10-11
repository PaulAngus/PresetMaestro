using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;

namespace PresetMaestro.Tests;

public sealed class SearchEntryInteractionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PresetMaestro-search-entry-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) { Directory.Delete(_directory, true); } }

    [AvaloniaTheory]
    [InlineData(1000, "Light", 0)]
    [InlineData(1440, "Light", 0)]
    [InlineData(1000, "Dark", 0)]
    [InlineData(1440, "Dark", 0)]
    [InlineData(1000, "Light", 1)]
    [InlineData(1440, "Light", 1)]
    [InlineData(1000, "Dark", 1)]
    [InlineData(1440, "Dark", 1)]
    public void OutsideClicksExitIndexSearchWithoutChangingQueryOrSwallowingTheClick(int width, string theme, int view)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            LastAttempt = new IndexScan
            {
                Status = "Cancelled",
                Firmware = "12.00",
                Presets = Enumerable.Range(0, 3)
                    .ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i) with { Name = $"Lead preset {i}", SceneNames = ["Lead", "Clean", "", "", "", "", "", ""] }),
            },
        });
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, [], midi, profileStore: store) { Width = width, Height = width == 1000 ? 640 : 850 };
        window.Show();
        try
        {
            Find<Button>("NavPresetIndex").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<TabStrip>("IndexViewTabs").SelectedIndex = view; Dispatcher.UIThread.RunJobs();
            var input = Find<TextBox>("IndexSearchInput");
            var results = Find<ListBox>(view == 0 ? "IndexPresetList" : "IndexSceneList");
            input.Focus(); input.Text = "Lead"; Dispatcher.UIThread.RunJobs();
            var originalItems = results.Items.ToArray();
            var popup = (Popup)typeof(MainWindow).GetField("_indexSearchPopup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.True(popup.IsOpen);
            ClickAt(Find<TextBlock>("AppTitle"));
            Assert.False(input.IsKeyboardFocusWithin);
            Assert.False(popup.IsOpen);
            window.KeyTextInput("unexpected"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lead", input.Text);
            Assert.Equal(originalItems, results.Items.ToArray());
            SaveFrame("outside");

            ClickAt(Find<Border>("IndexSearchEntry"));
            Assert.True(input.IsKeyboardFocusWithin);
            input.Text = "Lea"; Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            // Tab leaves editing and closes suggestions without clearing the filter.
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.False(input.IsKeyboardFocusWithin);
            Assert.False(popup.IsOpen);
            Assert.Equal("Lea", input.Text);

            input.Focus(); input.Text = "Lead"; Dispatcher.UIThread.RunJobs();
            // Escape hides suggestions; the next outside click must still release focus.
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            originalItems = results.Items.ToArray();
            var row = (ListBoxItem)results.ContainerFromIndex(1)!;
            var clickedItem = results.Items[1];
            ClickAt(row);
            Assert.False(input.IsKeyboardFocusWithin);
            Assert.Equal("Lead", input.Text);
            Assert.Same(clickedItem, results.SelectedItem);
            Assert.Equal(originalItems, results.Items.ToArray());
            SaveFrame("row");

            input.Focus(); Dispatcher.UIThread.RunJobs();
            ClickAt(Find<Button>("IndexClearSearch"));
            Assert.False(input.IsKeyboardFocusWithin);
            Assert.Empty(input.Text!);
            Assert.Equal(view == 0 ? 3 : 24, results.ItemCount);
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void ClickAt(Control control)
        {
            window.CaptureRenderedFrame()?.Dispose();
            var position = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
        void SaveFrame(string state)
        {
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, $"index-search-{view}-{width}-{theme.ToLowerInvariant()}-{state}.png"));
        }
    }

    [AvaloniaTheory]
    [InlineData(1000, "Light")]
    [InlineData(1440, "Light")]
    [InlineData(1000, "Dark")]
    [InlineData(1440, "Dark")]
    public void WholeSearchEntryAcceptsTypingWithoutInterceptingTextOrChipRemoval(int width, string theme)
    {
        var store = new ProfileStore(_directory);
        var settings = store.LoadSettings(); settings.Theme = theme;
        var device = new IndexDevice(Guid.NewGuid(), "Stage rig", FractalDeviceVariant.FM9, "12.00");
        new IndexLibrary(Path.Combine(_directory, "FractalIndex")).Save(new DeviceIndex
        {
            Device = device,
            LastAttempt = new IndexScan { Status = "Cancelled", Firmware = "12.00", Presets = Enumerable.Range(0, 3).ToDictionary(i => i, i => FractalIndexWorkflowTests.Preset(i)) },
        });
        settings.FractalIndex = IndexJson.ToElement(new IndexProfile { Devices = [device], SelectedDeviceId = device.Id });
        var favorites = Enumerable.Range(0, 3).Select(i => new Favorite { Id = i + 1, Slot = i + 1, Name = FractalIndexWorkflowTests.Preset(i).Name, Tags = ["Crunch"] }).ToList();
        var midi = new DeviceMidi();
        var window = new MainWindow(settings, favorites, midi, profileStore: store) { Width = width, Height = 850 };
        window.Show();
        try
        {
            foreach (var prefix in new[] { "Index", "Favorite" })
            {
                Find<Button>(prefix == "Index" ? "NavPresetIndex" : "NavFavorites").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var entry = Find<Border>(prefix + "SearchEntry");
                var input = Find<TextBox>(prefix + "SearchInput");
                var clear = Find<Button>(prefix == "Index" ? "IndexClearSearch" : "FavoriteClearSearch");
                Assert.Equal("Add tag or search term", AutomationProperties.GetName(input));
                foreach (var point in new[] { new Point(entry.Bounds.Width - 12, entry.Bounds.Height / 2), new Point(3, 3) })
                {
                    clear.Focus();
                    ClickAt(entry, point);
                    Assert.Same(input, window.FocusManager!.GetFocusedElement());
                    window.KeyTextInput("Bassman"); Dispatcher.UIThread.RunJobs();
                    Assert.Equal("Bassman", input.Text);
                    Assert.Single(Results(prefix).Items);
                    input.Text = ""; Dispatcher.UIThread.RunJobs();
                }

                // Direct clicks still place the caret and support selection inside the actual input.
                input.Text = "Bassman"; Dispatcher.UIThread.RunJobs();
                if (prefix == "Index") { window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); }
                ClickAt(input, new Point(8, input.Bounds.Height / 2));
                Assert.True(input.CaretIndex < input.Text.Length);
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); Dispatcher.UIThread.RunJobs();
                window.KeyTextInput("Bassman"); Dispatcher.UIThread.RunJobs();
                Assert.Equal("Bassman", input.Text);
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
                Assert.Empty(input.Text!);

                var label = entry.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Text: Bassman");
                clear.Focus(); ClickAt(label, new Point(label.Bounds.Width / 2, label.Bounds.Height / 2));
                Assert.Same(input, window.FocusManager!.GetFocusedElement());
                clear.Focus(); ClickAt(entry, new Point(entry.Bounds.Width - 12, entry.Bounds.Height / 2));
                window.KeyTextInput("Tweed"); Dispatcher.UIThread.RunJobs();
                Assert.Equal("Tweed", input.Text);
                Assert.Single(Results(prefix).Items);
                input.Text = ""; Dispatcher.UIThread.RunJobs();
                SaveFrame($"{prefix.ToLowerInvariant()}-search-{width}-{theme.ToLowerInvariant()}");
                var remove = entry.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "×"));
                ClickAt(remove, new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2));
                Assert.DoesNotContain(entry.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Text: Bassman");
                Assert.Equal(3, Results(prefix).ItemCount);
            }
            Find<Button>("NavAmps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            var amps = Find<TextBox>("AmpsSearch");
            var allAmps = Find<ListBox>("AmpsDirectory").ItemCount;
            foreach (var point in new[] { new Point(amps.Bounds.Width - 12, amps.Bounds.Height / 2), new Point(3, 3) })
            {
                Find<Button>("AmpsClearSearch").Focus(); ClickAt(amps, point);
                Assert.Same(amps, window.FocusManager!.GetFocusedElement());
                window.KeyTextInput("Fender"); Dispatcher.UIThread.RunJobs();
                Assert.Equal("Fender", amps.Text);
                Assert.InRange(Find<ListBox>("AmpsDirectory").ItemCount, 1, allAmps - 1);
                amps.Text = ""; Dispatcher.UIThread.RunJobs();
            }
            SaveFrame($"amps-search-{width}-{theme.ToLowerInvariant()}");
            var picker = new PresetSelectionWindow(new Dictionary<int, string> { [0] = "Bassman", [1] = "Brit Lead" }, 0) { Width = 600, Height = 640 };
            picker.Show(window); Dispatcher.UIThread.RunJobs();
            try
            {
                var search = picker.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "PresetSearch");
                var list = picker.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "PresetList");
                foreach (var point in new[] { new Point(search.Bounds.Width - 12, search.Bounds.Height / 2), new Point(3, 3) })
                {
                    list.Focus(); picker.CaptureRenderedFrame()?.Dispose();
                    var position = search.TranslatePoint(point, picker)!.Value;
                    picker.MouseDown(position, MouseButton.Left); picker.MouseUp(position, MouseButton.Left); Dispatcher.UIThread.RunJobs();
                    Assert.Same(search, picker.FocusManager!.GetFocusedElement());
                    picker.KeyTextInput("Bassman"); Dispatcher.UIThread.RunJobs();
                    Assert.Equal("Bassman", search.Text); Assert.Single(list.Items.OfType<ListBoxItem>(), i => i.Tag is PresetChoice);
                    search.Text = ""; Dispatcher.UIThread.RunJobs();
                }
            }
            finally { picker.Close(); }
            Assert.Empty(midi.Sent);
        }
        finally { window.Close(); }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        ListBox Results(string prefix) => prefix == "Index" ? Find<ListBox>("IndexPresetList") : (ListBox)typeof(MainWindow).GetField("_favListBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        void ClickAt(Control control, Point point)
        {
            window.CaptureRenderedFrame()?.Dispose();
            var position = control.TranslatePoint(point, window)!.Value;
            window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
        void SaveFrame(string name)
        {
            string? output = Environment.GetEnvironmentVariable("PRESET_MAESTRO_SCREENSHOT_DIR");
            if (output is null) { return; }
            Directory.CreateDirectory(output);
            using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(output, name + ".png"));
        }
    }
}


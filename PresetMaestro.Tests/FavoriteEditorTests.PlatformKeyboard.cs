using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using PresetMaestro.Core;

namespace PresetMaestro.Tests;

public partial class FavoriteEditorTests
{
    [AvaloniaTheory]
    [InlineData(KeyModifiers.Meta)]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Shift)]
    public void ModifiedShortcutsCannotEnterOrSendPresetCommands(KeyModifiers modifiers)
    {
        var midi = new FakeMidi { InputOpen = true, OutputOpen = true };
        var window = CreateWindow(midi, settings: new AppSettings { KeyboardEntryEnabled = true });
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.Focus();
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D1, KeyModifiers = modifiers });
            Assert.Empty(Field<string>(window, "_enteredDigits"));
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.D1 });
            Assert.Equal("1", Field<string>(window, "_enteredDigits"));
            foreach (var key in new[] { Key.Enter, Key.Delete, Key.Back, Key.Left, Key.Right })
            {
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            }
            Assert.Equal("1", Field<string>(window, "_enteredDigits"));
            Assert.Equal(0, midi.TotalSendCount);
        }
        finally { window.Close(); }
    }
}

#if FRACTAL_INDEX
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace PresetMaestro;

public partial class MainWindow
{
    private async Task RenameManagedLibraryAsync()
    {
        if (LibraryManagementBusy || _creatingLibrary || _managedLibraryId is not Guid id) { return; }
        var origin = _libraryRename!;
        int channelDraft = _libraryChannel!.SelectedIndex;
        int offsetDraft = _libraryOffset!.SelectedIndex;
        decimal? sceneDraft = _librarySceneCc!.Value;
        _changingProfile = true;
        UpdateIndexButtons();
        try
        {
            var device = _indexLibrary.Load(id)?.Device ?? throw new InvalidOperationException("This device is no longer available.");
            string? renamed = null;
            var input = new TextBox { Name = "LibraryName", Text = device.Name, MaxLength = 80, Height = 32, MinHeight = 32, Padding = new Thickness(8, 4) };
            AutomationProperties.SetName(input, "Device name");
            var error = new TextBlock { Name = "LibraryNameError", FontSize = 12, Foreground = DangerBrush, TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            var rename = ProfileCommand("LibraryNameSave", "Rename"); rename.Height = rename.MinHeight = 32;
            var cancel = ProfileCommand("LibraryNameCancel", "Cancel"); cancel.Height = cancel.MinHeight = 32;
            var dialog = new Window
            {
                Name = "LibraryNameDialog",
                Title = "Rename device",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                Background = SurfaceBrush,
                RequestedThemeVariant = RequestedThemeVariant,
                Icon = Icon,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Device name", FontSize = 12, Foreground = SecondaryBrush }, input, error,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { rename, cancel } },
                    },
                },
            };
            void Submit()
            {
                try
                {
                    // Reload by identity and save only the name; mapping drafts stay separate.
                    var current = _indexLibrary.Load(id)?.Device ?? throw new InvalidOperationException("This device is no longer available.");
                    renamed = _indexLibrary.SaveDevice(current with { Name = input.Text ?? "" }).Device.Name;
                    dialog.Close();
                }
                catch (Exception ex) { error.Text = ex.Message; error.IsVisible = true; }
            }
            rename.Click += (_, _) => Submit();
            cancel.Click += (_, _) => dialog.Close();
            dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
            dialog.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
                else if (e.Key == Key.Enter) { Submit(); e.Handled = true; }
            };
            await dialog.ShowDialog(this);
            if (renamed is not null)
            {
                RefreshIndexContext();
                _libraryChannel.SelectedIndex = channelDraft;
                _libraryOffset.SelectedIndex = offsetDraft;
                _librarySceneCc.Value = sceneDraft;
                SetIndexMessage($"Renamed '{device.Name}' to '{renamed}'.");
            }
        }
        catch (Exception ex) { SetIndexMessage("Could not rename device: " + ex.Message); }
        finally
        {
            _changingProfile = false;
            UpdateIndexButtons();
            if (origin.IsEffectivelyVisible) { origin.Focus(); }
        }
    }
}
#endif

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private sealed record ProfileImportChoice(string Name, bool Overwrite, Guid? LibraryId = null, bool KeepImportedLibrary = false);
    partial void AddProfileImportLibraryChoice(StackPanel content, ProfileStore.ImportData data, Action<Guid?, bool> choose);

    private async Task<ProfileImportChoice?> PromptProfileImportAsync(ProfileStore.ImportData data)
    {
        string sourceName = data.SourceName;
        Guid? libraryId = null;
        bool keepImportedLibrary = false;
        var libraryContent = new StackPanel { Spacing = 8 };
        AddProfileImportLibraryChoice(libraryContent, data, (id, keep) => { libraryId = id; keepImportedLibrary = keep; });
        if (_profileNamePromptOverride is not null)
        {
            string? name = await _profileNamePromptOverride("import", sourceName);
            if (name is null) { return null; }
            name = ProfileStore.ValidateName(string.IsNullOrWhiteSpace(name) ? sourceName : name);
            bool exists = _profileStore!.ContainsName(name);
            if (exists && !await ConfirmProfileAsync("Overwrite profile?",
                $"Replace '{name}' with the imported favorites, mapping and cached names? A backup of the existing profile will be kept.", "Overwrite")) { return null; }
            return new(name, exists, libraryId, keepImportedLibrary);
        }

        var origin = FocusManager?.GetFocusedElement();
        var result = new TaskCompletionSource<ProfileImportChoice?>();
        var input = new TextBox { Name = "ProfileName", Text = sourceName, Height = 32, MinHeight = 32, Padding = new Thickness(8, 4) };
        AutomationProperties.SetName(input, "Imported profile name");
        var conflict = new TextBlock { Name = "ProfileImportConflict", FontSize = 13, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Name = "ProfileNameError", FontSize = 12, Foreground = DangerBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var import = ProfileCommand("ProfileNameSave", "Import"); import.Height = import.MinHeight = 32;
        var overwrite = ProfileCommand("ProfileImportOverwrite", "Overwrite"); overwrite.Height = overwrite.MinHeight = 32;
        var cancel = ProfileCommand("ProfileNameCancel", "Cancel"); cancel.Height = cancel.MinHeight = 32;
        var dialog = new Window
        {
            Name = "ProfileImportDialog",
            Title = "Import profile",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Background = SurfaceBrush,
            RequestedThemeVariant = RequestedThemeVariant,
            Icon = Icon,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Choose a name for the imported profile", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Profile name", FontSize = 13, Foreground = SecondaryBrush }, input } },
                    conflict, libraryContent, error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { import, overwrite, cancel } },
                },
            },
        };
        void UpdateConflict()
        {
            bool exists = false;
            try { exists = _profileStore!.ContainsName(ProfileStore.ValidateName(input.Text ?? "")); }
            catch (ArgumentException) { }
            import.IsEnabled = !exists;
            overwrite.IsVisible = exists;
            conflict.Text = exists
                ? $"'{input.Text}' already exists. Enter a different name, cancel, or overwrite its favorites, mapping and cached names. A backup will be kept."
                : "Import creates a separate profile. Choose Use profile afterwards to show its favorites.";
            error.IsVisible = false;
        }
        void Submit(bool replace)
        {
            try
            {
                string name = ProfileStore.ValidateName(input.Text ?? "");
                if (_profileStore!.ContainsName(name) && !replace) { UpdateConflict(); return; }
                result.TrySetResult(new(name, replace, libraryId, keepImportedLibrary)); dialog.Close();
            }
            catch (Exception ex) { error.Text = ex.Message; error.IsVisible = true; input.Focus(); }
        }
        input.TextChanged += (_, _) => UpdateConflict();
        import.Click += (_, _) => Submit(false);
        overwrite.Click += (_, _) => Submit(true);
        cancel.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => result.TrySetResult(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && ReferenceEquals(dialog.FocusManager?.GetFocusedElement(), input))
            {
                if (import.IsEnabled) { Submit(false); }
                e.Handled = true;
            }
        };
        UpdateConflict();
        try { await dialog.ShowDialog(this); return await result.Task; }
        finally { origin?.Focus(); }
    }
}

#if FRACTAL_INDEX
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using PresetMaestro.Core;
using PresetMaestro.FractalIndex;
using PresetMaestro.Midi;

namespace PresetMaestro;

public partial class MainWindow
{
    private sealed record ImportLibraryOption(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }

    partial void AddProfileImportLibraryChoice(StackPanel content, ProfileStore.ImportData data, Action<Guid?, bool> choose)
    {
        var profile = IndexJson.ReadProfile(data.Settings.FractalIndex);
        if (profile.AssignedDevice is not { } imported) { return; }
        var devices = AvailableIndexDevices().Where(d => d.Variant == imported.Variant).ToArray();
        var current = _detectedDevice?.Model == imported.Variant.ToDeviceModel()
            ? devices.FirstOrDefault(d => d.Id == _indexProfile.SelectedDeviceId) : null;
        var known = devices.FirstOrDefault(d => d.Id == imported.Id);
        var recommended = current ?? known;
        bool hasSnapshot = profile.PortableSnapshots.Any(s => s.Device.Id == imported.Id);
        content.Children.Add(new TextBlock
        {
            Text = "Device",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"The export includes “{imported.Name}” ({imported.Variant.ToDeviceModel()}). Its presets, scenes and amps are saved on this PC; several profiles can use the same device.",
            FontSize = 13,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        var options = devices.OrderByDescending(d => d.Id == recommended?.Id)
            .Select(d => new ImportLibraryOption(d.Id, $"Use {LibraryDisplayName(d)}" + (d.Id == recommended?.Id ? " (recommended)" : "")))
            .Append(new(null, hasSnapshot ? "Keep a separate imported device" : "Choose a device later")).ToArray();
        var selection = new ComboBox
        {
            Name = "ProfileImportLibrary",
            ItemsSource = options,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        selection.ItemTemplate = new FuncDataTemplate<ImportLibraryOption>((option, _) =>
        {
            var label = new TextBlock { Text = option?.Label ?? "", TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(label, option?.Label);
            AutomationProperties.SetName(label, option?.Label ?? "");
            return label;
        });
        AutomationProperties.SetName(selection, "Device for the imported profile");
        var detail = new TextBlock { Name = "ProfileImportLibraryDetail", FontSize = 13, Foreground = TextBrush, TextWrapping = TextWrapping.Wrap };
        void Update()
        {
            if (selection.SelectedItem is not ImportLibraryOption option) { return; }
            choose(option.Id, option.Id is null);
            var selected = devices.FirstOrDefault(d => d.Id == option.Id);
            detail.Text = selected is null
                ? hasSnapshot
                    ? "Creates a separate saved copy from the export. If its name is already used, the new name includes the imported profile name."
                    : "No saved presets were included in the export. Import the profile, then choose a saved device in Config."
                : $"Shares “{selected.Name}” and its MIDI mapping. Its current saved data is kept; the export does not replace it."
                    + (selected.Id == current?.Id ? " This is the device selected for the current profile." : "");
        }
        selection.SelectionChanged += (_, _) => Update();
        selection.SelectedItem = options.First(o => o.Id == recommended?.Id);
        Update();
        content.Children.Add(selection);
        content.Children.Add(detail);
    }

    partial void DescribeProfileImportLibrary(ProfileSettings settings, ref string description)
    {
        var profile = IndexJson.ReadProfile(settings.FractalIndex);
        if (profile.AssignedDevice is not { } device) { return; }
        bool separate = profile.PortableSnapshots.Any(s => s.Device.Id == device.Id);
        description = separate ? $" Separate device: “{device.Name}”."
            : _indexLibrary.LoadSummary(device.Id) is not null ? $" Shared device: “{device.Name}”."
            : " No saved device data was included. Choose a device in Config.";
    }
}
#endif

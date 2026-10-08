using PresetMaestro.Core;
using PresetNameSync.Core;

namespace PresetMaestro;

public partial class MainWindow
{
    private CancellationTokenSource? _profileValidationCts;
    private bool _profileValidationCancelled;

    private bool ProfileDeviceConnected => _detectedDevice is not null && _connectionCts is null && _midi.InputOpen && _midi.OutputOpen;

    private Task<bool> ConfirmUncheckedProfileAsync(string name) => ConfirmProfileAsync("Device not connected",
        $"The device isn't connected, so profile '{name}' cannot be checked against it.\n\nSelect OK to continue switching to this profile anyway, or Cancel to keep the current profile.", "OK");

    private async Task<bool> ConfirmProfileDeviceMatchAsync(string name, ProfileSettings profile)
    {
        _profileValidationCancelled = false;
        if (!ProfileDeviceConnected) { return await ConfirmUncheckedProfileAsync(name); }

        var device = _detectedDevice!;
        long connectionGeneration = _connectionGeneration;
        bool interrupted = false;
        var warnings = new List<string>();
        if (profile.DeviceModel != device.Model)
        {
            warnings.Add($"Device type: profile uses {profile.DeviceModel}; connected device is {device.ModelLabel}.");
        }
        if (profile.DeviceName is null || device.DeviceName is null)
        {
            warnings.Add("Device name: cannot fully check because the device name was not recorded in the profile or the connected device name is unavailable.");
        }
        else if (!string.Equals(profile.DeviceName, device.DeviceName, StringComparison.Ordinal))
        {
            warnings.Add($"Device name: profile is '{DisplayDeviceName(profile.DeviceName)}'; connected device is '{DisplayDeviceName(device.DeviceName)}'.");
        }

        if (device.Model != DeviceModel.FM9)
        {
            warnings.Add("Preset names and scene names: cannot be checked on this device. Name reads are currently supported for FM9 only.");
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            _profileValidationCts = cancellation;
            _sceneCts?.Cancel();
            _scenePollCts?.Cancel();
            _favoriteSceneCts?.Cancel();
            UpdatePresetSyncButtons();
            try
            {
                // Port keys identify where names were cached, not which names the new device has.
                // Check every saved cache, reading each stored preset only once.
                var scenes = profile.SceneNameCaches.Values.SelectMany(cache => cache)
                    .GroupBy(entry => entry.Key).OrderBy(group => group.Key).ToArray();
                var stored = new Dictionary<int, PresetScenes>();
                int sceneMismatches = 0;
                int sceneCount = scenes.Sum(group => group.Sum(entry => entry.Value.Names.Length));
                if (sceneCount == 0) { warnings.Add("Scene names: no names are recorded in this profile to check."); }
                try
                {
                    foreach (var group in scenes)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        ShowProfileCheckProgress(name, $"Checking scene names for preset {group.Key + profile.DisplayOffset:000}…");
                        var actual = await _queryStoredScenesAsync(group.Key, cancellation.Token);
                        if (actual.Slot != group.Key) { throw new InvalidDataException("The device returned a different preset."); }
                        stored[group.Key] = actual;
                        foreach (var entry in group)
                        {
                            for (int i = 0; i < entry.Value.Names.Length; i++)
                            {
                                if (i >= actual.Names.Length || !string.Equals(entry.Value.Names[i], actual.Names[i], StringComparison.Ordinal))
                                {
                                    sceneMismatches++;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Scene names: could not fully check the saved names ({ex.Message}).");
                }
                if (sceneMismatches > 0) { warnings.Add($"Scene names: {sceneMismatches} saved names do not match the connected device."); }

                int presetMismatches = 0;
                if (profile.PresetNameCache.Count == 0) { warnings.Add("Preset names: no names are recorded in this profile to check."); }
                try
                {
                    foreach (var entry in profile.PresetNameCache.OrderBy(entry => entry.Key))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        ShowProfileCheckProgress(name, $"Checking preset name {entry.Key + profile.DisplayOffset:000}…");
                        string actualName;
                        if (stored.TryGetValue(entry.Key, out var actual)) { actualName = actual.PresetName; }
                        else
                        {
                            var result = await _queryPresetNameAsync(entry.Key, TimeSpan.FromSeconds(1.5), cancellation.Token);
                            if (result.Slot != entry.Key) { throw new InvalidDataException("The device returned a different preset."); }
                            actualName = result.PresetName;
                        }
                        if (!string.Equals(entry.Value, actualName, StringComparison.Ordinal)) { presetMismatches++; }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Preset names: could not fully check the saved names ({ex.Message}).");
                }
                if (presetMismatches > 0) { warnings.Add($"Preset names: {presetMismatches} saved names do not match the connected device."); }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { interrupted = true; }
            finally
            {
                _profileValidationCts = null;
                _profileNoticeProgress.IsVisible = false;
                _profileNoticeAction.IsVisible = false;
                _profileNoticeClose.IsVisible = true;
            }
        }

        if (_profileValidationCancelled) { return false; }
        if (_sceneClosing) { return false; }
        if (interrupted || !ProfileDeviceConnected || connectionGeneration != _connectionGeneration)
        {
            return await ConfirmUncheckedProfileAsync(name);
        }
        if (warnings.Count == 0) { return true; }
        _profileStatus.Text = "Profile check complete; waiting for confirmation.";
        ShowProfileNotice("Profile check needs your decision", $"Review the device differences before switching to '{name}'.");
        bool confirmed = await ConfirmProfileAsync("Profile does not fully match",
            $"Profile '{name}' does not fully match the connected device:\n\n" + string.Join("\n", warnings.Select(warning => "• " + warning)) +
            "\n\nSelect OK to continue switching to this profile anyway, or Cancel to keep the current profile.", "OK");
        if (confirmed && (!ProfileDeviceConnected || connectionGeneration != _connectionGeneration))
        {
            return await ConfirmUncheckedProfileAsync(name);
        }
        return confirmed;
    }

    private static string DisplayDeviceName(string name) => string.IsNullOrWhiteSpace(name) ? "Unnamed device" : name;
}

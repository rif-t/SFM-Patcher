using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System.Net.Http.Headers;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ModernSfmPatcher.App;

public sealed partial class MainWindow : Window
{
    private sealed record ShaderPackageUi(InstallerService.ShaderPackage Package, CheckBox Selection, IReadOnlyList<CheckBox> Effects, StackPanel EffectList, Button ExpandButton);
    private readonly InstallerService _installer = new();
    private bool _working;
    private IReadOnlyList<InstallerService.Release> _dxvk = [];
    private IReadOnlyList<InstallerService.ReShadeRelease> _reshade = [];
    private IReadOnlyList<InstallerService.Release> _rest = [];
    private readonly List<ShaderPackageUi> _shaderPackages = [];
    private bool _updatingShaderSelection;
    private static readonly HttpClient VersionHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    public MainWindow()
    {
        InitializeComponent();
        AppVersionText.Text = $"Current version: v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"}";
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)))
            .SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ModernSFMPatcher.ico"));
        GamePathBox.Text = InstallerService.DetectSfmInstall() ?? string.Empty;
        UpdateButtons();
        StatusText.Text = "Choose an SFM folder to install or remove its graphics setup.";
        _ = LoadVersionsAsync();
        _ = LoadShaderPackagesAsync();
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            GamePathBox.Text = folder.Path;
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (DxvkVersionBox.SelectedItem is ComboBoxItem d && d.Tag is InstallerService.Release dxvk && ReShadeVersionBox.SelectedItem is ComboBoxItem r && r.Tag is InstallerService.ReShadeRelease reshade)
            await RunOperationAsync(false, dxvk, reshade);
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var hasBackup = _installer.HasBackup(GamePathBox.Text.Trim());
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = hasBackup ? "Restore backup and uninstall?" : "Remove graphics modules?",
            Content = hasBackup
                ? "Modern SFM Patcher will restore the files backed up before installation."
                : "This removes the recognized ReShade and DXVK DLLs. Your ReShade configuration is backed up; shaders and presets are kept.",
            PrimaryButtonText = hasBackup ? "Restore and uninstall" : "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (_working) return;
        SetBusy(true);
        Progress.Visibility = Visibility.Visible;
        try
        {
            await _installer.UninstallAsync(GamePathBox.Text.Trim(), new Progress<string>(message => StatusText.Text = message));
            StatusText.Text = "Graphics setup removed. SFM configuration, shaders, and presets were preserved.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { Progress.Visibility = Visibility.Collapsed; SetBusy(false); }
    }

    private async void InstallRestButton_Click(object sender, RoutedEventArgs e)
    {
        if (RestVersionBox.SelectedItem is ComboBoxItem item && item.Tag is InstallerService.Release release)
        {
            try { SetBusy(true); await _installer.InstallRestAsync(GamePathBox.Text.Trim(), release, new Progress<string>(s => RestStatusText.Text = s)); RestStatusText.Text = $"Installed REST Enhanced {release.Version}."; }
            catch (Exception ex) { RestStatusText.Text = ex.Message; }
            finally { SetBusy(false); }
        }
    }

    private async void RestoreRestButton_Click(object sender, RoutedEventArgs e)
    {
        try { SetBusy(true); await _installer.RestoreRestAsync(GamePathBox.Text.Trim()); RestStatusText.Text = "REST add-on backup restored."; }
        catch (Exception ex) { RestStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e) => await LoadVersionsAsync();

    private async void CheckAppVersionButton_Click(object sender, RoutedEventArgs e)
    {
        CheckAppVersionButton.IsEnabled = false;
        AppUpdateStatusText.Text = "Checking GitHub for the latest release…";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://github.com/rif-t/SFM-Patcher/releases/latest");
            request.Headers.UserAgent.ParseAdd("ModernSfmPatcher/0.1.0");
            using var response = await VersionHttp.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                AppUpdateStatusText.Text = "No GitHub release has been published yet.";
                return;
            }
            response.EnsureSuccessStatusCode();
            var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
            const string tagMarker = "/releases/tag/";
            var tagOffset = finalPath.IndexOf(tagMarker, StringComparison.OrdinalIgnoreCase);
            if (tagOffset < 0)
                throw new InvalidDataException("GitHub did not identify a latest release tag.");
            var latestTag = Uri.UnescapeDataString(finalPath[(tagOffset + tagMarker.Length)..]).TrimEnd('/');
            var latestText = latestTag.TrimStart('v', 'V');
            var currentText = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
            if (!Version.TryParse(latestText, out var latest) || !Version.TryParse(currentText, out var current))
                AppUpdateStatusText.Text = $"Latest GitHub release: {latestTag}. Current version: v{currentText}.";
            else if (latest > current)
                AppUpdateStatusText.Text = $"Update available: v{latestText}. Current version: v{currentText}.";
            else if (latest == current)
                AppUpdateStatusText.Text = $"You're up to date: v{currentText}.";
            else
                AppUpdateStatusText.Text = $"GitHub's latest is v{latestText}; this build is newer (v{currentText}).";
        }
        catch (Exception ex) { AppUpdateStatusText.Text = "Could not check GitHub releases: " + ex.Message; }
        finally { CheckAppVersionButton.IsEnabled = true; }
    }

    private void CheckAllShadersButton_Click(object sender, RoutedEventArgs e) => SetAllShaders(true);
    private void UncheckAllShadersButton_Click(object sender, RoutedEventArgs e) => SetAllShaders(false);
    private async void RefreshShadersButton_Click(object sender, RoutedEventArgs e) => await LoadShaderPackagesAsync();

    private async void InstallShadersButton_Click(object sender, RoutedEventArgs e)
    {
        var selections = _shaderPackages.Select(item => new InstallerService.ShaderSelection(item.Package,
                item.Selection.IsChecked == true ? item.Package.EffectFiles : item.Effects.Where(effect => effect.IsChecked == true).Select(effect => (string)effect.Tag!).ToArray()))
            .Where(item => item.Effects.Count > 0).ToArray();
        if (selections.Length == 0) { ShaderStatusText.Text = "Select one or more shader effects first."; return; }
        if (_working) return;
        SetBusy(true); ShaderProgress.Visibility = Visibility.Visible;
        try
        {
            await _installer.InstallShadersAsync(GamePathBox.Text.Trim(), selections, new Progress<string>(message => ShaderStatusText.Text = message));
            ShaderStatusText.Text = "Selected effects installed. Restart SFM or reload effects in ReShade to see them.";
        }
        catch (Exception ex) { ShaderStatusText.Text = ex.Message; }
        finally { ShaderProgress.Visibility = Visibility.Collapsed; SetBusy(false); }
    }

    private async void RestoreShadersButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Restore shader backup?", Content = "This restores or removes shader files managed by Modern SFM Patcher. Files you edited after installation are kept.", PrimaryButtonText = "Restore", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        SetBusy(true);
        try { ShaderStatusText.Text = await _installer.RestoreShadersAsync(GamePathBox.Text.Trim()); }
        catch (Exception ex) { ShaderStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void InstallPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        SetBusy(true);
        try
        {
            await _installer.InstallSkibidiKittyPresetAsync(GamePathBox.Text.Trim(), new Progress<string>(message => PresetStatusText.Text = message));
            PresetStatusText.Text = "Skibidi Kitty preset and its extra files installed. Restart SFM or reload the preset in ReShade.";
        }
        catch (Exception ex) { PresetStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void RestorePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Restore preset and shader files?", Content = "This restores or removes preset files managed by Modern SFM Patcher. Files you edited after installation are kept.", PrimaryButtonText = "Restore", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        SetBusy(true);
        try { PresetStatusText.Text = await _installer.RestoreShadersAsync(GamePathBox.Text.Trim()); }
        catch (Exception ex) { PresetStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void GamePathBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e) =>
        UpdateButtons();

    private async Task RunOperationAsync(bool restore, InstallerService.Release? dxvk = null, InstallerService.ReShadeRelease? reshade = null)
    {
        if (_working)
            return;

        _working = true;
        UpdateButtons();
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = !restore;

        try
        {
            var progress = new Progress<string>(message => StatusText.Text = message);
            if (restore)
                await _installer.RestoreAsync(GamePathBox.Text.Trim(), progress);
            else
                await _installer.InstallAsync(GamePathBox.Text.Trim(), dxvk!, reshade!, progress);

            StatusText.Text = restore
                ? "Backup restored. Start SFM to verify the original setup."
                : $"Installed ReShade {reshade!.Version} and DXVK {dxvk!.Version}. Start SFM to use the new setup.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            _working = false;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        var path = GamePathBox.Text.Trim();
        var valid = InstallerService.IsSfmInstall(path);
        var hasBackup = valid && _installer.HasBackup(path);
        var installed = valid && InstallerService.IsGraphicsSetupInstalled(path);
        BrowseButton.IsEnabled = !_working;
        GamePathBox.IsEnabled = !_working;
        InstallButton.IsEnabled = !_working && valid && !hasBackup && _dxvk.Count > 0 && _reshade.Count > 0;
        UninstallButton.IsEnabled = !_working && valid && (hasBackup || installed);
        InstallRestButton.IsEnabled = !_working && valid && _rest.Count > 0;
        RestoreRestButton.IsEnabled = !_working && valid && _installer.HasRestBackup(path);
        CheckAllShadersButton.IsEnabled = !_working && _shaderPackages.Count > 0;
        UncheckAllShadersButton.IsEnabled = CheckAllShadersButton.IsEnabled;
        RefreshShadersButton.IsEnabled = !_working;
        InstallShadersButton.IsEnabled = !_working && valid && _shaderPackages.Count > 0;
        RestoreShadersButton.IsEnabled = !_working && valid && _installer.HasShaderBackup(path);
        InstallPresetButton.IsEnabled = !_working && valid;
        RestorePresetButton.IsEnabled = !_working && valid && _installer.HasShaderBackup(path);
        CheckAppVersionButton.IsEnabled = !_working;
        InstallButton.Content = installed ? "Reinstall / repair setup" : "Install graphics setup";
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await InstallerService.GetReleasesAsync();
            _dxvk = versions.Dxvk; _reshade = versions.ReShade; _rest = versions.RestReleases;
            Fill(DxvkVersionBox, _dxvk.Select(x => (x.Version, (object)x)).ToArray());
            Fill(ReShadeVersionBox, _reshade.Select(x => (x.Version, (object)x)).ToArray());
            Fill(RestVersionBox, _rest.Select(x => (x.Version, (object)x)).ToArray());
            var path = GamePathBox.Text.Trim();
            StatusText.Text = InstallerService.IsSfmInstall(path)
                ? $"ReShade {InstalledReShade(path)} — {UpdateState(InstalledReShade(path), _reshade[0].Version)}. DXVK {InstalledDxvk(path)} — {UpdateState(InstalledDxvk(path), _dxvk[0].Version)}. Latest: ReShade {_reshade[0].Version}, DXVK {_dxvk[0].Version}."
                : $"Latest available: ReShade {_reshade[0].Version}, DXVK {_dxvk[0].Version}. Select a valid SFM folder to compare installed versions.";
            RestStatusText.Text = InstallerService.IsSfmInstall(path)
                ? $"REST Enhanced {InstalledRest(path)} — {UpdateState(InstalledRest(path), _rest[0].Version)}. Latest: {_rest[0].Version}."
                : $"Latest REST Enhanced: {_rest[0].Version}. Select a valid SFM folder to compare installed versions.";
            UpdateButtons();
        }
        catch (Exception ex) { StatusText.Text = "Could not check upstream versions: " + ex.Message; RestStatusText.Text = StatusText.Text; }
    }

    private static void Fill(ComboBox box, (string Version, object Value)[] releases)
    {
        var previous = box.SelectedItem is ComboBoxItem selected ? selected.Tag : null;
        box.Items.Clear();
        foreach (var (version, value) in releases)
            box.Items.Add(new ComboBoxItem { Content = version == releases[0].Version ? $"Latest ({version})" : version, Tag = value });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, previous)) ?? box.Items.FirstOrDefault();
    }

    private void SetBusy(bool value) { _working = value; UpdateButtons(); }

    private async Task LoadShaderPackagesAsync()
    {
        ShaderStatusText.Text = "Loading the latest official ReShade effect package list…";
        RefreshShadersButton.IsEnabled = false;
        try
        {
            var packages = await InstallerService.GetShaderPackagesAsync();
            _shaderPackages.Clear(); ShaderPackagesPanel.Children.Clear();
            foreach (var package in packages)
            {
                var selection = new CheckBox { Content = package.Name, IsThreeState = true, IsChecked = package.Required || package.Enabled, IsEnabled = !package.Required, VerticalAlignment = VerticalAlignment.Center };
                var effects = package.EffectFiles.Select(file => new CheckBox { Content = file, Tag = file, Margin = new Thickness(4, 0, 4, 0) }).ToArray();
                var effectList = new StackPanel { Visibility = Visibility.Collapsed, Spacing = 2, Margin = new Thickness(32, 4, 4, 0) };
                foreach (var effect in effects) { effect.Checked += ShaderEffect_Changed; effect.Unchecked += ShaderEffect_Changed; effectList.Children.Add(effect); }
                var expand = new Button { Content = $"Show effects ({effects.Length})", MinWidth = 132 };
                expand.Click += (_, _) => { var show = effectList.Visibility != Visibility.Visible; effectList.Visibility = show ? Visibility.Visible : Visibility.Collapsed; expand.Content = show ? "Hide effects" : $"Show effects ({effects.Length})"; };
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(selection); Grid.SetColumn(expand, 1); row.Children.Add(expand);
                var source = new Button { Content = "View source", MinWidth = 96, Tag = package.RepositoryUrl };
                source.Click += ViewShaderSource_Click; Grid.SetColumn(source, 2); row.Children.Add(source);
                var description = new TextBlock { Text = package.Description, Margin = new Thickness(32, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
                var content = new StackPanel { Spacing = 4 }; content.Children.Add(row); content.Children.Add(description); content.Children.Add(effectList);
                var card = new Border { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = content };
                ShaderPackagesPanel.Children.Add(card);
                var ui = new ShaderPackageUi(package, selection, effects, effectList, expand); _shaderPackages.Add(ui);
                selection.Checked += (_, _) => SetPackageEffects(ui, true);
                selection.Unchecked += (_, _) => SetPackageEffects(ui, false);
                selection.Indeterminate += (_, _) => { };
            }
            ShaderStatusText.Text = $"Loaded {packages.Count} shader packages from the official ReShade list. Required packages remain selected; expand a package to choose individual effects.";
        }
        catch (Exception ex) { ShaderStatusText.Text = "Could not load the ReShade shader list: " + ex.Message; }
        finally { UpdateButtons(); }
    }

    private void SetPackageEffects(ShaderPackageUi package, bool selected)
    {
        if (_updatingShaderSelection) return;
        _updatingShaderSelection = true;
        foreach (var effect in package.Effects) effect.IsChecked = selected;
        _updatingShaderSelection = false;
    }

    private void ShaderEffect_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingShaderSelection || sender is not CheckBox changed) return;
        var package = _shaderPackages.FirstOrDefault(item => item.Effects.Contains(changed));
        if (package is null) return;
        _updatingShaderSelection = true;
        var count = package.Effects.Count(effect => effect.IsChecked == true);
        package.Selection.IsChecked = count == 0 ? false : count == package.Effects.Count ? true : null;
        _updatingShaderSelection = false;
    }

    private void SetAllShaders(bool selected)
    {
        _updatingShaderSelection = true;
        foreach (var package in _shaderPackages)
        {
            var value = selected || package.Package.Required;
            package.Selection.IsChecked = value;
            foreach (var effect in package.Effects) effect.IsChecked = value;
        }
        _updatingShaderSelection = false;
        ShaderStatusText.Text = selected ? "All available effects selected." : "Optional effects unchecked; required effects remain selected.";
    }

    private static void ViewShaderSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static string InstalledReShade(string root)
    {
        var dll = Path.Combine(root, "bin", "d3d9.dll");
        var version = File.Exists(dll) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).FileVersion : null;
        return version ?? "not detected";
    }

    private static string InstalledDxvk(string root)
    {
        foreach (var log in new[] { Path.Combine(root, "sfm_d3d9.log"), Path.Combine(root, "bin", "sfm_d3d9.log") })
            if (File.Exists(log))
            {
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(log), @"DXVK: v([\d.]+)");
                if (match.Success) return match.Groups[1].Value;
            }
        return File.Exists(Path.Combine(root, "bin", "dxvk", "d3d9.dll")) ? "installed (version unknown; run SFM)" : "not detected";
    }

    private static string InstalledRest(string root)
    {
        foreach (var log in new[] { Path.Combine(root, "bin", "ReShade.log"), Path.Combine(root, "ReShade.log") })
            if (File.Exists(log))
            {
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(log), @"REST Enhanced[^\r\n]*?v([\d.]+)");
                if (match.Success) return match.Groups[1].Value;
            }
        return File.Exists(Path.Combine(root, "bin", "ReshadeEffectShaderToggler.addon32")) ? "installed (version unknown; run SFM)" : "not detected";
    }

    private static string UpdateState(string installed, string latest)
    {
        if (!Version.TryParse(installed, out var current) || !Version.TryParse(latest, out var available))
            return "version not verified";
        return current >= available ? "up to date" : "update available";
    }
}


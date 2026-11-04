using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
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
    private readonly Queue<string> _recentErrors = new();
    private bool _updatingShaderSelection;
    private bool _updatingDxvkHud;
    private static readonly HttpClient VersionHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    public MainWindow()
    {
        InitializeComponent();
        AppVersionText.Text = $"Current version: v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"}";
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
        if (DxvkVersionBox.SelectedItem is not ComboBoxItem { Tag: InstallerService.Release dxvk } ||
            ReShadeVersionBox.SelectedItem is not ComboBoxItem { Tag: InstallerService.ReShadeRelease reshade }) return;
        var gameRoot = GamePathBox.Text.Trim();
        if (InstallerService.IsSfmRunning(gameRoot))
        {
            StatusText.Text = "Close Source Filmmaker before installing graphics files.";
            return;
        }

        string? dxvkConfiguration = null;
        if (UseDxvkConfigCheckBox.IsChecked == true)
        {
            try { dxvkConfiguration = InstallerService.ValidateDxvkConfiguration(DxvkConfigTextBox.Text); }
            catch (Exception ex) { RecordError(ex); StatusText.Text = ex.Message; return; }
        }

        var configPath = Path.Combine(gameRoot, "dxvk.conf");
        var configSummary = dxvkConfiguration is null
            ? File.Exists(configPath) ? "Existing dxvk.conf will be left unchanged." : "No custom DXVK configuration will be installed."
            : File.Exists(configPath) ? "The existing dxvk.conf will be backed up and replaced." : "The selected DXVK configuration will be installed.";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Review graphics installation",
            Content = $"SFM folder: {gameRoot}\n\nReShade {reshade.Version} and DXVK {dxvk.Version} (x86)\n\n{configSummary}\n\nExisting graphics files will be backed up before installation. You can restore them later.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        await RunOperationAsync(false, dxvk, reshade, dxvkConfiguration);
    }

    private async void LoadDxvkConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".conf");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            if (new FileInfo(file.Path).Length > 1_048_576)
                throw new InvalidDataException("DXVK configuration files must be 1 MB or smaller.");
            DxvkConfigTextBox.Text = InstallerService.ValidateDxvkConfiguration(await File.ReadAllTextAsync(file.Path));
            UseDxvkConfigCheckBox.IsChecked = true;
            DxvkConfigStatusText.Text = $"Loaded {Path.GetFileName(file.Path)}. Review the settings before installing.";
        }
        catch (Exception ex) { RecordError(ex); DxvkConfigStatusText.Text = ex.Message; }
    }

    private async void LoadInstalledDxvkConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(GamePathBox.Text.Trim(), "dxvk.conf");
        if (!File.Exists(path))
        {
            DxvkConfigStatusText.Text = "No dxvk.conf exists in the selected SFM folder yet.";
            return;
        }
        try
        {
            DxvkConfigTextBox.Text = InstallerService.ValidateDxvkConfiguration(await File.ReadAllTextAsync(path));
            UseDxvkConfigCheckBox.IsChecked = true;
            DxvkConfigStatusText.Text = "Loaded the current SFM configuration. HUD selections match its dxvk.hud setting.";
        }
        catch (Exception ex) { RecordError(ex); DxvkConfigStatusText.Text = ex.Message; }
    }

    private void SaveDxvkConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        var gameRoot = GamePathBox.Text.Trim();
        try
        {
            var configuration = InstallerService.ValidateDxvkConfiguration(DxvkConfigTextBox.Text);
            _working = true;
            UpdateButtons();
            _installer.SaveDxvkConfiguration(gameRoot, configuration);
            DxvkConfigStatusText.Text = "dxvk.conf saved. Restart SFM for DXVK to reload it.";
        }
        catch (Exception ex)
        {
            RecordError(ex);
            DxvkConfigStatusText.Text = ex.Message;
        }
        finally
        {
            _working = false;
            UpdateButtons();
        }
    }

    private async void RestoreDxvkConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        var gameRoot = GamePathBox.Text.Trim();
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Restore previous DXVK configuration?",
            Content = "This restores the dxvk.conf that existed before the first standalone save, or removes the file if none existed.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;

        _working = true;
        UpdateButtons();
        try
        {
            _installer.RestoreDxvkConfiguration(gameRoot);
            var path = Path.Combine(gameRoot, "dxvk.conf");
            DxvkConfigTextBox.Text = File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
            DxvkConfigStatusText.Text = "Previous dxvk.conf restored. Restart SFM to load it.";
        }
        catch (Exception ex)
        {
            RecordError(ex);
            DxvkConfigStatusText.Text = ex.Message;
        }
        finally
        {
            _working = false;
            UpdateButtons();
        }
    }

    private void DxvkHudOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDxvkHud) return;
        try
        {
            var configuration = DxvkConfigTextBox.Text;
            var values = new List<string>();
            if (DxvkHudDeviceCheckBox.IsChecked == true) values.Add("devinfo");
            if (DxvkHudFpsCheckBox.IsChecked == true) values.Add("fps");
            if (DxvkHudFrameTimesCheckBox.IsChecked == true) values.Add("frametimes");
            if (DxvkHudDrawCallsCheckBox.IsChecked == true) values.Add("drawcalls");
            if (DxvkHudMemoryCheckBox.IsChecked == true) values.Add("memory");
            if (DxvkHudGpuLoadCheckBox.IsChecked == true) values.Add("gpuload");
            if (DxvkHudPipelinesCheckBox.IsChecked == true) values.Add("pipelines");
            if (DxvkHudVersionCheckBox.IsChecked == true) values.Add("version");
            if (DxvkHudSubmissionsCheckBox.IsChecked == true) values.Add("submissions");
            if (DxvkHudDescriptorsCheckBox.IsChecked == true) values.Add("descriptors");
            if (DxvkHudAllocationsCheckBox.IsChecked == true) values.Add("allocations");
            if (DxvkHudApiCheckBox.IsChecked == true) values.Add("api");
            if (DxvkHudWorkerStatsCheckBox.IsChecked == true) values.Add("cs");
            if (DxvkHudCompilerCheckBox.IsChecked == true) values.Add("compiler");
            if (DxvkHudSamplersCheckBox.IsChecked == true) values.Add("samplers");
            if (DxvkHudSwvpCheckBox.IsChecked == true) values.Add("swvp");
            DxvkConfigTextBox.Text = MergeDxvkHud(configuration, values);
            UseDxvkConfigCheckBox.IsChecked = true;
            DxvkConfigStatusText.Text = values.Count == 0 ? "HUD disabled in dxvk.conf." : "HUD settings updated in dxvk.conf.";
        }
        catch (Exception ex) { RecordError(ex); DxvkConfigStatusText.Text = ex.Message; }
    }

    private void DxvkConfig_Toggled(object sender, RoutedEventArgs e)
    {
        var enabled = UseDxvkConfigCheckBox.IsChecked == true && !_working;
        DxvkConfigTextBox.IsEnabled = enabled;
        LoadDxvkConfigButton.IsEnabled = enabled;
        DxvkConfigStatusText.Text = UseDxvkConfigCheckBox.IsChecked == true
            ? "Enabled. An existing dxvk.conf will be backed up before replacement."
            : "Disabled. Any existing SFM dxvk.conf stays untouched.";
        UpdateButtons();
    }

    private void DxvkConfigTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingDxvkHud)
        {
            _updatingDxvkHud = true;
            try
            {
                var values = ReadDxvkHud(DxvkConfigTextBox.Text);
                DxvkHudDeviceCheckBox.IsChecked = values.Contains("devinfo");
                DxvkHudFpsCheckBox.IsChecked = values.Contains("fps");
                DxvkHudFrameTimesCheckBox.IsChecked = values.Contains("frametimes");
                DxvkHudDrawCallsCheckBox.IsChecked = values.Contains("drawcalls");
                DxvkHudMemoryCheckBox.IsChecked = values.Contains("memory");
                DxvkHudGpuLoadCheckBox.IsChecked = values.Contains("gpuload");
                DxvkHudPipelinesCheckBox.IsChecked = values.Contains("pipelines");
                DxvkHudVersionCheckBox.IsChecked = values.Contains("version");
                DxvkHudSubmissionsCheckBox.IsChecked = values.Contains("submissions");
                DxvkHudDescriptorsCheckBox.IsChecked = values.Contains("descriptors");
                DxvkHudAllocationsCheckBox.IsChecked = values.Contains("allocations");
                DxvkHudApiCheckBox.IsChecked = values.Contains("api");
                DxvkHudWorkerStatsCheckBox.IsChecked = values.Contains("cs");
                DxvkHudCompilerCheckBox.IsChecked = values.Contains("compiler");
                DxvkHudSamplersCheckBox.IsChecked = values.Contains("samplers");
                DxvkHudSwvpCheckBox.IsChecked = values.Contains("swvp");
            }
            finally { _updatingDxvkHud = false; }
        }
        UpdateButtons();
    }

    private static string[] ReadDxvkHud(string configuration)
    {
        var inAppSection = false;
        foreach (var line in configuration.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = line.Trim();
            if (value.StartsWith('[')) { inAppSection = true; continue; }
            if (inAppSection || value.StartsWith('#') || value.StartsWith(';')) continue;
            var separator = value.IndexOf('=');
            if (separator < 0 || !value[..separator].Trim().Equals("dxvk.hud", StringComparison.OrdinalIgnoreCase)) continue;
            var hud = value[(separator + 1)..].Trim().Trim('"');
            if (hud.Equals("full", StringComparison.OrdinalIgnoreCase))
                return ["devinfo", "fps", "frametimes", "submissions", "drawcalls", "pipelines", "descriptors", "memory", "allocations", "gpuload", "version", "api", "cs", "compiler", "samplers", "swvp"];
            if (hud == "1") return ["devinfo", "fps"];
            return hud.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }

    private static string MergeDxvkHud(string configuration, IReadOnlyCollection<string> selected)
    {
        var known = new HashSet<string>(["devinfo", "fps", "frametimes", "submissions", "drawcalls", "pipelines", "descriptors", "memory", "allocations", "gpuload", "version", "api", "cs", "compiler", "samplers", "swvp", "full", "1"], StringComparer.OrdinalIgnoreCase);
        var lines = configuration.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (string.IsNullOrWhiteSpace(configuration)) lines.Clear();
        var preserved = ReadDxvkHud(configuration).Where(value => !known.Contains(value)).ToArray();
        var setting = "dxvk.hud = " + string.Join(",", preserved.Concat(selected));
        var appSection = lines.FindIndex(line => line.TrimStart().StartsWith('['));
        for (var index = 0; index < (appSection < 0 ? lines.Count : appSection); index++)
        {
            var line = lines[index].Trim();
            var separator = line.IndexOf('=');
            if (separator >= 0 && line[..separator].Trim().Equals("dxvk.hud", StringComparison.OrdinalIgnoreCase))
            {
                lines[index] = setting;
                return string.Join(Environment.NewLine, lines);
            }
        }
        lines.Insert(appSection < 0 ? lines.Count : appSection, setting);
        return string.Join(Environment.NewLine, lines);
    }

    private void OpenBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var backup = _installer.GetBackupDirectory(GamePathBox.Text.Trim());
        if (backup is not null && Directory.Exists(backup))
        {
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add(backup);
            System.Diagnostics.Process.Start(start);
        }
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        var gameRoot = GamePathBox.Text.Trim();
        var hasBackup = _installer.HasBackup(gameRoot);
        var interrupted = hasBackup && !_installer.IsInstallationComplete(gameRoot);
        var backup = _installer.GetBackupDirectory(gameRoot);
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = interrupted ? "Restore interrupted installation?" : hasBackup ? "Restore backup and uninstall?" : "Remove graphics modules?",
            Content = hasBackup
                ? $"Modern SFM Patcher will restore the files backed up before installation.{(interrupted ? " The install was interrupted; files changed since then will be kept for safety." : "")}\n\nBackup folder: {backup}"
                : "This removes the recognized ReShade and DXVK DLLs. Your ReShade configuration is backed up; shaders and presets are kept.",
            PrimaryButtonText = hasBackup ? "Restore backup" : "Remove",
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
        catch (Exception ex) { RecordError(ex); StatusText.Text = ex.Message; }
        finally { Progress.Visibility = Visibility.Collapsed; SetBusy(false); }
    }

    private async void InstallRestButton_Click(object sender, RoutedEventArgs e)
    {
        if (RestVersionBox.SelectedItem is ComboBoxItem item && item.Tag is InstallerService.Release release)
        {
            try { SetBusy(true); await _installer.InstallRestAsync(GamePathBox.Text.Trim(), release, new Progress<string>(s => RestStatusText.Text = s)); RestStatusText.Text = $"Installed REST Enhanced {release.Version}."; }
            catch (Exception ex) { RecordError(ex); RestStatusText.Text = ex.Message; }
            finally { SetBusy(false); }
        }
    }

    private async void RestoreRestButton_Click(object sender, RoutedEventArgs e)
    {
        try { SetBusy(true); await _installer.RestoreRestAsync(GamePathBox.Text.Trim()); RestStatusText.Text = "REST add-on backup restored."; }
        catch (Exception ex) { RecordError(ex); RestStatusText.Text = ex.Message; }
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
            request.Headers.UserAgent.ParseAdd("ModernSfmPatcher/0.2.0");
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
            var currentText = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0";
            if (!Version.TryParse(latestText, out var latest) || !Version.TryParse(currentText, out var current))
                AppUpdateStatusText.Text = $"Latest GitHub release: {latestTag}. Current version: v{currentText}.";
            else if (latest > current)
                AppUpdateStatusText.Text = $"Update available: v{latestText}. Current version: v{currentText}.";
            else if (latest == current)
                AppUpdateStatusText.Text = $"You're up to date: v{currentText}.";
            else
                AppUpdateStatusText.Text = $"GitHub's latest is v{latestText}; this build is newer (v{currentText}).";
        }
        catch (Exception ex) { RecordError(ex); AppUpdateStatusText.Text = "Could not check GitHub releases: " + ex.Message; }
        finally { CheckAppVersionButton.IsEnabled = true; }
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "Modern-SFM-Patcher-diagnostics.txt" };
            picker.FileTypeChoices.Add("Text report", [".txt"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            var root = GamePathBox.Text.Trim();
            var report = new StringBuilder()
                .AppendLine("Modern SFM Patcher diagnostics")
                .AppendLine($"Created: {DateTimeOffset.Now:O}")
                .AppendLine($"App version: {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"}")
                .AppendLine($"Windows: {Environment.OSVersion.VersionString} (build {Environment.OSVersion.Version.Build})")
                .AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
                .AppendLine($"SFM folder: {(InstallerService.IsSfmInstall(root) ? root : "not selected or invalid")}")
                .AppendLine($"SFM running: {(InstallerService.IsSfmInstall(root) && InstallerService.IsSfmRunning(root))}")
                .AppendLine($"Graphics setup: {(InstallerService.IsSfmInstall(root) && InstallerService.IsGraphicsSetupInstalled(root) ? "recognized" : "not detected")}")
                .AppendLine($"ReShade: {(InstallerService.IsSfmInstall(root) ? InstalledReShade(root) : "unknown")}")
                .AppendLine($"DXVK: {(InstallerService.IsSfmInstall(root) ? InstalledDxvk(root) : "unknown")}")
                .AppendLine($"REST Enhanced: {(InstallerService.IsSfmInstall(root) ? InstalledRest(root) : "unknown")}")
                .AppendLine($"dxvk.conf present: {InstallerService.IsSfmInstall(root) && File.Exists(Path.Combine(root, "dxvk.conf"))}")
                .AppendLine($"Graphics backup: {(InstallerService.IsSfmInstall(root) ? _installer.GetBackupDirectory(root) ?? "not found" : "unknown")}")
                .AppendLine()
                .AppendLine("Current app status")
                .AppendLine(StatusText.Text)
                .AppendLine(RestStatusText.Text)
                .AppendLine(ShaderStatusText.Text)
                .AppendLine(PresetStatusText.Text)
                .AppendLine(AppUpdateStatusText.Text)
                .AppendLine()
                .AppendLine("Recent app errors (current session)")
                .AppendLine(_recentErrors.Count == 0 ? "None recorded." : string.Join(Environment.NewLine + Environment.NewLine, _recentErrors))
                .AppendLine();

            if (InstallerService.IsSfmInstall(root))
            {
                AppendLogTail(report, Path.Combine(root, "sfm_d3d9.log"));
                AppendLogTail(report, Path.Combine(root, "bin", "sfm_d3d9.log"));
                AppendLogTail(report, Path.Combine(root, "ReShade.log"));
                AppendLogTail(report, Path.Combine(root, "bin", "ReShade.log"));
            }
            await File.WriteAllTextAsync(file.Path, report.ToString(), new UTF8Encoding(false));
            DiagnosticsStatusText.Text = $"Diagnostics saved to {file.Path}. Review paths and log excerpts before sharing.";
        }
        catch (Exception ex)
        {
            RecordError(ex);
            DiagnosticsStatusText.Text = "Could not export diagnostics: " + ex.Message;
        }
    }

    private static void AppendLogTail(StringBuilder report, string path)
    {
        report.AppendLine().AppendLine($"--- {path} (last 100 lines) ---");
        try
        {
            if (!File.Exists(path)) { report.AppendLine("Not found."); return; }
            foreach (var line in File.ReadLines(path).TakeLast(100)) report.AppendLine(line);
        }
        catch (Exception ex) { report.AppendLine("Could not read log: " + ex.Message); }
    }

    private void RecordError(Exception error)
    {
        _recentErrors.Enqueue($"{DateTimeOffset.Now:O}: {error}");
        while (_recentErrors.Count > 20) _recentErrors.Dequeue();
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
            var missingEffects = await _installer.InstallShadersAsync(GamePathBox.Text.Trim(), selections, new Progress<string>(message => ShaderStatusText.Text = message));
            ShaderStatusText.Text = missingEffects.Count == 0
                ? "Selected effects installed. Restart SFM or reload effects in ReShade to see them."
                : $"Installed available effects. Skipped files missing from their current archives: {string.Join(", ", missingEffects)}. Refresh the list to update package entries.";
        }
        catch (Exception ex) { RecordError(ex); ShaderStatusText.Text = ex.Message; }
        finally { ShaderProgress.Visibility = Visibility.Collapsed; SetBusy(false); }
    }

    private async void RestoreShadersButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Restore shader backup?", Content = "This restores or removes shader files managed by Modern SFM Patcher. Files you edited after installation are kept.", PrimaryButtonText = "Restore", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        SetBusy(true);
        try { ShaderStatusText.Text = await _installer.RestoreShadersAsync(GamePathBox.Text.Trim()); }
        catch (Exception ex) { RecordError(ex); ShaderStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void InstallPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        string[] missingEffects;
        try { missingEffects = InstallerService.GetMissingPresetEffects(GamePathBox.Text.Trim()); }
        catch (Exception ex) { RecordError(ex); PresetStatusText.Text = ex.Message; return; }
        var content = missingEffects.Length == 0
            ? "All preset effects are already included or installed. The preset and its bundled shader/texture files will be installed."
            : "These preset effects are missing and will be downloaded from the latest official ReShade shader packages:\n\n" + string.Join("\n", missingEffects) + "\n\nThe preset and its bundled shader/texture files will also be installed.";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Review preset requirements",
            Content = content,
            PrimaryButtonText = "Install preset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        SetBusy(true);
        try
        {
            await _installer.InstallSkibidiKittyPresetAsync(GamePathBox.Text.Trim(), new Progress<string>(message => PresetStatusText.Text = message));
            PresetStatusText.Text = "Skibidi Kitty preset and its extra files installed. Restart SFM or reload the preset in ReShade.";
        }
        catch (Exception ex) { RecordError(ex); PresetStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void RestorePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Restore preset and shader files?", Content = "This restores or removes preset files managed by Modern SFM Patcher. Files you edited after installation are kept.", PrimaryButtonText = "Restore", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _working) return;
        SetBusy(true);
        try { PresetStatusText.Text = await _installer.RestoreShadersAsync(GamePathBox.Text.Trim()); }
        catch (Exception ex) { RecordError(ex); PresetStatusText.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void GamePathBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e) =>
        UpdateButtons();

    private async Task RunOperationAsync(bool restore, InstallerService.Release? dxvk = null, InstallerService.ReShadeRelease? reshade = null, string? dxvkConfiguration = null)
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
                await _installer.InstallAsync(GamePathBox.Text.Trim(), dxvk!, reshade!, progress, dxvkConfiguration);

            StatusText.Text = restore
                ? "Backup restored. Start SFM to verify the original setup."
                : $"Installed ReShade {reshade!.Version} and DXVK {dxvk!.Version}{(dxvkConfiguration is null ? "" : " with your DXVK configuration")}. Start SFM to use the new setup.";
        }
        catch (Exception ex)
        {
            RecordError(ex);
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
        var interrupted = hasBackup && !_installer.IsInstallationComplete(path);
        var reusableBackup = valid && hasBackup && _installer.CanInstallWithExistingBackup(path);
        var sfmRunning = valid && InstallerService.IsSfmRunning(path);
        var configInvalid = UseDxvkConfigCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(DxvkConfigTextBox.Text);
        var canInstall = !_working && valid && _installer.CanInstallWithExistingBackup(path) && _dxvk.Count > 0 && _reshade.Count > 0 &&
            !configInvalid && !sfmRunning;
        BrowseButton.IsEnabled = !_working;
        GamePathBox.IsEnabled = !_working;
        InstallButton.IsEnabled = canInstall;
        UninstallButton.IsEnabled = !_working && valid && (hasBackup || installed);
        OpenBackupButton.IsEnabled = !_working && hasBackup;
        BackupStatusText.Text = interrupted
            ? $"An interrupted install was detected. Restore it before installing again. Backup: {_installer.GetBackupDirectory(path)}"
            : reusableBackup && !installed
                ? $"A previous setup was restored. Current SFM config will be backed up before a new install. Backup folder: {_installer.GetBackupDirectory(path)}"
                : hasBackup ? $"Graphics backup is available: {_installer.GetBackupDirectory(path)}" : "No managed graphics backup found.";
        InstallAvailabilityText.Text = _working ? "An operation is in progress."
            : !valid ? "Select the SFM game folder containing sfm.exe."
            : _dxvk.Count == 0 || _reshade.Count == 0 ? "Waiting for ReShade and DXVK version lists to load. Check your internet connection or retry updates."
            : interrupted || hasBackup && !reusableBackup ? "Restore the active or interrupted graphics backup before installing again."
            : configInvalid ? "Load or enter a non-empty dxvk.conf, or turn off its checkbox."
            : sfmRunning ? "Close Source Filmmaker before installing graphics files."
            : "Ready to install ReShade and DXVK.";
        UseDxvkConfigCheckBox.IsEnabled = !_working;
        var configEnabled = !_working && UseDxvkConfigCheckBox.IsChecked == true;
        DxvkConfigTextBox.IsEnabled = configEnabled;
        LoadDxvkConfigButton.IsEnabled = configEnabled;
        LoadInstalledDxvkConfigButton.IsEnabled = !_working && valid;
        SaveDxvkConfigButton.IsEnabled = !_working && valid && !sfmRunning && !string.IsNullOrWhiteSpace(DxvkConfigTextBox.Text);
        RestoreDxvkConfigButton.IsEnabled = !_working && valid && !sfmRunning && _installer.HasDxvkConfigurationBackup(path);
        foreach (var hudOption in new[] { DxvkHudFpsCheckBox, DxvkHudVersionCheckBox, DxvkHudDeviceCheckBox, DxvkHudFrameTimesCheckBox, DxvkHudDrawCallsCheckBox, DxvkHudMemoryCheckBox, DxvkHudGpuLoadCheckBox, DxvkHudPipelinesCheckBox, DxvkHudSubmissionsCheckBox, DxvkHudDescriptorsCheckBox, DxvkHudAllocationsCheckBox, DxvkHudApiCheckBox, DxvkHudWorkerStatsCheckBox, DxvkHudCompilerCheckBox, DxvkHudSamplersCheckBox, DxvkHudSwvpCheckBox })
            hudOption.IsEnabled = configEnabled;
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
        ExportDiagnosticsButton.IsEnabled = !_working;
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
        catch (Exception ex) { RecordError(ex); StatusText.Text = "Could not check upstream versions: " + ex.Message; RestStatusText.Text = StatusText.Text; }
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
        catch (Exception ex) { RecordError(ex); ShaderStatusText.Text = "Could not load the ReShade shader list: " + ex.Message; }
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


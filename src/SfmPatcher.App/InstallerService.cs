using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ModernSfmPatcher.App;

internal sealed class InstallerService
{
    private const string ReshadeSignerThumbprint = "589690208A5E52FB96980C4A6698F50ACD47C49F";
    // Keep this legacy location so upgrades can still restore existing backups and cached versions.
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFM-Patcher", "installs");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    internal sealed record Release(string Version, string Url, string Sha256);
    internal sealed record ReShadeRelease(string Version, string Url);
    private sealed record ReleaseCache(IReadOnlyList<Release> Dxvk, IReadOnlyList<ReShadeRelease> ReShade, IReadOnlyList<Release> RestReleases);
    internal sealed record ShaderPackage(string Name, string Description, string DownloadUrl, string RepositoryUrl,
        string InstallPath, string TextureInstallPath, string[] EffectFiles, bool Required, bool Enabled);
    internal sealed record ShaderSelection(ShaderPackage Package, IReadOnlyList<string> Effects);
    private sealed record RestState(string GameRoot, string? BackupPath, string InstalledHash, string Version);
    private sealed record DxvkConfigState(string GameRoot, bool OriginalExisted);
    private sealed class ShaderState
    {
        public required string GameRoot { get; init; }
        public Dictionary<string, string?> OriginalFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> InstalledHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<(IReadOnlyList<Release> Dxvk, IReadOnlyList<ReShadeRelease> ReShade, IReadOnlyList<Release> RestReleases)> GetReleasesAsync(CancellationToken token = default)
    {
        try
        {
            var releases = await GetUpstreamReleasesAsync(token);
            var cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFM-Patcher", "releases.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(new ReleaseCache(releases.Dxvk, releases.ReShade, releases.RestReleases)), token);
            return releases;
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode == 403)
        {
            var cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SFM-Patcher", "releases.json");
            if (File.Exists(cachePath))
            {
                var cached = JsonSerializer.Deserialize<ReleaseCache>(await File.ReadAllTextAsync(cachePath, token));
                if (cached is { Dxvk.Count: > 0, ReShade.Count: > 0, RestReleases.Count: > 0 })
                    return (cached.Dxvk, cached.ReShade, cached.RestReleases);
            }
            throw new InvalidOperationException("GitHub is rate-limiting version checks (HTTP 403), and no saved version list is available yet. Try again after GitHub resets the limit.", ex);
        }
    }

    private static async Task<(IReadOnlyList<Release> Dxvk, IReadOnlyList<ReShadeRelease> ReShade, IReadOnlyList<Release> RestReleases)> GetUpstreamReleasesAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/doitsujin/dxvk/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("ModernSfmPatcher/0.2.0");
        using var response = await Http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var dxvkJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var dxvk = dxvkJson.RootElement.EnumerateArray().Where(r => !r.GetProperty("draft").GetBoolean() && !r.GetProperty("prerelease").GetBoolean())
            .SelectMany(r => r.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString()?.Equals($"dxvk-{r.GetProperty("tag_name").GetString()!.TrimStart('v')}.tar.gz", StringComparison.OrdinalIgnoreCase) == true)
                .Select(a => new Release(r.GetProperty("tag_name").GetString()!.TrimStart('v'), a.GetProperty("browser_download_url").GetString()!, ReadSha256(a))))
            .Where(r => r.Sha256.Length == 64).ToArray();
        using var tagRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/crosire/reshade/tags?per_page=100");
        tagRequest.Headers.UserAgent.ParseAdd("ModernSfmPatcher/0.2.0");
        using var tagResponse = await Http.SendAsync(tagRequest, token);
        tagResponse.EnsureSuccessStatusCode();
        using var tagJson = JsonDocument.Parse(await tagResponse.Content.ReadAsStringAsync(token));
        var reshade = tagJson.RootElement.EnumerateArray().Select(t => t.GetProperty("name").GetString() ?? "")
            .Where(v => Regex.IsMatch(v, "^v?\\d+\\.\\d+\\.\\d+$"))
            .Select(v => v.TrimStart('v')).Distinct().OrderByDescending(v => Version.Parse(v)).Select(v => new ReShadeRelease(v, $"https://reshade.me/downloads/ReShade_Setup_{v}_Addon.exe")).ToArray();
        var rest = await GetGithubReleasesAsync("https://api.github.com/repos/Pav-Osmolski/ReshadeEffectShaderToggler/releases?per_page=100", "release.zip", token);
        if (dxvk.Length == 0 || reshade.Length == 0 || rest.Count == 0) throw new InvalidDataException("GitHub did not return usable stable releases for all components.");
        return (dxvk, reshade, rest);
    }

    private static async Task<IReadOnlyList<Release>> GetGithubReleasesAsync(string url, string assetName, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ModernSfmPatcher/0.2.0");
        using var response = await Http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.EnumerateArray().Where(r => !r.GetProperty("draft").GetBoolean() && !r.GetProperty("prerelease").GetBoolean())
            .SelectMany(r => r.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == assetName)
                .Select(a => new Release(r.GetProperty("tag_name").GetString()!.TrimStart('v'), a.GetProperty("browser_download_url").GetString()!, ReadSha256(a))))
            .Where(r => r.Sha256.Length == 64).ToArray();
    }

    private static string ReadSha256(JsonElement asset)
    {
        if (!asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String)
            return string.Empty;
        var value = digest.GetString();
        return value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? value[7..] : string.Empty;
    }

    public static async Task<IReadOnlyList<ShaderPackage>> GetShaderPackagesAsync(CancellationToken token = default)
    {
        var text = await Http.GetStringAsync("https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini", token);
        var packages = new List<ShaderPackage>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void AddPackage()
        {
            if (!fields.TryGetValue("PackageName", out var name)) return;
            var denied = fields.GetValueOrDefault("DenyEffectFiles", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var effects = fields.GetValueOrDefault("EffectFiles", "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(file => !denied.Contains(file, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (effects.Length == 0 || !fields.TryGetValue("DownloadUrl", out var url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return;
            packages.Add(new ShaderPackage(name, fields.GetValueOrDefault("PackageDescription", ""), url,
                fields.GetValueOrDefault("RepositoryUrl", ""), fields.GetValueOrDefault("InstallPath", @".\reshade-shaders\Shaders"),
                fields.GetValueOrDefault("TextureInstallPath", @".\reshade-shaders\Textures"), effects,
                fields.GetValueOrDefault("Required") == "1", fields.GetValueOrDefault("Enabled") == "1"));
        }
        foreach (var line in text.Split('\n'))
        {
            var value = line.Trim();
            if (value.StartsWith('[')) { AddPackage(); fields.Clear(); continue; }
            var separator = value.IndexOf('=');
            if (separator > 0) fields[value[..separator].Trim()] = value[(separator + 1)..].Trim();
        }
        AddPackage();
        return packages;
    }

    public async Task<IReadOnlyList<string>> InstallShadersAsync(string gameRoot, IReadOnlyList<ShaderSelection> selections,
        IProgress<string> progress, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (selections.Count == 0) throw new InvalidOperationException("Select at least one shader effect to install.");
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before installing shaders.");
        var stateDir = StateDirectory(gameRoot);
        var stageDir = Path.Combine(stateDir, "shader-staging-" + Guid.NewGuid().ToString("N"));
        var stateFile = Path.Combine(stateDir, "shaders.json");
        Directory.CreateDirectory(stageDir);
        var state = File.Exists(stateFile)
            ? JsonSerializer.Deserialize<ShaderState>(await File.ReadAllTextAsync(stateFile, token)) ?? throw new InvalidDataException("Shader backup manifest is invalid.")
            : new ShaderState { GameRoot = gameRoot };
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Shader backup belongs to another SFM folder.");
        var stagedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingEffects = new List<string>();
        try
        {
            foreach (var selection in selections)
            {
                var package = selection.Package;
                var chosen = selection.Effects.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!package.EffectFiles.Any(chosen.Contains)) continue;
                progress.Report($"Downloading latest {package.Name}…");
                var archive = Path.Combine(stageDir, Guid.NewGuid().ToString("N") + ".zip");
                await DownloadAsync(package.DownloadUrl, archive, progress, token);
                using var archiveFile = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var zip = new ZipArchive(archiveFile, ZipArchiveMode.Read);
                var root = zip.Entries.Select(e => e.FullName.Split('/')[0]).FirstOrDefault() ?? "";
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var packageFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/')) continue;
                    var relative = entry.FullName.StartsWith(root + "/", StringComparison.Ordinal) ? entry.FullName[(root.Length + 1)..] : entry.FullName;
                    string? target = null; string? tail = null;
                    if (relative.StartsWith("Shaders/", StringComparison.OrdinalIgnoreCase))
                    {
                        tail = relative["Shaders/".Length..];
                        var filename = Path.GetFileName(tail);
                        if (filename.EndsWith(".fx", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!chosen.Contains(filename)) continue;
                            found.Add(filename);
                        }
                        else if (!filename.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)) continue;
                        target = ResolvePackagePath(package.InstallPath, tail);
                    }
                    else if (relative.StartsWith("Textures/", StringComparison.OrdinalIgnoreCase))
                    {
                        tail = relative["Textures/".Length..];
                        target = ResolvePackagePath(package.TextureInstallPath, tail);
                    }
                    if (target is null) continue;
                    var staged = Path.Combine(stageDir, Guid.NewGuid().ToString("N"));
                    await using (var input = entry.Open()) await using (var output = File.Create(staged)) await input.CopyToAsync(output, token);
                    if (packageFiles.TryGetValue(target, out var duplicate) && !HashFile(duplicate).Equals(HashFile(staged), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"More than one selected package provides a different file at {target}.");
                    packageFiles[target] = staged;
                }
                File.Delete(archive);
                missingEffects.AddRange(chosen.Where(effect => !found.Contains(effect)).Select(effect => $"{effect} ({package.Name})"));
                if (found.Count == 0)
                {
                    progress.Report($"Skipped {package.Name}: its current archive did not contain the selected effect files.");
                    continue;
                }
                foreach (var (target, staged) in packageFiles)
                {
                    if (stagedFiles.TryGetValue(target, out var duplicate) && !HashFile(duplicate).Equals(HashFile(staged), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"More than one selected package provides a different file at {target}.");
                    stagedFiles[target] = staged;
                }
            }

            if (stagedFiles.Count == 0)
                throw new InvalidDataException("None of the selected effects were present in their current shader package archives. Refresh the list and try again.");

            var updated = new ShaderState { GameRoot = gameRoot, OriginalFiles = new(state.OriginalFiles, StringComparer.OrdinalIgnoreCase), InstalledHashes = new(state.InstalledHashes, StringComparer.OrdinalIgnoreCase) };
            foreach (var (relative, staged) in stagedFiles)
            {
                var target = SafeCombine(gameRoot, relative);
                if (!updated.OriginalFiles.ContainsKey(relative))
                {
                    if (File.Exists(target))
                    {
                        var backup = SafeCombine(Path.Combine(stateDir, "shader-original"), relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(target, backup, overwrite: true);
                        updated.OriginalFiles[relative] = Path.GetRelativePath(Path.Combine(stateDir, "shader-original"), backup);
                    }
                    else updated.OriginalFiles[relative] = null;
                }
                else if (File.Exists(target) && updated.InstalledHashes.TryGetValue(relative, out var oldHash) && !HashFile(target).Equals(oldHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Keeping your modified shader file: {relative}");
                updated.InstalledHashes[relative] = HashFile(staged);
            }
            SaveState(stateFile, updated);
            foreach (var (relative, staged) in stagedFiles)
            {
                var target = SafeCombine(gameRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".modern-sfm-patcher-tmp";
                try { File.Copy(staged, temporary, overwrite: true); File.Move(temporary, target, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            progress.Report($"Installed or updated {stagedFiles.Count} shader support files from the selected latest repositories.");
            return missingEffects;
        }
        finally { TryDeleteDirectory(stageDir); }
    }

    public async Task<string> RestoreShadersAsync(string gameRoot, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before restoring shader files.");
        var stateFile = Path.Combine(StateDirectory(gameRoot), "shaders.json");
        if (!File.Exists(stateFile)) throw new InvalidOperationException("No Modern SFM Patcher shader installation backup exists.");
        var state = JsonSerializer.Deserialize<ShaderState>(await File.ReadAllTextAsync(stateFile, token)) ?? throw new InvalidDataException("Shader backup manifest is invalid.");
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Shader backup belongs to another SFM folder.");
        var keptModified = new List<string>();
        foreach (var (relative, original) in state.OriginalFiles)
        {
            var target = SafeCombine(gameRoot, relative);
            if (File.Exists(target) && state.InstalledHashes.TryGetValue(relative, out var hash) && !HashFile(target).Equals(hash, StringComparison.OrdinalIgnoreCase)) { keptModified.Add(relative); continue; }
            if (original is null) { if (File.Exists(target)) File.Delete(target); }
            else
            {
                var backup = SafeCombine(Path.Combine(StateDirectory(gameRoot), "shader-original"), original);
                if (!File.Exists(backup)) throw new FileNotFoundException("A shader backup file is missing.", backup);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(backup, target, overwrite: true);
            }
        }
        File.Delete(stateFile);
        return keptModified.Count == 0 ? "Shader backup restored." : "Restored unchanged shader files; kept later edits to: " + string.Join(", ", keptModified);
    }

    public static string[] GetMissingPresetEffects(string gameRoot)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        var presetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "SkibidiKittyPreset");
        var presetFile = Path.Combine(presetRoot, "ReShadePreset.ini");
        var shaderRoot = Path.Combine(presetRoot, "Shaders");
        if (!File.Exists(presetFile) || !Directory.Exists(shaderRoot))
            throw new DirectoryNotFoundException("The bundled Skibidi Kitty preset files are missing.");

        var bundledEffects = Directory.EnumerateFiles(shaderRoot, "*.fx", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var techniqueLine = File.ReadLines(presetFile).FirstOrDefault(line => line.TrimStart().StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase));
        var requiredEffects = techniqueLine?[(techniqueLine.IndexOf('=') + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(technique => technique[(technique.LastIndexOf('@') + 1)..]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? throw new InvalidDataException("The preset does not list any ReShade techniques.");
        var installedRoot = Path.Combine(gameRoot, "bin", "reshade-shaders", "Shaders");
        var installedEffects = Directory.Exists(installedRoot)
            ? Directory.EnumerateFiles(installedRoot, "*.fx", SearchOption.AllDirectories).Select(path => Path.GetFileName(path)!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return requiredEffects.Where(effect => !bundledEffects.Contains(effect) && !installedEffects.Contains(effect)).ToArray();
    }

    public async Task InstallSkibidiKittyPresetAsync(string gameRoot, IProgress<string> progress, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before installing the preset.");
        var presetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "SkibidiKittyPreset");
        var presetFile = Path.Combine(presetRoot, "ReShadePreset.ini");
        var shaderRoot = Path.Combine(presetRoot, "Shaders");
        var textureRoot = Path.Combine(presetRoot, "Textures");
        if (!File.Exists(presetFile) || !Directory.Exists(shaderRoot) || !Directory.Exists(textureRoot))
            throw new DirectoryNotFoundException("The bundled Skibidi Kitty preset files are missing.");

        var missingEffects = GetMissingPresetEffects(gameRoot);
        if (missingEffects.Length > 0)
        {
            progress.Report("Loading official ReShade shader packages for the preset…");
            var packages = await GetShaderPackagesAsync(token);
            var selections = new List<ShaderSelection>();
            foreach (var effect in missingEffects)
            {
                var package = packages.FirstOrDefault(item => item.EffectFiles.Contains(effect, StringComparer.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"No official ReShade package lists the preset effect {effect}.");
                var selection = selections.FirstOrDefault(item => ReferenceEquals(item.Package, package));
                if (selection is null)
                    selections.Add(new ShaderSelection(package, [effect]));
                else
                    selections[selections.IndexOf(selection)] = selection with { Effects = selection.Effects.Append(effect).ToArray() };
            }
            var unavailableEffects = await InstallShadersAsync(gameRoot, selections, progress, token);
            if (unavailableEffects.Count > 0)
                throw new InvalidDataException("The preset still requires effects missing from current ReShade archives: " + string.Join(", ", unavailableEffects));
        }

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bin/ReShadePreset.ini"] = presetFile
        };
        foreach (var source in Directory.EnumerateFiles(shaderRoot, "*", SearchOption.AllDirectories))
            sources["bin/reshade-shaders/Shaders/" + Path.GetRelativePath(shaderRoot, source).Replace('\\', '/')] = source;
        foreach (var source in Directory.EnumerateFiles(textureRoot, "*", SearchOption.AllDirectories))
            sources["bin/reshade-shaders/Textures/" + Path.GetRelativePath(textureRoot, source).Replace('\\', '/')] = source;

        var stateDir = StateDirectory(gameRoot);
        Directory.CreateDirectory(stateDir);
        var stageDir = Path.Combine(stateDir, "preset-staging-" + Guid.NewGuid().ToString("N"));
        var stateFile = Path.Combine(stateDir, "shaders.json");
        Directory.CreateDirectory(stageDir);
        var state = File.Exists(stateFile)
            ? JsonSerializer.Deserialize<ShaderState>(await File.ReadAllTextAsync(stateFile, token)) ?? throw new InvalidDataException("Shader backup manifest is invalid.")
            : new ShaderState { GameRoot = gameRoot };
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Shader backup belongs to another SFM folder.");
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (relative, source) in sources)
            {
                token.ThrowIfCancellationRequested();
                var temporary = Path.Combine(stageDir, Guid.NewGuid().ToString("N"));
                File.Copy(source, temporary);
                staged.Add(relative, temporary);
            }

            var updated = new ShaderState
            {
                GameRoot = gameRoot,
                OriginalFiles = new(state.OriginalFiles, StringComparer.OrdinalIgnoreCase),
                InstalledHashes = new(state.InstalledHashes, StringComparer.OrdinalIgnoreCase)
            };
            foreach (var (relative, temporary) in staged)
            {
                var target = SafeCombine(gameRoot, relative);
                if (!updated.OriginalFiles.ContainsKey(relative))
                {
                    if (File.Exists(target))
                    {
                        var backup = SafeCombine(Path.Combine(stateDir, "shader-original"), relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(target, backup, overwrite: true);
                        updated.OriginalFiles[relative] = Path.GetRelativePath(Path.Combine(stateDir, "shader-original"), backup);
                    }
                    else updated.OriginalFiles[relative] = null;
                }
                else if (File.Exists(target) && updated.InstalledHashes.TryGetValue(relative, out var oldHash) && !HashFile(target).Equals(oldHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Keeping your modified shader or preset file: {relative}");
                updated.InstalledHashes[relative] = HashFile(temporary);
            }
            SaveState(stateFile, updated);
            foreach (var (relative, temporary) in staged)
            {
                var target = SafeCombine(gameRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var replacement = target + ".modern-sfm-patcher-tmp";
                try { File.Copy(temporary, replacement, overwrite: true); File.Move(replacement, target, overwrite: true); }
                finally { if (File.Exists(replacement)) File.Delete(replacement); }
            }
            progress.Report($"Installed the Skibidi Kitty preset and {sources.Count - 1} supporting shader and texture files.");
        }
        finally { TryDeleteDirectory(stageDir); }
    }

    private static string ResolvePackagePath(string installPath, string tail)
    {
        var folderParts = installPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".").ToArray();
        var fileParts = tail.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (folderParts.Length < 2 || !folderParts[0].Equals("reshade-shaders", StringComparison.OrdinalIgnoreCase) ||
            folderParts.Any(part => part is "." or "..") || fileParts.Length == 0 || fileParts.Any(part => part is "." or ".."))
            throw new InvalidDataException("Shader package contains an invalid install path.");
        var parts = new List<string> { "bin" };
        foreach (var part in folderParts.Concat(fileParts))
        {
            parts.Add(part);
        }
        return string.Join('/', parts);
    }

    private sealed class InstallState
    {
        public required string GameRoot { get; init; }
        public required string BackupPath { get; init; }
        public required string? PreviousModuleOverride { get; init; }
        public Dictionary<string, string?> OriginalFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OriginalHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> InstalledHashes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public bool InstallationComplete { get; set; } = true;
    }

    public bool HasBackup(string gameRoot) => File.Exists(StateFile(gameRoot));
    public bool HasRestBackup(string gameRoot) => File.Exists(Path.Combine(StateDirectory(gameRoot), "rest.json"));
    public bool HasShaderBackup(string gameRoot) => File.Exists(Path.Combine(StateDirectory(gameRoot), "shaders.json"));
    public string? GetBackupDirectory(string gameRoot) => HasBackup(gameRoot) ? StateDirectory(gameRoot) : null;

    public bool CanInstallWithExistingBackup(string gameRoot)
    {
        if (!HasBackup(gameRoot)) return true;
        if (!IsInstallationComplete(gameRoot)) return false;
        var bin = Path.Combine(gameRoot, "bin");
        return !File.Exists(Path.Combine(bin, "d3d9.dll")) && !File.Exists(Path.Combine(bin, "dxvk", "d3d9.dll"));
    }

    public bool IsInstallationComplete(string gameRoot)
    {
        try
        {
            var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(StateFile(gameRoot)));
            return state is not null && Path.GetFullPath(state.GameRoot).Equals(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase) && state.InstallationComplete;
        }
        catch (FileNotFoundException) { return true; }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool IsSfmRunning(string gameRoot) =>
        Process.GetProcessesByName("sfm").Any(process => SamePath(process, Path.Combine(gameRoot, "sfm.exe")));

    public static string ValidateDxvkConfiguration(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new InvalidDataException("The DXVK configuration is empty.");
        if (configuration.Contains('\0'))
            throw new InvalidDataException("The DXVK configuration contains a null character.");
        if (Encoding.UTF8.GetByteCount(configuration) > 1_048_576)
            throw new InvalidDataException("DXVK configuration files must be 1 MB or smaller.");
        return configuration;
    }

    public bool HasDxvkConfigurationBackup(string gameRoot) => File.Exists(DxvkConfigStateFile(gameRoot));

    public void SaveDxvkConfiguration(string gameRoot, string configuration)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        configuration = ValidateDxvkConfiguration(configuration);
        EnsureSfmClosed(gameRoot, "saving DXVK configuration");
        var stateDir = StateDirectory(gameRoot);
        Directory.CreateDirectory(stateDir);
        using var operationLock = AcquireGraphicsOperationLock(stateDir);

        var configPath = Path.Combine(gameRoot, "dxvk.conf");
        var statePath = DxvkConfigStateFile(gameRoot);
        if (File.Exists(statePath))
        {
            var state = JsonSerializer.Deserialize<DxvkConfigState>(File.ReadAllText(statePath))
                ?? throw new InvalidDataException("The DXVK configuration backup is invalid.");
            if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The DXVK configuration backup belongs to another SFM folder.");
            if (state.OriginalExisted && !File.Exists(Path.Combine(stateDir, "dxvk.conf.original")))
                throw new FileNotFoundException("The original DXVK configuration backup is missing.", Path.Combine(stateDir, "dxvk.conf.original"));
        }
        else
        {
            var existed = File.Exists(configPath);
            if (existed)
                CopyAtomically(configPath, Path.Combine(stateDir, "dxvk.conf.original"));
            SaveState(statePath, new DxvkConfigState(gameRoot, existed));
        }

        var stagedPath = Path.Combine(stateDir, "dxvk.conf.pending");
        try
        {
            File.WriteAllText(stagedPath, configuration, new UTF8Encoding(false));
            CopyAtomically(stagedPath, configPath);
        }
        finally
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
        }
    }

    public void RestoreDxvkConfiguration(string gameRoot)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        EnsureSfmClosed(gameRoot, "restoring DXVK configuration");
        var stateDir = StateDirectory(gameRoot);
        using var operationLock = AcquireGraphicsOperationLock(stateDir);
        var statePath = DxvkConfigStateFile(gameRoot);
        if (!File.Exists(statePath))
            throw new InvalidOperationException("No saved DXVK configuration backup exists for this SFM folder.");
        var state = JsonSerializer.Deserialize<DxvkConfigState>(File.ReadAllText(statePath))
            ?? throw new InvalidDataException("The DXVK configuration backup is invalid.");
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The DXVK configuration backup belongs to another SFM folder.");

        var configPath = Path.Combine(gameRoot, "dxvk.conf");
        if (state.OriginalExisted)
        {
            var backupPath = Path.Combine(stateDir, "dxvk.conf.original");
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("The original DXVK configuration backup is missing.", backupPath);
            CopyAtomically(backupPath, configPath);
        }
        else if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        File.Delete(statePath);
        var backupPathToDelete = Path.Combine(stateDir, "dxvk.conf.original");
        if (File.Exists(backupPathToDelete)) File.Delete(backupPathToDelete);
    }

    public static string? DetectSfmInstall()
    {
        var libraryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            AddSteamLibraries(key, libraryRoots);
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            AddSteamLibraries(key, libraryRoots);
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            AddSteamLibraries(key, libraryRoots);

        var candidates = libraryRoots
            .SelectMany(library => new[]
            {
                Path.Combine(library, "steamapps", "common", "SourceFilmmaker", "game"),
                Path.Combine(library, "steamapps", "common", "SourceFilmmaker")
            })
            .Where(IsSfmInstall)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.FirstOrDefault(IsGraphicsSetupInstalled) ?? candidates.FirstOrDefault();
    }

    public static bool IsGraphicsSetupInstalled(string gameRoot)
    {
        var bin = Path.Combine(gameRoot, "bin");
        var reshadeDll = Path.Combine(bin, "d3d9.dll");
        var dxvkDll = Path.Combine(bin, "dxvk", "d3d9.dll");
        var ini = Path.Combine(bin, "ReShade.ini");
        if (!File.Exists(reshadeDll) || !IsX86Pe(reshadeDll) || !File.Exists(dxvkDll) || !IsX86Pe(dxvkDll) || !File.Exists(ini))
            return false;

        var version = FileVersionInfo.GetVersionInfo(reshadeDll);
        if (!string.Equals(version.ProductName, "ReShade", StringComparison.OrdinalIgnoreCase))
            return false;

        var lines = File.ReadAllLines(ini);
        var proxySection = false;
        var enabled = false;
        var library = false;
        foreach (var line in lines)
        {
            var value = line.Trim();
            if (value.StartsWith('['))
            {
                proxySection = value.Equals("[PROXY]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!proxySection)
                continue;
            enabled |= value.Equals("EnableProxyLibrary=1", StringComparison.OrdinalIgnoreCase);
            library |= value.Equals("ProxyLibrary=dxvk\\d3d9.dll", StringComparison.OrdinalIgnoreCase);
        }
        return enabled && library;
    }

    public static bool IsSfmInstall(string gameRoot) =>
        !string.IsNullOrWhiteSpace(gameRoot) &&
        File.Exists(Path.Combine(gameRoot, "sfm.exe")) &&
        Directory.Exists(Path.Combine(gameRoot, "bin")) &&
        IsX86Pe(Path.Combine(gameRoot, "sfm.exe"));

    private static void AddSteamLibraries(RegistryKey? key, HashSet<string> libraries)
    {
        if (key is null)
            return;

        foreach (var valueName in new[] { "SteamPath", "InstallPath" })
        {
            if (key.GetValue(valueName) is string path && Directory.Exists(path))
                libraries.Add(path.Replace('/', Path.DirectorySeparatorChar));
        }

        foreach (var root in libraries.ToArray())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
            {
                var library = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                if (Directory.Exists(library))
                    libraries.Add(library);
            }
        }
    }

    public async Task InstallAsync(string gameRoot, Release dxvkRelease, ReShadeRelease reshadeRelease, IProgress<string> progress, string? dxvkConfiguration = null, CancellationToken cancellationToken = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (dxvkConfiguration is not null)
            dxvkConfiguration = ValidateDxvkConfiguration(dxvkConfiguration);
        var stateDir = StateDirectory(gameRoot);
        Directory.CreateDirectory(stateDir);
        using (AcquireGraphicsOperationLock(stateDir))
        {
        if (HasBackup(gameRoot) && !CanInstallWithExistingBackup(gameRoot))
            throw new InvalidOperationException("This SFM installation already has an active Modern SFM Patcher backup. Restore it before installing again.");
        if (IsSfmRunning(gameRoot))
            throw new InvalidOperationException("Close Source Filmmaker before installing so its DLLs are not locked.");

        var backupPath = Path.Combine(stateDir, "original-" + Guid.NewGuid().ToString("N"));
        var stagingPath = Path.Combine(stateDir, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupPath);
        Directory.CreateDirectory(stagingPath);
        var state = new InstallState
        {
            GameRoot = gameRoot,
            BackupPath = backupPath,
            PreviousModuleOverride = Environment.GetEnvironmentVariable("RESHADE_MODULE_PATH_OVERRIDE", EnvironmentVariableTarget.User)
        };
        var stateFile = StateFile(gameRoot);

        var installationStateSaved = false;
        try
        {
            progress.Report($"Downloading DXVK {dxvkRelease.Version}…");
            var dxvkArchive = Path.Combine(stagingPath, "dxvk.tar.gz");
            await DownloadVerifiedAsync(dxvkRelease.Url, dxvkArchive, dxvkRelease.Sha256, progress, cancellationToken);
            var dxvkDll = Path.Combine(stagingPath, "dxvk-d3d9.dll");
            ExtractX86D3D9(dxvkArchive, dxvkDll);
            if (!IsX86Pe(dxvkDll))
                throw new InvalidDataException("The DXVK archive did not contain a valid x86 D3D9 DLL.");

            progress.Report($"Downloading official ReShade {reshadeRelease.Version} add-on setup…");
            var reshadeSetup = Path.Combine(stagingPath, "ReShade_Setup.exe");
            await DownloadAsync(reshadeRelease.Url, reshadeSetup, progress, cancellationToken);
            VerifyReShadeSetup(reshadeSetup, reshadeRelease.Version);

            progress.Report("Extracting ReShade’s official x86 D3D9 proxy…");
            var reshadeStage = Path.Combine(stagingPath, "reshade");
            Directory.CreateDirectory(reshadeStage);
            File.Copy(Path.Combine(gameRoot, "sfm.exe"), Path.Combine(reshadeStage, "sfm.exe"));
            await RunReShadeSetupAsync(reshadeSetup, Path.Combine(reshadeStage, "sfm.exe"), cancellationToken);
            var reshadeDll = Path.Combine(reshadeStage, "d3d9.dll");
            VerifyReShadeDll(reshadeDll, reshadeRelease.Version);

            var paths = GetTouchedPaths(dxvkConfiguration is not null);
            progress.Report("Backing up existing SFM files…");
            var originals = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var originalHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relativePath in paths)
            {
                var source = Path.Combine(gameRoot, relativePath);
                if (!File.Exists(source))
                {
                    originals[relativePath] = null;
                    continue;
                }

                var backupFile = Path.Combine(backupPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                File.Copy(source, backupFile, overwrite: true);
                originals[relativePath] = relativePath;
                originalHashes[relativePath] = HashFile(backupFile);
            }

            state = new InstallState
            {
                GameRoot = gameRoot,
                BackupPath = backupPath,
                PreviousModuleOverride = state.PreviousModuleOverride,
                OriginalFiles = originals,
                OriginalHashes = originalHashes,
                InstallationComplete = false
            };
            SaveState(stateFile, state);
            installationStateSaved = true;

            progress.Report("Installing the ReShade → DXVK chain…");
            var bin = Path.Combine(gameRoot, "bin");
            var dxvkDir = Path.Combine(bin, "dxvk");
            Directory.CreateDirectory(dxvkDir);

            PreserveConfig(gameRoot, reshadeStage, Path.Combine(bin, "ReShade.ini"), "ReShade.ini");
            PreserveConfig(gameRoot, reshadeStage, Path.Combine(bin, "ReShade2.ini"), "ReShade2.ini");
            PreserveConfig(gameRoot, reshadeStage, Path.Combine(bin, "ReShadePreset.ini"), "ReShadePreset.ini");

            CopyAtomically(reshadeDll, Path.Combine(bin, "d3d9.dll"));
            CopyAtomically(dxvkDll, Path.Combine(dxvkDir, "d3d9.dll"));
            if (dxvkConfiguration is not null)
            {
                var configurationStage = Path.Combine(stagingPath, "dxvk.conf");
                await File.WriteAllTextAsync(configurationStage, dxvkConfiguration, new UTF8Encoding(false), cancellationToken);
                CopyAtomically(configurationStage, Path.Combine(gameRoot, "dxvk.conf"));
            }
            ConfigureProxyIni(Path.Combine(bin, "ReShade.ini"));
            ConfigureSearchPaths(Path.Combine(bin, "ReShade.ini"));
            ConfigureSearchPaths(Path.Combine(bin, "ReShade2.ini"));
            SetUserModuleOverride(dxvkDir);

            foreach (var relativePath in paths)
            {
                var installedFile = Path.Combine(gameRoot, relativePath);
                if (File.Exists(installedFile))
                    state.InstalledHashes[relativePath] = HashFile(installedFile);
                SaveState(stateFile, state);
            }
            state.InstallationComplete = true;
            SaveState(stateFile, state);
            progress.Report($"Installed ReShade {reshadeRelease.Version} and DXVK {dxvkRelease.Version}.");
        }
        catch
        {
            if (installationStateSaved)
                await RestoreAsync(gameRoot, progress, CancellationToken.None);
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
        }
    }

    public async Task InstallRestAsync(string gameRoot, Release release, IProgress<string> progress, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before installing an add-on.");
        var target = Path.Combine(gameRoot, "bin", "ReshadeEffectShaderToggler.addon32");
        var statePath = Path.Combine(StateDirectory(gameRoot), "rest.json");
        var temp = Path.Combine(StateDirectory(gameRoot), "rest.zip");
        Directory.CreateDirectory(StateDirectory(gameRoot));
        try
        {
            progress.Report($"Downloading REST Enhanced {release.Version}…");
            await DownloadVerifiedAsync(release.Url, temp, release.Sha256, progress, token);
            using var zip = ZipFile.OpenRead(temp);
            var entries = zip.Entries.Where(e => Path.GetFileName(e.FullName).Equals("ReshadeEffectShaderToggler.addon32", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new InvalidDataException("REST release must contain exactly one x86 .addon32 file.");
            var extracted = temp + ".addon32";
            using (var input = entries[0].Open()) using (var output = File.Create(extracted)) await input.CopyToAsync(output, token);
            if (!IsX86Pe(extracted)) { File.Delete(extracted); throw new InvalidDataException("REST release add-on is not a valid x86 module."); }
            RestState? state = File.Exists(statePath) ? JsonSerializer.Deserialize<RestState>(await File.ReadAllTextAsync(statePath, token)) : null;
            string? backup = state?.BackupPath;
            if (state is null && File.Exists(target))
            {
                backup = Path.Combine(StateDirectory(gameRoot), "rest-original.addon32");
                File.Copy(target, backup, overwrite: true);
            }
            File.Copy(extracted, target, overwrite: true);
            File.Delete(extracted);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new RestState(gameRoot, backup, HashFile(target), release.Version), new JsonSerializerOptions { WriteIndented = true }), token);
            progress.Report($"Installed REST Enhanced {release.Version} (x86).");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); if (File.Exists(temp + ".addon32")) File.Delete(temp + ".addon32"); }
    }

    public async Task RestoreRestAsync(string gameRoot, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before restoring an add-on.");
        var statePath = Path.Combine(StateDirectory(gameRoot), "rest.json");
        if (!File.Exists(statePath)) throw new InvalidOperationException("No Modern SFM Patcher REST add-on backup exists.");
        var state = JsonSerializer.Deserialize<RestState>(await File.ReadAllTextAsync(statePath, token)) ?? throw new InvalidDataException("REST backup manifest is invalid.");
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("REST backup belongs to another SFM folder.");
        var target = Path.Combine(gameRoot, "bin", "ReshadeEffectShaderToggler.addon32");
        if (File.Exists(target) && !HashFile(target).Equals(state.InstalledHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("REST add-on changed after installation; keeping your file and backup.");
        if (state.BackupPath is null) { if (File.Exists(target)) File.Delete(target); }
        else File.Copy(state.BackupPath, target, overwrite: true);
        File.Delete(statePath);
        if (state.BackupPath is not null && File.Exists(state.BackupPath)) File.Delete(state.BackupPath);
    }

    public async Task UninstallAsync(string gameRoot, IProgress<string> progress, CancellationToken token = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        if (HasBackup(gameRoot))
        {
            await RestoreAsync(gameRoot, progress, token);
            return;
        }
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before uninstalling its graphics modules.");

        var bin = Path.Combine(gameRoot, "bin");
        var reshade = Path.Combine(bin, "d3d9.dll");
        var dxvk = Path.Combine(bin, "dxvk", "d3d9.dll");
        var ini = Path.Combine(bin, "ReShade.ini");
        if (!IsX86Pe(reshade) || !string.Equals(FileVersionInfo.GetVersionInfo(reshade).ProductName, "ReShade", StringComparison.OrdinalIgnoreCase) ||
            !IsX86Pe(dxvk) || !string.Equals(FileVersionInfo.GetVersionInfo(dxvk).ProductName, "DXVK", StringComparison.OrdinalIgnoreCase) ||
            !HasDxvkProxyConfiguration(ini))
            throw new InvalidOperationException("No managed backup or recognized ReShade → DXVK setup was found. No files were changed.");

        var backup = Path.Combine(StateDirectory(gameRoot), "uninstall-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(backup);
        var configBackup = Path.Combine(backup, "ReShade.ini");
        var reshadeBackup = Path.Combine(backup, "ReShade-d3d9.dll");
        var dxvkBackup = Path.Combine(backup, "DXVK-d3d9.dll");
        File.Copy(ini, configBackup);
        File.Copy(reshade, reshadeBackup);
        File.Copy(dxvk, dxvkBackup);
        try
        {
            RemoveDxvkProxyConfiguration(ini);
            File.Delete(reshade);
            File.Delete(dxvk);
        }
        catch
        {
            File.Copy(configBackup, ini, overwrite: true);
            if (!File.Exists(reshade)) File.Copy(reshadeBackup, reshade);
            if (!File.Exists(dxvk)) File.Copy(dxvkBackup, dxvk);
            throw;
        }
        TryDeleteEmptyDirectory(Path.GetDirectoryName(dxvk)!);
        progress.Report($"Removed ReShade and DXVK. Recovery copies are in {backup}; shaders and presets were kept.");
    }

    private static bool HasDxvkProxyConfiguration(string path)
    {
        if (!File.Exists(path)) return false;
        var section = false; var enabled = false; var library = false;
        foreach (var line in File.ReadLines(path))
        {
            var value = line.Trim();
            if (value.StartsWith('[')) { section = value.Equals("[PROXY]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!section) continue;
            enabled |= value.Equals("EnableProxyLibrary=1", StringComparison.OrdinalIgnoreCase);
            library |= value.Equals("ProxyLibrary=dxvk\\d3d9.dll", StringComparison.OrdinalIgnoreCase);
        }
        return enabled && library;
    }

    private static void RemoveDxvkProxyConfiguration(string path)
    {
        var lines = File.ReadAllLines(path).ToList(); var section = false;
        lines.RemoveAll(line =>
        {
            var value = line.Trim();
            if (value.StartsWith('[')) { section = value.Equals("[PROXY]", StringComparison.OrdinalIgnoreCase); return false; }
            return section && (value.Equals("EnableProxyLibrary=1", StringComparison.OrdinalIgnoreCase) || value.Equals("ProxyLibrary=dxvk\\d3d9.dll", StringComparison.OrdinalIgnoreCase));
        });
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public async Task RestoreAsync(string gameRoot, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        gameRoot = ValidateGameRoot(gameRoot);
        var stateFile = StateFile(gameRoot);
        if (!File.Exists(stateFile))
            throw new InvalidOperationException("No Modern SFM Patcher backup exists for this folder.");
        if (Process.GetProcessesByName("sfm").Any(p => SamePath(p, Path.Combine(gameRoot, "sfm.exe"))))
            throw new InvalidOperationException("Close Source Filmmaker before restoring files so its DLLs are not locked.");

        var state = JsonSerializer.Deserialize<InstallState>(await File.ReadAllTextAsync(stateFile, cancellationToken))
            ?? throw new InvalidDataException("The Modern SFM Patcher backup manifest is invalid.");
        if (!Path.GetFullPath(state.GameRoot).Equals(gameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The backup belongs to a different SFM folder.");

        var keptModified = new List<string>();
        foreach (var (relativePath, originalPath) in state.OriginalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SafeCombine(gameRoot, relativePath);
            var existed = originalPath is not null;
            if (File.Exists(target))
            {
                var currentHash = HashFile(target);
                if (state.InstalledHashes.TryGetValue(relativePath, out var installedHash) &&
                    !currentHash.Equals(installedHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (state.OriginalHashes.TryGetValue(relativePath, out var originalHash) &&
                        currentHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase))
                        continue;
                    keptModified.Add(relativePath);
                    continue;
                }
                if (!state.InstallationComplete && !state.InstalledHashes.ContainsKey(relativePath) &&
                    (!state.OriginalHashes.TryGetValue(relativePath, out var untouchedHash) ||
                     !currentHash.Equals(untouchedHash, StringComparison.OrdinalIgnoreCase)))
                {
                    keptModified.Add(relativePath);
                    continue;
                }
            }

            if (existed)
            {
                var backup = SafeCombine(state.BackupPath, originalPath!);
                if (!File.Exists(backup))
                    throw new FileNotFoundException("A required backup file is missing.", backup);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyAtomically(backup, target);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        var currentOverride = Environment.GetEnvironmentVariable("RESHADE_MODULE_PATH_OVERRIDE", EnvironmentVariableTarget.User);
        var installedOverride = Path.Combine(gameRoot, "bin", "dxvk");
        if (currentOverride?.Equals(installedOverride, StringComparison.OrdinalIgnoreCase) == true)
            SetUserModuleOverride(state.PreviousModuleOverride);

        TryDeleteEmptyDirectory(Path.Combine(gameRoot, "bin", "dxvk"));
        if (keptModified.Count == 0)
            File.Delete(stateFile);
        else
        {
            var remainingOriginalFiles = state.OriginalFiles
                .Where(pair => keptModified.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var remainingInstalledHashes = state.InstalledHashes
                .Where(pair => keptModified.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var remainingOriginalHashes = state.OriginalHashes
                .Where(pair => keptModified.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            state = new InstallState
            {
                GameRoot = state.GameRoot,
                BackupPath = state.BackupPath,
                PreviousModuleOverride = state.PreviousModuleOverride,
                OriginalFiles = remainingOriginalFiles,
                InstalledHashes = remainingInstalledHashes,
                OriginalHashes = remainingOriginalHashes,
                InstallationComplete = true
            };
            SaveState(stateFile, state);
        }
        progress.Report(keptModified.Count == 0
            ? "Original files and environment setting restored."
            : "Restored unchanged files. Kept your later edits to: " + string.Join(", ", keptModified));
    }

    private static string ValidateGameRoot(string path)
    {
        if (!IsSfmInstall(path))
            throw new DirectoryNotFoundException("Select the SFM game folder containing an x86 sfm.exe and a bin folder.");
        return Path.GetFullPath(path);
    }

    private static string[] GetTouchedPaths(bool includeDxvkConfiguration)
    {
        var paths = new List<string>
        {
            "bin/d3d9.dll",
            "bin/dxvk/d3d9.dll",
            "bin/ReShade.ini",
            "bin/ReShade2.ini",
            "bin/ReShadePreset.ini"
        };
        if (includeDxvkConfiguration) paths.Add("dxvk.conf");
        return [.. paths];
    }

    private static void PreserveConfig(string gameRoot, string reshadeStage, string target, string name)
    {
        if (File.Exists(target))
            return;
        var fromGame = Path.Combine(gameRoot, name);
        var fromSetup = Path.Combine(reshadeStage, name);
        var source = File.Exists(fromGame) ? fromGame : fromSetup;
        if (File.Exists(source))
            File.Copy(source, target);
    }

    private static void ConfigureProxyIni(string path)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var start = lines.FindIndex(line => line.Trim().Equals("[PROXY]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.Add("[PROXY]");
            start = lines.Count - 1;
        }
        var end = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        if (end < 0)
            end = lines.Count;
        lines.RemoveRange(start + 1, end - start - 1);
        lines.Insert(start + 1, "EnableProxyLibrary=1");
        lines.Insert(start + 2, "ProxyLibrary=dxvk\\d3d9.dll");
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void ConfigureSearchPaths(string path)
    {
        if (!File.Exists(path))
            return;
        var text = File.ReadAllText(path)
            .Replace(".\\bin\\reshade-shaders\\Shaders", ".\\reshade-shaders\\Shaders", StringComparison.OrdinalIgnoreCase)
            .Replace(".\\bin\\reshade-shaders\\Textures", ".\\reshade-shaders\\Textures", StringComparison.OrdinalIgnoreCase)
            .Replace("\\**\\**", "\\**", StringComparison.Ordinal);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static async Task DownloadVerifiedAsync(string url, string destination, string expectedHash,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read, 131072, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            received += read;
            if (total is > 0)
                progress.Report($"Downloading… {received * 100 / total.Value}%");
        }
        await output.FlushAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(expectedHash) || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded file failed its SHA-256 check: " + Path.GetFileName(destination));
    }

    private static async Task DownloadAsync(string url, string destination, IProgress<string> progress, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read, 131072, useAsync: true);
        var buffer = new byte[131072]; int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0) await output.WriteAsync(buffer.AsMemory(0, read), token);
        await output.FlushAsync(token);
    }

    private static void ExtractX86D3D9(string archivePath, string destination)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var found = false;
        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile || !entry.Name.EndsWith("/x32/d3d9.dll", StringComparison.Ordinal))
                continue;
            if (found)
                throw new InvalidDataException("The DXVK archive contains more than one x86 D3D9 DLL.");
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            entry.DataStream!.CopyTo(output);
            found = true;
        }
        if (!found)
            throw new InvalidDataException("The DXVK archive does not contain x32/d3d9.dll.");
    }

    private static void VerifyReShadeSetup(string path, string version)
    {
#pragma warning disable SYSLIB0057 // Authenticode signer extraction has no X509CertificateLoader equivalent.
        using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        if (!signer.GetCertHashString().Equals(ReshadeSignerThumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ReShade installer signature does not match the official ReShade certificate.");
    }

    private static void VerifyReShadeDll(string path, string version)
    {
        if (!File.Exists(path) || FileVersionInfo.GetVersionInfo(path).ProductName != "ReShade" ||
            !FileVersionInfo.GetVersionInfo(path).FileVersion!.StartsWith(version + ".", StringComparison.Ordinal) || !IsX86Pe(path))
            throw new InvalidDataException($"Official ReShade setup did not produce a valid 32-bit ReShade {version} D3D9 proxy.");
    }

    private static async Task RunReShadeSetupAsync(string setup, string targetExe, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(setup)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(targetExe)!
        };
        start.ArgumentList.Add(targetExe);
        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--api");
        start.ArgumentList.Add("d3d9");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start official ReShade setup.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var details = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Official ReShade setup failed. " + details.Trim());
    }

    private static bool IsX86Pe(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (reader.ReadUInt16() != 0x5A4D)
                return false;
            file.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > file.Length - 6)
                return false;
            file.Position = peOffset;
            return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x014C;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool SamePath(Process process, string path)
    {
        try { return Path.GetFullPath(process.MainModule!.FileName!).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string StateDirectory(string gameRoot) => Path.Combine(StateRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(gameRoot).ToUpperInvariant()))));
    private static string StateFile(string gameRoot) => Path.Combine(StateDirectory(gameRoot), "state.json");
    private static string DxvkConfigStateFile(string gameRoot) => Path.Combine(StateDirectory(gameRoot), "dxvk-config.json");
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static FileStream AcquireGraphicsOperationLock(string stateDir)
    {
        try
        {
            return new FileStream(Path.Combine(stateDir, "graphics-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Another Modern SFM Patcher operation is already changing files for this SFM folder. Wait for it to finish, then try again.", ex);
        }
    }

    private static void EnsureSfmClosed(string gameRoot, string operation)
    {
        if (IsSfmRunning(gameRoot))
            throw new InvalidOperationException($"Close Source Filmmaker before {operation} so its files are not in use.");
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup path escapes its expected folder.");
        return full;
    }

    private static void SaveState<T>(string path, T state)
    {
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CopyAtomically(string source, string target)
    {
        var temporary = target + ".modern-sfm-patcher-tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void SetUserModuleOverride(string? value)
    {
        using var environment = Registry.CurrentUser.OpenSubKey("Environment", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Environment", writable: true);
        if (string.IsNullOrWhiteSpace(value))
            environment.DeleteValue("RESHADE_MODULE_PATH_OVERRIDE", throwOnMissingValue: false);
        else
            environment.SetValue("RESHADE_MODULE_PATH_OVERRIDE", value, RegistryValueKind.String);
        BroadcastEnvironmentChange();
    }

    private static void BroadcastEnvironmentChange()
    {
        _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out _);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}



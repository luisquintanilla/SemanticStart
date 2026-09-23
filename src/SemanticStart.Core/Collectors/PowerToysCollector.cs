using System.Runtime.CompilerServices;
using Microsoft.Win32;
using SemanticStart.Core.Abstractions;
using SemanticStart.Core.Model;

namespace SemanticStart.Core.Collectors;

/// <summary>
/// Exposes the utilities hosted by PowerToys as individual searchable entities. PowerToys
/// registers one shell application for the suite; its utilities therefore do not appear in the
/// AppsFolder, Start menu, App Paths, or PATH independently.
/// </summary>
public sealed class PowerToysCollector : IEntityCollector
{
    private static readonly IReadOnlyList<PowerToy> Utilities =
    [
        new("Advanced Paste", "AdvancedPaste", "advanced-paste", "Paste clipboard content in another format or transform it with AI."),
        new("Always on Top", "AlwaysOnTop", "always-on-top", "Pin a window above every other window."),
        new("Awake", "Awake", "awake", "Keep the computer awake without changing its power settings."),
        new("Color Picker", "ColorPicker", "color-picker", "Pick a color from anywhere on the screen and copy it in a configurable format."),
        new("Command Not Found", "CommandNotFound", "cmd-not-found", "Suggest a WinGet package when a PowerShell command is not installed."),
        new("Command Palette", "CmdPal", "command-palette/overview", "Search, launch, and control applications and commands from a customizable palette."),
        new("Crop And Lock", "CropAndLock", "crop-and-lock", "Create a cropped view or interactive thumbnail of another window."),
        new("Cursor Wrap", "MouseUtils", "mouse-utilities", "Wrap the mouse pointer across screen edges."),
        new("Environment Variables", "EnvironmentVariables", "environment-variables", "Create and manage user and system environment variable profiles."),
        new("FancyZones", "FancyZones", "fancyzones", "Create window layouts and quickly arrange windows into zones."),
        new("File Explorer add-ons", "FileExplorer", "file-explorer", "Add previews and thumbnails for developer and document file formats."),
        new("File Locksmith", "FileLocksmith", "file-locksmith", "Find which processes are using a file or directory."),
        new("Hosts File Editor", "Hosts", "hosts-file-editor", "Edit the Windows hosts file."),
        new("Image Resizer", "ImageResizer", "image-resizer", "Resize one or more images from File Explorer."),
        new("Keyboard Manager", "KeyboardManager", "keyboard-manager", "Remap keys and keyboard shortcuts."),
        new("Light Switch", "LightSwitch", "light-switch", "Switch Windows between light and dark themes on a schedule."),
        new("Find My Mouse", "MouseUtils", "mouse-utilities", "Locate the mouse pointer with a visual spotlight."),
        new("Mouse Highlighter", "MouseUtils", "mouse-utilities", "Show visual indicators when mouse buttons are clicked."),
        new("Mouse Jump", "MouseUtils", "mouse-utilities", "Move the pointer long distances using a screen preview."),
        new("Mouse Pointer Crosshairs", "MouseUtils", "mouse-utilities", "Draw crosshairs centered on the mouse pointer."),
        new("Mouse Without Borders", "MouseWithoutBorders", "mouse-without-borders", "Control multiple computers with one keyboard and mouse."),
        new("New+", "NewPlus", "newplus", "Create files and folders from personalized templates."),
        new("Peek", "Peek", "peek", "Preview a file without opening its application."),
        new("PowerRename", "PowerRename", "powerrename", "Rename many files with search, replacement, and regular expressions."),
        new("PowerToys Run", "PowerLauncher", "run", "Quickly search for and launch applications, files, folders, and commands."),
        new("Quick Accent", "QuickAccent", "quick-accent", "Type accented characters from a popup selector."),
        new("Registry Preview", "RegistryPreview", "registry-preview", "Preview and edit Windows Registry files."),
        new("Screen Ruler", "MeasureTool", "screen-ruler", "Measure pixels on the screen using image edge detection."),
        new("Shortcut Guide", "ShortcutGuide", "shortcut-guide", "Show an overlay of available Windows-key shortcuts."),
        new("Text Extractor", "PowerOcr", "text-extractor", "Copy text from anywhere on the screen using optical character recognition."),
        new("Workspaces", "Workspaces", "workspaces", "Launch applications into saved desktop layouts."),
        new("ZoomIt", "ZoomIt", "zoomit", "Zoom, annotate, and record the screen during presentations."),
    ];

    private readonly Func<string?> _resolveRunner;

    public PowerToysCollector()
        : this(ResolveRunner)
    {
    }

    internal PowerToysCollector(Func<string?> resolveRunner) =>
        _resolveRunner = resolveRunner ?? throw new ArgumentNullException(nameof(resolveRunner));

    public string Source => "powertoys";

    public bool IsSupported => OperatingSystem.IsWindows() && TryResolveRunner() is not null;

    public async IAsyncEnumerable<Entity> CollectAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        if (!OperatingSystem.IsWindows() || TryResolveRunner() is not { } runner)
            yield break;

        foreach (var utility in Utilities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var learnArticle = $"https://learn.microsoft.com/en-us/windows/powertoys/{utility.LearnPath}";
            var entity = new Entity
            {
                Id = EntityId.Create(Source, utility.DisplayName),
                Kind = EntityKind.Application,
                DisplayName = utility.DisplayName,
                LaunchKind = LaunchKind.Executable,
                LaunchTarget = runner,
                LaunchArguments = $"--open-settings={utility.SettingsPage}",
                IconSource = runner,
                Publisher = "Microsoft Corporation",
                Source = Source,
                RawMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["description"] = utility.Description,
                    ["fileName"] = Path.GetFileName(runner),
                    ["learnArticle"] = learnArticle,
                    ["settingsPage"] = utility.SettingsPage,
                    ["sharedLaunchTarget"] = "true",
                    ["suite"] = "Microsoft PowerToys",
                    ["targetPath"] = runner,
                },
            };

            yield return CollectorEntity.WithContentHash(entity);
        }
    }

    private string? TryResolveRunner()
    {
        try
        {
            var path = _resolveRunner();
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ResolveRunner()
    {
        foreach (var path in RegistryCandidates().Concat(KnownLocationCandidates()))
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    private static IEnumerable<string?> RegistryCandidates()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? appPath = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    appPath = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\PowerToys.exe");
                    if (appPath?.GetValue(null) is string registered)
                        yield return registered.Trim().Trim('"');
                }
                finally
                {
                    appPath?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    private static IEnumerable<string> KnownLocationCandidates()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            yield return Path.Combine(local, "PowerToys", "PowerToys.exe");

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(folder, "PowerToys", "PowerToys.exe");
        }
    }

    private sealed record PowerToy(string DisplayName, string SettingsPage, string LearnPath, string Description);
}

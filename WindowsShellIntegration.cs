using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

internal static class WindowsShellIntegration
{
    private const string DirectoryShellKey = @"Software\Classes\Directory\shell\GitCopy";
    private const string DirectoryBackgroundShellKey = @"Software\Classes\Directory\Background\shell\GitCopy";
    private const string MenuLabel = "Copy with GitCopy...";

    public static int Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows Explorer integration is only available on Windows.");
            return 1;
        }

        try
        {
            return InstallWindows();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to install Windows Explorer integration: {ex.Message}");
            return 1;
        }
    }

    public static int Uninstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows Explorer integration is only available on Windows.");
            return 1;
        }

        try
        {
            return UninstallWindows();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to remove Windows Explorer integration: {ex.Message}");
            return 1;
        }
    }

    public static async Task<string?> SelectDestinationParentAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows destination picker is only available on Windows.");
        }

        return await SelectDestinationParentWindowsAsync();
    }

    [SupportedOSPlatform("windows")]
    private static int InstallWindows()
    {
        string sourceExecutable = ResolveCurrentExecutable();
        string installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitCopy");
        string installedExecutable = Path.Combine(installDirectory, "GitCopy.exe");

        Directory.CreateDirectory(installDirectory);

        if (!string.Equals(
                Path.GetFullPath(sourceExecutable),
                Path.GetFullPath(installedExecutable),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceExecutable, installedExecutable, overwrite: true);
        }

        RegisterShellVerb(DirectoryShellKey, installedExecutable, "%1");
        RegisterShellVerb(DirectoryBackgroundShellKey, installedExecutable, "%V");

        Console.WriteLine("GitCopy Windows Explorer integration installed.");
        Console.WriteLine($"Executable: {installedExecutable}");
        Console.WriteLine("Right-click a folder, or right-click empty space inside a folder, and choose 'Copy with GitCopy...'.");
        Console.WriteLine("On Windows 11 the entry may appear under 'Show more options'.");

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static int UninstallWindows()
    {
        Registry.CurrentUser.DeleteSubKeyTree(DirectoryShellKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(DirectoryBackgroundShellKey, throwOnMissingSubKey: false);

        Console.WriteLine("GitCopy Windows Explorer context-menu entries removed.");
        Console.WriteLine("The cached executable under %LOCALAPPDATA%\GitCopy is intentionally left in place and may be deleted manually.");

        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterShellVerb(string registryPath, string executablePath, string sourceToken)
    {
        using RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(registryPath)
            ?? throw new InvalidOperationException($"Unable to create registry key HKCU\\{registryPath}.");

        shellKey.SetValue(string.Empty, MenuLabel, RegistryValueKind.String);
        shellKey.SetValue("Icon", executablePath, RegistryValueKind.String);
        shellKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);

        using RegistryKey commandKey = shellKey.CreateSubKey("command")
            ?? throw new InvalidOperationException($"Unable to create command registry key HKCU\\{registryPath}\\command.");

        commandKey.SetValue(
            string.Empty,
            $"\"{executablePath}\" --shell-copy \"{sourceToken}\"",
            RegistryValueKind.String);
    }

    private static string ResolveCurrentExecutable()
    {
        string? processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to determine the current GitCopy executable path.");
        }

        string fileName = Path.GetFileName(processPath);
        if (fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            string appHost = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "GitCopy.exe" : "GitCopy");
            if (File.Exists(appHost))
            {
                return appHost;
            }
        }

        if (!File.Exists(processPath))
        {
            throw new FileNotFoundException("The current GitCopy executable could not be found.", processPath);
        }

        return processPath;
    }

    [SupportedOSPlatform("windows")]
    private static async Task<string?> SelectDestinationParentWindowsAsync()
    {
        const string script = "$shell = New-Object -ComObject Shell.Application; "
            + "$folder = $shell.BrowseForFolder(0, 'GitCopy: Select the destination parent folder', 1, 0); "
            + "if ($null -ne $folder) { [Console]::Out.Write($folder.Self.Path) }";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-STA");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string error = (await stderr).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"powershell.exe exited with code {process.ExitCode}."
                    : error);
        }

        string selectedPath = (await stdout).Trim();
        return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
    }
}

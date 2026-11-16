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
        Console.WriteLine("The cached executable under %LOCALAPPDATA%\\GitCopy is intentionally left in place and may be deleted manually.");

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

    /// <summary>Remember a successfully used parent directory without affecting copy results.</summary>
    public static void RememberDestinationParent(string destinationParent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string selectedPath = Path.GetFullPath(destinationParent);
            if (!Directory.Exists(selectedPath))
            {
                return;
            }

            string historyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GitCopy", "recent-destinations.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);

            List<string> recent = [selectedPath];
            if (File.Exists(historyPath))
            {
                foreach (string previous in File.ReadAllLines(historyPath))
                {
                    if (recent.Count >= 10)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(previous) || !Path.IsPathFullyQualified(previous))
                    {
                        continue;
                    }

                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(previous);
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        continue;
                    }

                    if (Directory.Exists(fullPath) && !recent.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    {
                        recent.Add(fullPath);
                    }
                }
            }

            // Write-then-rename prevents interrupted writes from truncating the history.
            string temporaryPath = historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporaryPath, recent, new UTF8Encoding(false));
                File.Move(temporaryPath, historyPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Warning: Could not save GitCopy destination history: {ex.Message}");
        }
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
        const string script = """
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            [System.Windows.Forms.Application]::EnableVisualStyles()

            $form = New-Object System.Windows.Forms.Form
            $form.Text = 'GitCopy - Choose destination folder'
            $form.ClientSize = [System.Drawing.Size]::new(760, 420)
            $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
            $form.MaximizeBox = $false
            $form.MinimizeBox = $false
            $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
            $form.TopMost = $true

            $intro = New-Object System.Windows.Forms.Label
            $intro.Text = 'Select a previously used destination parent folder, or browse for a new one.'
            $intro.AutoSize = $true
            $intro.Location = [System.Drawing.Point]::new(18, 18)
            [void]$form.Controls.Add($intro)

            $heading = New-Object System.Windows.Forms.Label
            $heading.Text = 'Recent destination folders (most recent first)'
            $heading.AutoSize = $true
            $heading.Location = [System.Drawing.Point]::new(18, 45)
            [void]$form.Controls.Add($heading)

            $recentList = New-Object System.Windows.Forms.ListBox
            $recentList.Location = [System.Drawing.Point]::new(18, 66)
            $recentList.Size = [System.Drawing.Size]::new(724, 270)
            $recentList.HorizontalScrollbar = $true
            [void]$form.Controls.Add($recentList)

            $historyPath = Join-Path (Join-Path $env:LOCALAPPDATA 'GitCopy') 'recent-destinations.txt'
            if ([System.IO.File]::Exists($historyPath)) {
                try {
                    foreach ($folder in [System.IO.File]::ReadAllLines($historyPath)) {
                        if (-not [string]::IsNullOrWhiteSpace($folder) -and [System.IO.Directory]::Exists($folder)) {
                            [void]$recentList.Items.Add($folder)
                        }
                    }
                } catch {
                    # An unreadable history file must not prevent choosing a folder.
                }
            }

            $useButton = New-Object System.Windows.Forms.Button
            $useButton.Text = 'Use selected folder'
            $useButton.Location = [System.Drawing.Point]::new(18, 357)
            $useButton.Size = [System.Drawing.Size]::new(165, 32)
            $useButton.Enabled = $false
            [void]$form.Controls.Add($useButton)

            $browseButton = New-Object System.Windows.Forms.Button
            $browseButton.Text = 'Browse...'
            $browseButton.Location = [System.Drawing.Point]::new(195, 357)
            $browseButton.Size = [System.Drawing.Size]::new(115, 32)
            [void]$form.Controls.Add($browseButton)

            $clearButton = New-Object System.Windows.Forms.Button
            $clearButton.Text = 'Clear history'
            $clearButton.Location = [System.Drawing.Point]::new(322, 357)
            $clearButton.Size = [System.Drawing.Size]::new(115, 32)
            $clearButton.Enabled = $recentList.Items.Count -gt 0
            [void]$form.Controls.Add($clearButton)

            $cancelButton = New-Object System.Windows.Forms.Button
            $cancelButton.Text = 'Cancel'
            $cancelButton.Location = [System.Drawing.Point]::new(627, 357)
            $cancelButton.Size = [System.Drawing.Size]::new(115, 32)
            [void]$form.Controls.Add($cancelButton)

            $script:selectedPath = $null
            $recentList.Add_SelectedIndexChanged({
                $useButton.Enabled = $null -ne $recentList.SelectedItem
            })
            $useButton.Add_Click({
                if ($null -ne $recentList.SelectedItem) {
                    $script:selectedPath = [string]$recentList.SelectedItem
                    $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
                    $form.Close()
                }
            })
            $recentList.Add_DoubleClick({ $useButton.PerformClick() })
            $browseButton.Add_Click({
                $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
                $dialog.Description = 'Select the destination parent folder'
                $dialog.ShowNewFolderButton = $true
                if ($null -ne $recentList.SelectedItem) {
                    $dialog.SelectedPath = [string]$recentList.SelectedItem
                }
                try {
                    if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
                        $script:selectedPath = $dialog.SelectedPath
                        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
                        $form.Close()
                    }
                } finally {
                    $dialog.Dispose()
                }
            })
            $clearButton.Add_Click({
                try {
                    if ([System.IO.File]::Exists($historyPath)) {
                        [System.IO.File]::Delete($historyPath)
                    }
                    $recentList.Items.Clear()
                    $useButton.Enabled = $false
                    $clearButton.Enabled = $false
                } catch {
                    [void][System.Windows.Forms.MessageBox]::Show(
                        $_.Exception.Message, 'GitCopy: Unable to clear history',
                        [System.Windows.Forms.MessageBoxButtons]::OK,
                        [System.Windows.Forms.MessageBoxIcon]::Warning)
                }
            })
            $cancelButton.Add_Click({
                $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
                $form.Close()
            })
            $form.AcceptButton = $useButton
            $form.CancelButton = $cancelButton

            try {
                $result = $form.ShowDialog()
                if ($result -eq [System.Windows.Forms.DialogResult]::OK -and $script:selectedPath) {
                    [Console]::Out.Write($script:selectedPath)
                }
            } finally {
                $form.Dispose()
            }
            """;

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

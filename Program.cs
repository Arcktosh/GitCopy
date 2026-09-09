using System.Diagnostics;
using System.Text;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return args.Length < 2 ? 1 : 0;
        }

        string sourceArgument = Path.GetFullPath(args[0]);
        string destination = Path.GetFullPath(args[1]);

        bool dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        bool clean = args.Contains("--clean", StringComparer.OrdinalIgnoreCase);

        string[] knownOptions = ["--dry-run", "--clean", "--help", "-h"];
        string? unknownOption = args.Skip(2).FirstOrDefault(arg => arg.StartsWith('-') && !knownOptions.Contains(arg, StringComparer.OrdinalIgnoreCase));

        if (unknownOption is not null)
        {
            Console.Error.WriteLine($"Unknown option: {unknownOption}");
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }

        if (!Directory.Exists(sourceArgument))
        {
            Console.Error.WriteLine("Source directory does not exist:");
            Console.Error.WriteLine(sourceArgument);
            return 1;
        }

        if (!await GitIsAvailable())
        {
            Console.Error.WriteLine("Git could not be found. Install Git for Windows and ensure git.exe is on PATH.");
            return 1;
        }

        string? repositoryRoot = await GetRepositoryRoot(sourceArgument);

        if (repositoryRoot is null)
        {
            Console.Error.WriteLine("The source is not inside a Git repository:");
            Console.Error.WriteLine(sourceArgument);
            return 1;
        }

        repositoryRoot = Path.GetFullPath(repositoryRoot);

        if (PathsOverlap(repositoryRoot, destination))
        {
            Console.Error.WriteLine("The source and destination directories may not overlap.");
            Console.Error.WriteLine($"Source:      {repositoryRoot}");
            Console.Error.WriteLine($"Destination: {destination}");
            return 1;
        }

        if (clean && IsFileSystemRoot(destination))
        {
            Console.Error.WriteLine("Refusing to clean a filesystem root directory.");
            Console.Error.WriteLine($"Destination: {destination}");
            return 1;
        }

        Console.WriteLine("GitCopy");
        Console.WriteLine("-------");
        Console.WriteLine($"Source      : {repositoryRoot}");
        Console.WriteLine($"Destination : {destination}");
        Console.WriteLine($"Dry run     : {dryRun}");
        Console.WriteLine($"Clean       : {clean}");
        Console.WriteLine();

        IReadOnlyList<string> files;

        try
        {
            files = await GetFilesToCopy(repositoryRoot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unable to query Git: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Git selected {files.Count:N0} files.");
        Console.WriteLine();

        if (clean && Directory.Exists(destination))
        {
            if (dryRun)
            {
                Console.WriteLine($"[DRY RUN] Delete: {destination}");
            }
            else
            {
                Console.WriteLine($"Cleaning destination: {destination}");
                Directory.Delete(destination, recursive: true);
            }
        }

        long totalBytes = 0;
        int copiedFiles = 0;
        int skippedFiles = 0;
        int failedFiles = 0;

        foreach (string gitPath in files)
        {
            string relativePath = gitPath.Replace('/', Path.DirectorySeparatorChar);
            string sourceFile = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
            string destinationFile = Path.GetFullPath(Path.Combine(destination, relativePath));

            if (!IsInside(repositoryRoot, sourceFile))
            {
                Console.Error.WriteLine($"[SKIP] Unsafe source path: {gitPath}");
                skippedFiles++;
                continue;
            }

            if (!IsInside(destination, destinationFile))
            {
                Console.Error.WriteLine($"[SKIP] Unsafe destination path: {gitPath}");
                skippedFiles++;
                continue;
            }

            if (!File.Exists(sourceFile))
            {
                // Git can return special entries such as submodule paths. They are not normal files.
                Console.WriteLine($"[SKIP] {gitPath}");
                skippedFiles++;
                continue;
            }

            var info = new FileInfo(sourceFile);
            totalBytes += info.Length;

            if (dryRun)
            {
                Console.WriteLine($"[COPY] {gitPath}");
                copiedFiles++;
                continue;
            }

            try
            {
                string? destinationDirectory = Path.GetDirectoryName(destinationFile);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourceFile, destinationFile, overwrite: true);
                copiedFiles++;

                if (copiedFiles % 100 == 0)
                {
                    Console.Write($"\rCopied {copiedFiles:N0}/{files.Count:N0} files...");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"[ERROR] {gitPath}: {ex.Message}");
                failedFiles++;
            }
        }

        if (!dryRun)
        {
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("Complete");
        Console.WriteLine("--------");
        Console.WriteLine($"Copied  : {copiedFiles:N0}");
        Console.WriteLine($"Skipped : {skippedFiles:N0}");
        Console.WriteLine($"Failed  : {failedFiles:N0}");
        Console.WriteLine($"Size    : {FormatBytes(totalBytes)}");

        return failedFiles == 0 ? 0 : 2;
    }

    private static async Task<bool> GitIsAvailable()
    {
        try
        {
            GitResult result = await RunGit(Environment.CurrentDirectory, ["--version"]);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> GetRepositoryRoot(string directory)
    {
        GitResult result = await RunGit(directory, ["rev-parse", "--show-toplevel"]);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string root = result.StandardOutput.Trim();
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private static async Task<IReadOnlyList<string>> GetFilesToCopy(string repositoryRoot)
    {
        GitResult result = await RunGit(
            repositoryRoot,
            ["ls-files", "-z", "--cached", "--others", "--exclude-standard"]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError.Trim());
        }

        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<GitResult> RunGit(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new GitResult(process.ExitCode, await stdout, await stderr);
    }

    private static bool IsInside(string parent, string child)
    {
        string relative = Path.GetRelativePath(parent, child);

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool PathsOverlap(string source, string destination)
    {
        source = NormalizeDirectoryPath(source);
        destination = NormalizeDirectoryPath(destination);

        return string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
            || IsInside(source, destination)
            || IsInside(destination, source);
    }

    private static bool IsFileSystemRoot(string path)
    {
        string normalized = NormalizeDirectoryPath(path);
        string? root = Path.GetPathRoot(normalized);

        return root is not null
            && string.Equals(normalized, NormalizeDirectoryPath(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (root is not null && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            GitCopy

            Copies a Git working tree while respecting Git ignore rules.

            Usage:
              GitCopy <source> <destination> [options]

            Options:
              --dry-run    Display files without copying them
              --clean      Delete the destination before copying
              --help, -h   Show this help text

            Examples:
              GitCopy C:\Repos\RepoName D:\Copies\RepoName
              GitCopy C:\Repos\RepoName D:\Copies\RepoName --clean
              GitCopy C:\Repos\RepoName D:\Copies\RepoName --dry-run
            """);
    }

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}

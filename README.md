# GitCopy

GitCopy is a .NET 8 repository-copy engine with a Visual Studio Code extension for copying a Git working tree to another location while respecting Git ignore rules.

Rather than implementing `.gitignore` parsing itself, GitCopy asks Git for the effective file set using:

```powershell
git ls-files -z --cached --others --exclude-standard
```

This means GitCopy respects tracked files, root and nested `.gitignore` files, negation rules such as `!important.log`, `.git/info/exclude`, global Git ignore configuration, and Git's own interpretation of ignored files. The `.git` directory itself is not copied.

## Components

- **GitCopy CLI** - the .NET 8 copy engine.
- **Windows Explorer integration** - optional per-user context-menu entries for folders and folder backgrounds.
- **VS Code extension** - commands, Explorer integration, destination selection, progress, and settings around the same engine.

## Requirements

- Windows 10/11 or another platform supported by .NET 8 for the CLI
- Git installed and available on `PATH`
- .NET 8 SDK for building the CLI from source
- Native AOT prerequisites for the target platform when publishing a release build
- Node.js 22+ for building or packaging the VS Code extension

## Build the CLI

```powershell
dotnet build -c Release
```

## Publish the optimized Windows executable

Release publishing uses .NET Native AOT with size optimization. The resulting `GitCopy.exe` is self-contained and does not require the .NET runtime to be installed on the target machine.

```powershell
dotnet publish -c Release -r win-x64
```

The executable is produced under:

```text
bin\Release\net8.0\win-x64\publish\GitCopy.exe
```

## CLI usage

```powershell
GitCopy <source> <destination> [options]
```

### Options

```text
--dry-run                 Display the files that would be copied without writing them
--clean                   Delete the destination before copying
--install-context-menu    Install per-user Windows Explorer context-menu integration
--uninstall-context-menu  Remove per-user Windows Explorer context-menu integration
--help, -h                Show help
```

### Examples

```powershell
GitCopy C:\Repos\RepoName D:\Copies\RepoName
GitCopy C:\Repos\RepoName D:\Copies\RepoName --clean
GitCopy C:\Repos\RepoName D:\Copies\RepoName --dry-run
```

## Windows Explorer integration

On Windows, install the shell integration once:

```powershell
GitCopy.exe --install-context-menu
```

GitCopy copies the current executable to:

```text
%LOCALAPPDATA%\GitCopy\GitCopy.exe
```

and registers per-user Explorer verbs under `HKCU`, so administrator rights are not required.

After installation, **Copy with GitCopy...** is available when:

- right-clicking a folder; or
- right-clicking empty space inside a folder.

The command opens a destination-folder picker and copies the selected Git repository into `<destination>/<repository-name>`. On Windows 11 the classic shell entry may appear under **Show more options**.

Remove the menu entries with:

```powershell
GitCopy.exe --uninstall-context-menu
```

The cached executable under `%LOCALAPPDATA%\GitCopy` is intentionally left in place so an uninstall never attempts to delete a running executable; it can be removed manually afterward.

## Visual Studio Code extension

The extension lives in [`vscode-extension`](vscode-extension) and exposes:

- `GitCopy: Copy Repository`
- `GitCopy: Copy Repository (Clean Destination)`
- `GitCopy: Preview Repository Copy`
- `GitCopy: Copy Workspace Repository`
- a **GitCopy** submenu when right-clicking folders in the Explorer
- **Copy Workspace Repository** when right-clicking empty space in the VS Code File Explorer

For a packaged Windows VSIX, the build publishes the optimized `win-x64` Native AOT engine and bundles `GitCopy.exe` inside the extension. The extension invokes that executable rather than duplicating `.gitignore` parsing logic in TypeScript.

### Build the extension

```powershell
cd vscode-extension
npm install
npm run compile
```

### Package a Windows VSIX locally

From the repository root:

```powershell
dotnet publish -c Release -r win-x64 -o artifacts\gitcopy\win-x64
New-Item -ItemType Directory -Force vscode-extension\bin | Out-Null
Copy-Item artifacts\gitcopy\win-x64\GitCopy.exe vscode-extension\bin\GitCopy.exe
cd vscode-extension
npm install
npm run package:vsix -- --out ..\artifacts\gitcopy-vscode.vsix
```

The VSIX excludes TypeScript source maps and development-only files. GitHub Actions performs the same process, reports the final executable and VSIX sizes, and uploads both artifacts.

See [`vscode-extension/README.md`](vscode-extension/README.md) for extension settings and usage.

## Behavior

GitCopy resolves the repository root from the supplied source path. If the source points to a subdirectory of a repository, the entire repository working tree is copied.

Tracked files are copied even if they later match an ignore rule because `.gitignore` does not untrack files that are already tracked.

The tool refuses to copy when the source and destination overlap. It also refuses to clean a filesystem root directory.

Files that cannot be copied because of I/O or access errors are reported individually. A non-zero exit code is returned when failures occur.

## Exit codes

- `0` - success
- `1` - invalid input, Git unavailable, shell integration failure, or repository discovery/query failure
- `2` - one or more files failed to copy

## License

MIT. See [LICENSE](LICENSE).

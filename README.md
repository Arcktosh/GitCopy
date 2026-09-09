# GitCopy

GitCopy is a .NET 8 repository-copy engine with a Visual Studio Code extension for copying a Git working tree to another location while respecting Git ignore rules.

Rather than implementing `.gitignore` parsing itself, GitCopy asks Git for the effective file set using:

```powershell
git ls-files -z --cached --others --exclude-standard
```

This means GitCopy respects:

- tracked files
- root and nested `.gitignore` files
- negation rules such as `!important.log`
- `.git/info/exclude`
- global Git ignore configuration
- Git's own interpretation of ignored files

The `.git` directory itself is not copied.

## Components

- **GitCopy CLI** - the .NET 8 copy engine.
- **VS Code extension** - commands, Explorer integration, destination selection, progress, and settings around the same engine.

## Requirements

- Windows 10/11 or another platform supported by .NET 8 for the CLI
- Git installed and available on `PATH`
- .NET 8 SDK for building the CLI from source
- Node.js 22+ for building or packaging the VS Code extension

## Build the CLI

```powershell
dotnet build -c Release
```

## Publish a single Windows executable

For 64-bit Windows:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be produced under:

```text
bin\Release\net8.0\win-x64\publish\GitCopy.exe
```

## CLI usage

```powershell
GitCopy <source> <destination> [options]
```

### Options

```text
--dry-run    Display the files that would be copied without writing them
--clean      Delete the destination before copying
--help, -h   Show help
```

### Examples

Copy a repository working tree:

```powershell
GitCopy C:\Repos\Diamond D:\Copies\Diamond
```

Recreate the destination first:

```powershell
GitCopy C:\Repos\Diamond D:\Copies\Diamond --clean
```

Preview the copy:

```powershell
GitCopy C:\Repos\Diamond D:\Copies\Diamond --dry-run
```

## Visual Studio Code extension

The extension lives in [`vscode-extension`](vscode-extension) and exposes:

- `GitCopy: Copy Repository`
- `GitCopy: Copy Repository (Clean Destination)`
- `GitCopy: Preview Repository Copy`
- a **GitCopy** submenu when right-clicking folders in the Explorer

For a packaged Windows VSIX, the build publishes the `win-x64` .NET engine and bundles `GitCopy.exe` inside the extension. The extension invokes that executable rather than duplicating `.gitignore` parsing logic in TypeScript.

### Build the extension

```powershell
cd vscode-extension
npm install
npm run compile
```

### Package a Windows VSIX locally

From the repository root:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts\gitcopy\win-x64
New-Item -ItemType Directory -Force vscode-extension\bin | Out-Null
Copy-Item artifacts\gitcopy\win-x64\GitCopy.exe vscode-extension\bin\GitCopy.exe
cd vscode-extension
npm install
npm run package:vsix -- --out ..\artifacts\gitcopy-vscode.vsix
```

The GitHub Actions build performs the same process and uploads both the standalone `GitCopy.exe` and packaged VSIX as artifacts.

See [`vscode-extension/README.md`](vscode-extension/README.md) for extension settings and usage.

## Behavior

GitCopy resolves the repository root from the supplied source path. If the source points to a subdirectory of a repository, the entire repository working tree is copied.

Tracked files are copied even if they later match an ignore rule because `.gitignore` does not untrack files that are already tracked.

The tool refuses to copy when the source and destination overlap. It also refuses to clean a filesystem root directory.

Files that cannot be copied because of I/O or access errors are reported individually. A non-zero exit code is returned when failures occur.

## Exit codes

- `0` - success
- `1` - invalid input, Git unavailable, or repository discovery/query failure
- `2` - one or more files failed to copy

## License

MIT. See [LICENSE](LICENSE).

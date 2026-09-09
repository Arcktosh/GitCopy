# GitCopy

GitCopy is a small .NET 8 command-line utility for copying a Git working tree to another location while respecting Git ignore rules.

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

## Requirements

- Windows 10/11 or another platform supported by .NET 8
- Git installed and available on `PATH`
- .NET 8 SDK for building from source

## Build

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

## Usage

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

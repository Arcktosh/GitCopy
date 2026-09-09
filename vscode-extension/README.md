# GitCopy for Visual Studio Code

GitCopy adds repository-copy commands to Visual Studio Code and delegates the actual copy operation to the GitCopy .NET engine.

The engine asks Git for the effective working-tree file set, so copies respect tracked files, root and nested `.gitignore` files, negation rules, `.git/info/exclude`, and global Git excludes. The `.git` directory itself is not copied.

## Commands

Open the Command Palette and run:

- `GitCopy: Copy Repository`
- `GitCopy: Copy Repository (Clean Destination)`
- `GitCopy: Preview Repository Copy`
- `GitCopy: Copy Workspace Repository`
- `GitCopy: Open Settings`

You can also:

- right-click a folder in the Explorer and use the **GitCopy** submenu; or
- right-click empty space in the File Explorer and choose **Copy Workspace Repository**.

The empty-space command uses the current workspace folder. If the workspace contains multiple folders, GitCopy asks which workspace folder to copy.

When no default destination root is configured, GitCopy asks you to select a parent destination folder and creates the copy under `<destination>/<repository-name>`.

## Settings

- `gitCopy.executablePath` - optional custom path to `GitCopy.exe`; otherwise the extension uses its bundled Windows executable and then falls back to `GitCopy.exe` on `PATH`.
- `gitCopy.defaultDestinationRoot` - optional parent directory used without prompting.
- `gitCopy.confirmClean` - confirms before an existing destination repository folder is deleted by a clean copy.
- `gitCopy.revealDestination` - reveals the copied repository in the operating system after success.

## Requirements

- Windows 10/11 for the bundled `win-x64` engine.
- Git installed and available on `PATH`.

On another platform, configure `gitCopy.executablePath` to a compatible GitCopy build.

## Development

From `vscode-extension`:

```powershell
npm install
npm run compile
```

Before running or packaging the extension with its bundled engine, publish the optimized Native AOT GitCopy executable and copy it into `vscode-extension/bin/GitCopy.exe`:

```powershell
dotnet publish ..\GitCopy.csproj -c Release -r win-x64 -o ..\artifacts\gitcopy\win-x64
New-Item -ItemType Directory -Force .\bin | Out-Null
Copy-Item ..\artifacts\gitcopy\win-x64\GitCopy.exe .\bin\GitCopy.exe
npm run package:vsix
```

Native AOT keeps the bundled engine self-contained while avoiding the much larger managed self-contained runtime bundle. Source maps and development-only files are excluded from the packaged VSIX.

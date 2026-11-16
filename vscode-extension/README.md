# GitCopy for Visual Studio Code

GitCopy adds repository-copy commands to Visual Studio Code and delegates the actual copy operation to the GitCopy .NET engine.

The engine asks Git for the effective working-tree file set, so copies respect tracked files, root and nested `.gitignore` files, negation rules, `.git/info/exclude`, and global Git excludes. The `.git` directory itself is not copied.

## Install the packaged extension

GitCopy provides **two separate integrations**: this **Visual Studio Code extension** (installed from a `.vsix`) and the optional **Windows Explorer context menu** (installed through `GitCopy.exe --install-context-menu`).

The `.vsix` created by this project is **not compatible with Visual Studio 2022**. Do not double-click the file: Windows may start the Visual Studio VSIX Installer and return `NoApplicableSKUsException`.

After packaging, from the **repository root**, install the extension into **Visual Studio Code**:

```powershell
code --install-extension .\artifacts\gitcopy-vscode.vsix --force
code --list-extensions --show-versions | Select-String 'arcktosh.git-copy'
```

Or open **Visual Studio Code** > **Extensions** (`Ctrl+Shift+X`) > **...** > **Install from VSIX...** and select `artifacts\gitcopy-vscode.vsix`. Reload VS Code if needed.

For the separate Windows File Explorer right-click menu, follow the [Windows Explorer integration instructions](../README.md#windows-explorer-integration).

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

When no default destination root is configured, GitCopy offers the 10 most recently used destination parent directories in a searchable picker, with options to **Browse for another folder** or **Clear recent destinations**. The repository copy is created under `<destination>/<repository-name>`. Missing directories are omitted from the picker. Only successful copies are recorded; failed or cancelled copies and previews do not change the history. On Windows, the extension shares history with the Explorer context-menu picker in `%LOCALAPPDATA%\GitCopy\recent-destinations.txt`. On other platforms, history is stored in VS Code extension global state.

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

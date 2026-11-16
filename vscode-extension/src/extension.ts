import * as vscode from 'vscode';
import { spawn } from 'node:child_process';
import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

const output = vscode.window.createOutputChannel('GitCopy');

type CopyMode = 'copy' | 'clean' | 'preview';

export function activate(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('gitCopy.copyRepository', (resource?: vscode.Uri) => runCopy(context, resource, 'copy')),
        vscode.commands.registerCommand('gitCopy.copyRepositoryClean', (resource?: vscode.Uri) => runCopy(context, resource, 'clean')),
        vscode.commands.registerCommand('gitCopy.previewRepositoryCopy', (resource?: vscode.Uri) => runCopy(context, resource, 'preview')),
        vscode.commands.registerCommand('gitCopy.copyWorkspaceRepository', () => runCopy(context, undefined, 'copy')),
        vscode.commands.registerCommand('gitCopy.openSettings', () => vscode.commands.executeCommand('workbench.action.openSettings', '@ext:arcktosh.gitcopy'))
    );
}

export function deactivate(): void {
    // Resources are disposed through the extension context.
}

async function runCopy(context: vscode.ExtensionContext, resource: vscode.Uri | undefined, mode: CopyMode): Promise<void> {
    try {
        const source = await resolveSource(resource);
        if (!source) {
            return;
        }

        const repositoryRoot = await resolveRepositoryRoot(source);
        if (!repositoryRoot) {
            vscode.window.showErrorMessage('GitCopy: The selected folder is not inside a Git repository.');
            return;
        }

        const destination = await resolveDestination(context, repositoryRoot);
        if (!destination) {
            return;
        }

        if (pathsOverlap(repositoryRoot, destination)) {
            vscode.window.showErrorMessage('GitCopy: The destination cannot be the repository itself, a child of it, or a parent containing it.');
            return;
        }

        if (mode === 'clean' && !(await confirmClean(destination))) {
            return;
        }

        const engine = await resolveEngine(context);
        const args = [repositoryRoot, destination];

        if (mode === 'clean') {
            args.push('--clean');
        } else if (mode === 'preview') {
            args.push('--dry-run');
        }

        output.clear();
        output.appendLine(`GitCopy ${mode === 'preview' ? 'preview' : 'copy'}`);
        output.appendLine(`Source:      ${repositoryRoot}`);
        output.appendLine(`Destination: ${destination}`);
        output.appendLine(`Engine:      ${engine}`);
        output.appendLine('');
        output.show(true);

        const exitCode = await vscode.window.withProgress(
            {
                location: vscode.ProgressLocation.Notification,
                title: mode === 'preview' ? 'GitCopy: Previewing repository copy' : 'GitCopy: Copying repository',
                cancellable: true
            },
            async (_progress, token) => runProcess(engine, args, repositoryRoot, token)
        );

        if (exitCode === 0) {
            if (mode === 'preview') {
                vscode.window.showInformationMessage('GitCopy preview complete. See the GitCopy output channel for details.');
                return;
            }

            await rememberDestination(context, path.dirname(destination));
            vscode.window.showInformationMessage(`GitCopy completed: ${destination}`);

            const configuration = vscode.workspace.getConfiguration('gitCopy');
            if (configuration.get<boolean>('revealDestination', true)) {
                await vscode.commands.executeCommand('revealFileInOS', vscode.Uri.file(destination));
            }
            return;
        }

        if (exitCode === null) {
            vscode.window.showWarningMessage('GitCopy was cancelled.');
            return;
        }

        vscode.window.showErrorMessage(`GitCopy failed with exit code ${exitCode}. See the GitCopy output channel.`);
    } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        output.appendLine(`ERROR: ${message}`);
        output.show(true);
        vscode.window.showErrorMessage(`GitCopy: ${message}`);
    }
}

async function resolveSource(resource: vscode.Uri | undefined): Promise<string | undefined> {
    if (resource?.scheme === 'file') {
        return resource.fsPath;
    }

    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) {
        vscode.window.showWarningMessage('GitCopy: Open a Git repository folder in VS Code first.');
        return undefined;
    }

    if (folders.length === 1) {
        return folders[0].uri.fsPath;
    }

    const selected = await vscode.window.showQuickPick(
        folders.map(folder => ({
            label: folder.name,
            description: folder.uri.fsPath,
            folder
        })),
        { placeHolder: 'Select the repository workspace to copy' }
    );

    return selected?.folder.uri.fsPath;
}

async function resolveRepositoryRoot(source: string): Promise<string | undefined> {
    const result = await captureProcess('git', ['rev-parse', '--show-toplevel'], source);
    if (result.exitCode !== 0) {
        return undefined;
    }

    const root = result.stdout.trim();
    return root.length > 0 ? path.resolve(root) : undefined;
}

const historyKey = 'gitCopy.recentDestinationParents';
const historyLimit = 10;

function historyFilePath(): string | undefined {
    if (process.platform !== 'win32') {
        return undefined;
    }
    const localAppData = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
    return path.join(localAppData, 'GitCopy', 'recent-destinations.txt');
}

async function recentDestinations(context: vscode.ExtensionContext): Promise<string[]> {
    const historyPath = historyFilePath();
    let entries: string[];
    if (historyPath) {
        try {
            entries = (await fs.readFile(historyPath, 'utf8')).split(/\r?\n/);
        } catch {
            entries = [];
        }
    } else {
        entries = context.globalState.get<string[]>(historyKey, []);
    }

    const recent: string[] = [];
    for (const entry of entries) {
        if (!entry.trim() || !path.isAbsolute(entry)) {
            continue;
        }
        const resolved = path.resolve(entry.trim());
        if (recent.some(previous => sameDirectory(previous, resolved))) {
            continue;
        }
        if (await directoryExists(resolved)) {
            recent.push(resolved);
        }
        if (recent.length >= historyLimit) {
            break;
        }
    }
    return recent;
}

function sameDirectory(left: string, right: string): boolean {
    const normalize = (value: string) => path.normalize(value).replace(/[\\/]+$/, '');
    return process.platform === 'win32'
        ? normalize(left).toLowerCase() === normalize(right).toLowerCase()
        : normalize(left) === normalize(right);
}

async function saveRecentDestinations(context: vscode.ExtensionContext, destinations: string[]): Promise<void> {
    const historyPath = historyFilePath();
    if (!historyPath) {
        await context.globalState.update(historyKey, destinations);
        return;
    }

    await fs.mkdir(path.dirname(historyPath), { recursive: true });
    const tempPath = path.join(path.dirname(historyPath), `recent-destinations-${process.pid}-${Date.now()}.tmp`);
    try {
        await fs.writeFile(tempPath, destinations.join('\n') + (destinations.length ? '\n' : ''), 'utf8');
        try {
            await fs.rename(tempPath, historyPath);
        } catch (error) {
            // Windows rename does not overwrite an existing destination.
            if ((error as NodeJS.ErrnoException).code !== 'EEXIST' && (error as NodeJS.ErrnoException).code !== 'EPERM') {
                throw error;
            }
            await fs.rm(historyPath, { force: true });
            await fs.rename(tempPath, historyPath);
        }
    } finally {
        await fs.rm(tempPath, { force: true }).catch(() => {});
    }
}

async function rememberDestination(context: vscode.ExtensionContext, destinationParent: string): Promise<void> {
    try {
        if (!(await directoryExists(destinationParent))) {
            return;
        }
        const recent = await recentDestinations(context);
        const updated = [path.resolve(destinationParent), ...recent.filter(folder => !sameDirectory(folder, destinationParent))];
        await saveRecentDestinations(context, updated.slice(0, historyLimit));
    } catch (error) {
        output.appendLine(`Warning: Unable to save recent destination: ${String(error)}`);
    }
}

async function resolveDestination(context: vscode.ExtensionContext, repositoryRoot: string): Promise<string | undefined> {
    const configuration = vscode.workspace.getConfiguration('gitCopy');
    const configuredRoot = expandPath(configuration.get<string>('defaultDestinationRoot', '').trim());
    const repositoryName = path.basename(repositoryRoot);

    if (configuredRoot) {
        return path.join(configuredRoot, repositoryName);
    }

    while (true) {
        const recent = await recentDestinations(context);
        const browseLabel = '$(folder-opened) Browse for another folder...';
        const clearLabel = '$(trash) Clear recent destinations';
        type DestinationChoice = vscode.QuickPickItem & { destination?: string; action?: 'browse' | 'clear' };
        const choices: DestinationChoice[] = recent.map((folder, index) => ({
            label: `$(history) ${path.basename(folder) || folder}`,
            description: folder,
            detail: index === 0 ? 'Most recently used' : undefined,
            destination: folder
        }));
        choices.push({ label: browseLabel, action: 'browse' });
        if (recent.length) {
            choices.push({ label: clearLabel, action: 'clear' });
        }

        const selected = await vscode.window.showQuickPick(choices, {
            title: 'GitCopy: Choose destination parent directory',
            placeHolder: 'Select a recent destination or browse for a new folder',
            matchOnDescription: true
        });
        if (!selected) {
            return undefined;
        }
        if (selected.action === 'clear') {
            try {
                await saveRecentDestinations(context, []);
            } catch (error) {
                vscode.window.showWarningMessage(`GitCopy: Unable to clear recent destinations: ${String(error)}`);
            }
            continue;
        }
        if (selected.destination) {
            if (await directoryExists(selected.destination)) {
                return path.join(selected.destination, repositoryName);
            }
            vscode.window.showWarningMessage('GitCopy: The selected directory no longer exists. Please choose another.');
            continue;
        }

        const folders = await vscode.window.showOpenDialog({
            canSelectFiles: false,
            canSelectFolders: true,
            canSelectMany: false,
            defaultUri: recent.length ? vscode.Uri.file(recent[0]) : undefined,
            openLabel: `Copy ${repositoryName} here`,
            title: 'GitCopy: Select the parent destination folder'
        });
        if (!folders?.length) {
            return undefined;
        }
        return path.join(folders[0].fsPath, repositoryName);
    }
}

async function confirmClean(destination: string): Promise<boolean> {
    const configuration = vscode.workspace.getConfiguration('gitCopy');
    if (!configuration.get<boolean>('confirmClean', true)) {
        return true;
    }

    if (!(await directoryExists(destination))) {
        return true;
    }

    const answer = await vscode.window.showWarningMessage(
        `GitCopy will delete the existing destination folder before copying:\n${destination}`,
        { modal: true },
        'Delete and Copy'
    );

    return answer === 'Delete and Copy';
}

async function resolveEngine(context: vscode.ExtensionContext): Promise<string> {
    const configuration = vscode.workspace.getConfiguration('gitCopy');
    const configured = expandPath(configuration.get<string>('executablePath', '').trim());

    if (configured) {
        if (!(await fileExists(configured))) {
            throw new Error(`Configured executable was not found: ${configured}`);
        }
        return configured;
    }

    if (process.platform === 'win32') {
        const bundled = context.asAbsolutePath(path.join('bin', 'GitCopy.exe'));
        if (await fileExists(bundled)) {
            return bundled;
        }
    }

    return process.platform === 'win32' ? 'GitCopy.exe' : 'GitCopy';
}

function runProcess(command: string, args: string[], cwd: string, token: vscode.CancellationToken): Promise<number | null> {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, {
            cwd,
            windowsHide: true,
            shell: false
        });

        let cancelled = false;
        const cancellation = token.onCancellationRequested(() => {
            cancelled = true;
            child.kill();
        });

        child.stdout.on('data', data => output.append(data.toString()));
        child.stderr.on('data', data => output.append(data.toString()));

        child.on('error', error => {
            cancellation.dispose();
            reject(new Error(`Unable to start GitCopy engine '${command}': ${error.message}`));
        });

        child.on('close', code => {
            cancellation.dispose();
            resolve(cancelled ? null : (code ?? 1));
        });
    });
}

function captureProcess(command: string, args: string[], cwd: string): Promise<{ exitCode: number; stdout: string; stderr: string }> {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, {
            cwd,
            windowsHide: true,
            shell: false
        });

        let stdout = '';
        let stderr = '';

        child.stdout.on('data', data => { stdout += data.toString(); });
        child.stderr.on('data', data => { stderr += data.toString(); });
        child.on('error', error => reject(new Error(`Unable to run '${command}': ${error.message}`)));
        child.on('close', code => resolve({ exitCode: code ?? 1, stdout, stderr }));
    });
}

function pathsOverlap(source: string, destination: string): boolean {
    const relativeSourceToDestination = path.relative(source, destination);
    const relativeDestinationToSource = path.relative(destination, source);

    return relativeSourceToDestination === ''
        || isInside(relativeSourceToDestination)
        || isInside(relativeDestinationToSource);
}

function isInside(relativePath: string): boolean {
    return relativePath !== ''
        && relativePath !== '..'
        && !relativePath.startsWith(`..${path.sep}`)
        && !path.isAbsolute(relativePath);
}

function expandPath(value: string): string {
    if (!value) {
        return '';
    }

    let expanded = value.replace(/^~(?=$|[\\/])/, os.homedir());
    expanded = expanded.replace(/%([^%]+)%/g, (_match, name: string) => process.env[name] ?? `%${name}%`);
    return path.resolve(expanded);
}

async function fileExists(filePath: string): Promise<boolean> {
    try {
        return (await fs.stat(filePath)).isFile();
    } catch {
        return false;
    }
}

async function directoryExists(directoryPath: string): Promise<boolean> {
    try {
        return (await fs.stat(directoryPath)).isDirectory();
    } catch {
        return false;
    }
}

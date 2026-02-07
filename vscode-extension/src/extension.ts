import * as vscode from 'vscode';
import WebSocket from 'ws';
import * as path from 'path';

let ws: WebSocket | null = null;
let reconnectTimer: NodeJS.Timeout | null = null;
let statusBarItem: vscode.StatusBarItem;

export function activate(context: vscode.ExtensionContext) {
    console.log('Black Widow Bridge extension is now active');

    // Create status bar item
    statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
    statusBarItem.text = "$(plug) Black Widow";
    statusBarItem.tooltip = "Connecting to Black Widow...";
    statusBarItem.command = 'blackwidow.reconnect';
    statusBarItem.show();
    context.subscriptions.push(statusBarItem);

    // Connect to Black Widow desktop app
    connectToBlackWidow();

    // Register commands
    context.subscriptions.push(
        vscode.commands.registerCommand('blackwidow.analyzeLog', analyzeLogFile)
    );

    context.subscriptions.push(
        vscode.commands.registerCommand('blackwidow.reconnect', () => {
            vscode.window.showInformationMessage('Reconnecting to Black Widow...');
            connectToBlackWidow();
        })
    );
}

/**
 * Connect to Black Widow desktop app via WebSocket
 */
function connectToBlackWidow() {
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
        return; // Already connected or connecting
    }

    try {
        ws = new WebSocket('ws://localhost:7777');

        ws.on('open', () => {
            console.log('✓ Connected to Black Widow desktop app');
            statusBarItem.text = "$(check) Black Widow";
            statusBarItem.tooltip = "Connected to Black Widow desktop app";
            statusBarItem.backgroundColor = undefined;

            // Send workspace path to desktop app
            sendWorkspacePath();

            // Clear any reconnect timers
            if (reconnectTimer) {
                clearTimeout(reconnectTimer);
                reconnectTimer = null;
            }

            vscode.window.showInformationMessage('✓ Black Widow Bridge connected');
        });

        ws.on('message', (data: WebSocket.Data) => {
            try {
                const message = JSON.parse(data.toString());
                handleMessageFromDesktopApp(message);
            } catch (error) {
                console.error('Error parsing message from desktop app:', error);
            }
        });

        ws.on('error', (error) => {
            console.error('WebSocket error:', error);
            updateStatusDisconnected();
        });

        ws.on('close', () => {
            console.log('✗ Disconnected from Black Widow desktop app');
            updateStatusDisconnected();
            scheduleReconnect();
        });

    } catch (error) {
        console.error('Failed to connect to Black Widow:', error);
        updateStatusDisconnected();
        scheduleReconnect();
    }
}

/**
 * Update status bar to show disconnected state
 */
function updateStatusDisconnected() {
    statusBarItem.text = "$(x) Black Widow";
    statusBarItem.tooltip = "Disconnected. Click to reconnect or start Black Widow desktop app.";
    statusBarItem.backgroundColor = new vscode.ThemeColor('statusBarItem.warningBackground');
}

/**
 * Schedule reconnection attempt
 */
function scheduleReconnect() {
    if (reconnectTimer) {
        return; // Already scheduled
    }

    reconnectTimer = setTimeout(() => {
        reconnectTimer = null;
        console.log('Attempting to reconnect to Black Widow...');
        connectToBlackWidow();
    }, 5000); // Retry every 5 seconds
}

/**
 * Handle messages from Black Widow desktop app
 */
function handleMessageFromDesktopApp(message: any) {
    console.log('Received message from desktop app:', message);

    switch (message.type) {
        case 'openFile':
            openFileAtLine(message.path, message.line);
            break;

        case 'highlightRange':
            highlightCodeRange(message.path, message.startLine, message.endLine);
            break;

        case 'getWorkspacePath':
            sendWorkspacePath();
            break;

        case 'ping':
            sendMessage({ type: 'pong' });
            break;

        default:
            console.warn('Unknown message type:', message.type);
    }
}

/**
 * Open file at specific line in VSCode
 */
async function openFileAtLine(filePath: string, line: number) {
    try {
        // Convert to URI
        const uri = vscode.Uri.file(filePath);

        // Open document
        const document = await vscode.workspace.openTextDocument(uri);
        const editor = await vscode.window.showTextDocument(document, {
            preview: false,
            viewColumn: vscode.ViewColumn.One
        });

        // Jump to line (line numbers are 0-indexed in VSCode)
        const position = new vscode.Position(line - 1, 0);
        editor.selection = new vscode.Selection(position, position);
        editor.revealRange(
            new vscode.Range(position, position),
            vscode.TextEditorRevealType.InCenter
        );

        console.log(`✓ Opened ${path.basename(filePath)} at line ${line}`);
        vscode.window.showInformationMessage(`Opened ${path.basename(filePath)}:${line}`);

    } catch (error) {
        console.error('Error opening file:', error);
        vscode.window.showErrorMessage(`Failed to open ${path.basename(filePath)}: ${error}`);
        sendMessage({ type: 'error', message: `Could not open file: ${error}` });
    }
}

/**
 * Highlight a range of code lines
 */
async function highlightCodeRange(filePath: string, startLine: number, endLine: number) {
    try {
        const uri = vscode.Uri.file(filePath);
        const document = await vscode.workspace.openTextDocument(uri);
        const editor = await vscode.window.showTextDocument(document);

        // Create selection range (0-indexed)
        const start = new vscode.Position(startLine - 1, 0);
        const end = new vscode.Position(endLine - 1, 1000); // End of line
        editor.selection = new vscode.Selection(start, end);
        editor.revealRange(new vscode.Range(start, end), vscode.TextEditorRevealType.InCenter);

        console.log(`✓ Highlighted lines ${startLine}-${endLine} in ${path.basename(filePath)}`);

    } catch (error) {
        console.error('Error highlighting range:', error);
        sendMessage({ type: 'error', message: `Could not highlight range: ${error}` });
    }
}

/**
 * Send workspace path to desktop app
 */
function sendWorkspacePath() {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders && workspaceFolders.length > 0) {
        const workspacePath = workspaceFolders[0].uri.fsPath;
        sendMessage({ type: 'workspacePath', path: workspacePath });
        console.log(`✓ Sent workspace path: ${workspacePath}`);
    } else {
        console.log('⚠ No workspace folder open');
    }
}

/**
 * Send message to desktop app
 */
function sendMessage(message: any) {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
        console.warn('Cannot send message: WebSocket not connected');
        return;
    }

    try {
        ws.send(JSON.stringify(message));
    } catch (error) {
        console.error('Error sending message:', error);
    }
}

/**
 * Analyze current log file with Black Widow
 */
async function analyzeLogFile() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('No file open');
        return;
    }

    const filePath = editor.document.uri.fsPath;
    if (!filePath.endsWith('.log')) {
        vscode.window.showWarningMessage('Current file is not a .log file');
        return;
    }

    // Send log file path to desktop app
    sendMessage({
        type: 'analyzeLog',
        path: filePath
    });

    vscode.window.showInformationMessage(`Sent ${path.basename(filePath)} to Black Widow`);
}

export function deactivate() {
    if (ws) {
        ws.close();
    }
    if (reconnectTimer) {
        clearTimeout(reconnectTimer);
    }
    if (statusBarItem) {
        statusBarItem.dispose();
    }
}

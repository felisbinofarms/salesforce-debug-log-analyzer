# Black Widow Bridge - VSCode Extension

Connect your Black Widow desktop app to VSCode for seamless jump-to-source functionality.

## Features

- **Click to Open**: Click any error location in Black Widow → opens in VSCode at exact line
- **Auto-Connect**: Automatically connects to Black Widow desktop app on localhost:7777
- **Workspace Detection**: Shares your VSCode workspace path with Black Widow
- **Right-Click Analysis**: Right-click `.log` files → "Analyze with Black Widow"
- **Status Indicator**: Shows connection status in status bar

## Requirements

- Black Widow desktop app must be running
- Black Widow app listens on `ws://localhost:7777`

## Installation

### From VSCode Marketplace (Coming Soon)
1. Open VSCode
2. Go to Extensions (Ctrl+Shift+X)
3. Search for "Black Widow Bridge"
4. Click Install

### Manual Installation (Development)
1. Open terminal in extension folder
2. Run `npm install`
3. Run `npm run compile`
4. Press F5 in VSCode to launch extension development host

## Usage

### 1. Start Black Widow Desktop App
Make sure the Black Widow desktop app is running. The extension will automatically connect.

### 2. Check Connection Status
Look at the status bar (bottom right):
- ✓ **Green check**: Connected
- ✗ **Red X**: Disconnected (click to reconnect)

### 3. Click Locations in Black Widow
When you see an error location like `Case_Util.externalEscalationEmail:154`:
1. Click the underlined pink text
2. VSCode opens the file at that exact line
3. Magic! ✨

### 4. Analyze Log Files from VSCode
Right-click any `.log` file in Explorer:
- Select "Analyze with Black Widow"
- File is sent to desktop app for analysis

## How It Works

```
┌─────────────────────────┐
│  Black Widow Desktop    │
│  (localhost:7777)       │
└───────────┬─────────────┘
            │ WebSocket
            │ Commands:
            │ • openFile
            │ • highlightRange
            │
            ▼
┌─────────────────────────┐
│  VSCode Extension       │
│  (This extension)       │
└─────────────────────────┘
```

## Commands

- `Black Widow: Analyze Log` - Send current .log file to Black Widow
- `Black Widow: Reconnect` - Manually reconnect to desktop app

## Extension Settings

No configuration needed! Works out of the box.

## Troubleshooting

### "Black Widow" shows red X in status bar
**Solution:** 
1. Make sure Black Widow desktop app is running
2. Check that nothing else is using port 7777
3. Click the status bar item to manually reconnect

### Files not opening when clicking locations
**Solution:**
1. Make sure you have a workspace folder open in VSCode
2. The Apex class files must be in your workspace
3. Check VSCode Output panel → "Black Widow Bridge" for errors

### WebSocket connection refused
**Solution:**
1. Restart Black Widow desktop app
2. Check Windows Firewall isn't blocking localhost:7777
3. Try running VSCode as administrator

## Development

### Building
```bash
npm install
npm run compile
```

### Testing
```bash
npm run watch  # Auto-recompile on changes
# Press F5 in VSCode to launch Extension Development Host
```

### Packaging
```bash
npm install -g vsce
vsce package
# Creates black-widow-bridge-1.0.0.vsix
```

## Release Notes

### 1.0.0 (Feb 5, 2026)
- Initial release
- WebSocket bridge to Black Widow desktop app
- Jump-to-source from error locations
- Auto-reconnect on disconnect
- Right-click context menu for .log files

## License

MIT License - See LICENSE file

## Feedback & Issues

- Report bugs: [GitHub Issues](https://github.com/felisbinofarms/salesforce-debug-log-analyzer/issues)
- Feature requests: [GitHub Discussions](https://github.com/felisbinofarms/salesforce-debug-log-analyzer/discussions)

---

**Made with 🕷️ by the Black Widow team**

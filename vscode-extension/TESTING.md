# Testing the VSCode Bridge Extension

## Quick Test Steps

### Step 1: Start Black Widow Desktop App
In a PowerShell terminal:
```powershell
cd "e:\Black Widow\salesforce-debug-log-analyzer"
dotnet run
```
Wait for the app window to open. The EditorBridgeService will automatically start on `localhost:7777`.

### Step 2: Press F5 in VSCode
- VSCode should already be open with the extension project
- Press **F5** (or click Run → Start Debugging)
- A new VSCode window opens (Extension Development Host)

### Step 3: Check Connection Status
Look at the status bar (bottom right corner) in the Extension Development Host window:
- ✅ **Green check + "Black Widow"** = Connected!
- ❌ **Red X + "Black Widow"** = Not connected (desktop app not running)

### Step 4: Test Jump-to-Source

#### Option A: Using Sample Log (Recommended)
1. In the desktop app, click **File → Open** or **Connect to Salesforce**
2. Load a log file with errors (check `SampleLogs/` folder)
3. Look for the **Actionable Issues** panel
4. You should see locations like:
   - `Case_Util.externalEscalationEmail:154`
   - `CaseTrigger.apxt:42`
5. **Click** the underlined pink location text
6. VSCode should jump to that file at the exact line! ✨

#### Option B: Manual Test
In the Extension Development Host window terminal:
```javascript
// This simulates what the desktop app sends
// (Just for testing - normally desktop app does this)
```

### Step 5: Test Right-Click Context Menu
1. In Extension Development Host, open a `.log` file
2. Right-click in the editor
3. You should see **"Analyze with Black Widow"** option
4. Click it → Desktop app should load the log file

## What to Look For

### ✅ Success Indicators:
- Status bar shows green check
- Clicking locations in desktop app opens files in VSCode
- VSCode jumps to the correct line
- Console output shows: `Connected to Black Widow desktop app`

### ❌ Troubleshooting:

**Status bar shows red X:**
- Desktop app not running
- Port 7777 blocked by firewall
- Another app using port 7777

**Files don't open:**
- Make sure you have a workspace folder open in VSCode
- Apex classes must be in workspace (e.g., `force-app/main/default/classes/`)
- Check Output panel → "Black Widow Bridge" for errors

**"Cannot connect to localhost:7777":**
- Restart desktop app
- Check Windows Firewall settings
- Try running VSCode as Administrator

## Debug Output

Open the **Output** panel in VSCode (Ctrl+Shift+U):
- Select **"Black Widow Bridge"** from dropdown
- You'll see connection logs:
  ```
  Attempting to connect to Black Widow...
  Connected to Black Widow desktop app
  Workspace path sent: E:\MyProject
  Received command: openFile
  Opening file: Case_Util.cls at line 154
  ```

## Advanced Testing

### Test Auto-Reconnect:
1. Start extension (F5)
2. Verify connection (green check)
3. Close desktop app
4. Status bar should show red X
5. Restart desktop app
6. Within 5 seconds, status bar should show green check again ✅

### Test Workspace Detection:
1. Open a Salesforce project in Extension Development Host
2. Desktop app should receive the workspace path
3. Verify in desktop app's debug output

## Next Steps After Testing

Once everything works:
1. Package extension: `vsce package`
2. Install locally: Extensions → Install from VSIX
3. Share with team or publish to marketplace

## Need Help?

- Check Output panel (Ctrl+Shift+U → "Black Widow Bridge")
- Check desktop app console for errors
- Verify port 7777 is not in use: `netstat -an | Select-String "7777"`

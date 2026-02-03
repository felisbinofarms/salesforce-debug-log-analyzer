using System.Diagnostics;
using System.IO;
using System.Text;
using SalesforceDebugAnalyzer.Models;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Service for integrating with Salesforce CLI to stream real-time debug logs
/// </summary>
public class SalesforceCliService
{
    private Process? _cliProcess;
    private readonly string _cliPath;
    private readonly bool _useLegacyCommands;
    private bool _isStreaming;
    private readonly StringBuilder _logBuffer = new();
    private bool _isBufferingLog = false;
    private Timer? _pollingTimer;
    private HashSet<string> _processedLogIds = new();
    private string? _streamingUsername;
    private string? _streamingOrgAlias;

    public event EventHandler<LogReceivedEventArgs>? LogReceived;
    public event EventHandler<string>? StatusChanged;

    public bool IsInstalled => CheckCliInstalled();
    public bool IsStreaming => _isStreaming;

    public SalesforceCliService()
    {
        // Check common CLI installation paths and determine command format
        (_cliPath, _useLegacyCommands) = FindSalesforceCli();
    }

    /// <summary>
    /// Check if Salesforce CLI (sf or sfdx) is installed
    /// </summary>
    private bool CheckCliInstalled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c sf --version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            // Try legacy sfdx
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c sfdx --version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Find Salesforce CLI executable path and determine command format
    /// </summary>
    private (string cliPath, bool useLegacy) FindSalesforceCli()
    {
        // Check if sf has apex commands (newer CLI versions)
        if (CheckCommand("sf") && CheckSfApexCommands())
            return ("sf", false);

        // Fall back to legacy CLI (sfdx) with force:apex commands
        if (CheckCommand("sfdx"))
            return ("sfdx", true);
        
        // If sf exists but no apex commands, still use sfdx for log operations
        if (CheckCommand("sf") && CheckCommand("sfdx"))
            return ("sfdx", true);

        return (string.Empty, false);
    }

    /// <summary>
    /// Check if sf CLI has the apex commands available
    /// </summary>
    private bool CheckSfApexCommands()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c sf apex --help",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckCommand(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command} --version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start streaming logs for a specific user by polling every 3 seconds
    /// </summary>
    public async Task<bool> StartStreamingAsync(string username, string orgAlias = "")
    {
        if (_isStreaming)
        {
            StatusChanged?.Invoke(this, "Already streaming logs");
            return await Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(_cliPath))
        {
            StatusChanged?.Invoke(this, "Salesforce CLI not installed. Install from: https://developer.salesforce.com/tools/salesforcecli");
            return await Task.FromResult(false);
        }

        try
        {
            StatusChanged?.Invoke(this, $"Starting log stream for {username}...");

            _streamingUsername = username;
            _streamingOrgAlias = orgAlias;
            _processedLogIds.Clear();
            _isStreaming = true;

            // Start polling timer (check for new logs every 3 seconds)
            _pollingTimer = new Timer(async _ => await PollForNewLogsAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));

            StatusChanged?.Invoke(this, $"✓ Streaming logs from {username} (polling every 3 seconds)");

            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to start streaming: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Poll for new logs using CLI
    /// </summary>
    private async Task PollForNewLogsAsync()
    {
        if (!_isStreaming) return;

        try
        {
            // List logs using CLI
            string listCommand;
            if (_useLegacyCommands)
            {
                listCommand = string.IsNullOrEmpty(_streamingOrgAlias)
                    ? $"{_cliPath} force:apex:log:list --targetusername {_streamingUsername} --json"
                    : $"{_cliPath} force:apex:log:list -u {_streamingOrgAlias} --json";
            }
            else
            {
                listCommand = string.IsNullOrEmpty(_streamingOrgAlias)
                    ? $"{_cliPath} apex list log --target-org {_streamingUsername} --json"
                    : $"{_cliPath} apex list log -o {_streamingOrgAlias} --json";
            }

            var output = await ExecuteCliCommandAsync(listCommand);
            var jsonOutput = string.Join("\n", output);

            // Parse JSON to get log IDs (simplified - would need proper JSON parsing)
            // For now, get the first 5 most recent logs
            var logListMatch = System.Text.RegularExpressions.Regex.Matches(jsonOutput, @"""Id""\s*:\s*""([^""]+)""");
            
            foreach (System.Text.RegularExpressions.Match match in logListMatch.Take(5))
            {
                var logId = match.Groups[1].Value;
                
                // Skip if already processed
                if (_processedLogIds.Contains(logId)) continue;
                
                // Download this log
                await DownloadAndEmitLogAsync(logId);
                _processedLogIds.Add(logId);
                
                // Keep only last 100 processed IDs in memory
                if (_processedLogIds.Count > 100)
                {
                    _processedLogIds = new HashSet<string>(_processedLogIds.Skip(50));
                }
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"⚠️ Polling error: {ex.Message}");
        }
    }

    /// <summary>
    /// Download a specific log and emit it
    /// </summary>
    private async Task DownloadAndEmitLogAsync(string logId)
    {
        try
        {
            string getCommand;
            if (_useLegacyCommands)
            {
                getCommand = string.IsNullOrEmpty(_streamingOrgAlias)
                    ? $"{_cliPath} force:apex:log:get --logid {logId} --targetusername {_streamingUsername}"
                    : $"{_cliPath} force:apex:log:get --logid {logId} -u {_streamingOrgAlias}";
            }
            else
            {
                getCommand = string.IsNullOrEmpty(_streamingOrgAlias)
                    ? $"{_cliPath} apex get log --log-id {logId} --target-org {_streamingUsername}"
                    : $"{_cliPath} apex get log --log-id {logId} -o {_streamingOrgAlias}";
            }

            var output = await ExecuteCliCommandAsync(getCommand);
            var logContent = string.Join("\n", output);

            // Only emit if we got actual log content (not just JSON metadata)
            if (logContent.Length > 200 && (logContent.Contains("EXECUTION_STARTED") || logContent.Contains("CODE_UNIT_STARTED")))
            {
                LogReceived?.Invoke(this, new LogReceivedEventArgs
                {
                    LogContent = logContent,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            // Silently ignore individual log download errors
        }
    }

    /// <summary>
    /// Stop streaming logs
    /// </summary>
    public void StopStreaming()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        
        if (_cliProcess != null && !_cliProcess.HasExited)
        {
            _cliProcess.Kill();
            _cliProcess.Dispose();
            _cliProcess = null;
        }

        _isStreaming = false;
        _processedLogIds.Clear();
        StatusChanged?.Invoke(this, "Log streaming stopped");
    }

    /// <summary>
    /// Download logs for a specific user and time range using CLI
    /// </summary>
    public async Task<List<string>> DownloadLogsAsync(string username, DateTime startTime, DateTime endTime, string outputFolder)
    {
        var logFiles = new List<string>();

        try
        {
            StatusChanged?.Invoke(this, $"Downloading logs from {startTime:g} to {endTime:g}...");

            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputFolder);

            // Build CLI command to list logs
            var queryCommand = _useLegacyCommands ? "force:data:soql:query" : "data query";
            var listCommand = $"{_cliPath} {queryCommand} --query \"SELECT Id, LogUserId, LogLength, StartTime FROM ApexLog WHERE LogUserId IN (SELECT Id FROM User WHERE Username = '{username}') AND StartTime >= {startTime:yyyy-MM-ddTHH:mm:ss.fffZ} AND StartTime <= {endTime:yyyy-MM-ddTHH:mm:ss.fffZ} ORDER BY StartTime DESC\" --json";

            var logIds = await ExecuteCliCommandAsync(listCommand);

            // Parse JSON response and extract log IDs (simplified - would need proper JSON parsing)
            // For each log ID, download the log body

            foreach (var logId in logIds)
            {
                string downloadCommand;
                if (_useLegacyCommands)
                {
                    downloadCommand = $"{_cliPath} force:apex:log:get --logid {logId} --outputdir {outputFolder}";
                }
                else
                {
                    downloadCommand = $"{_cliPath} apex get log --log-id {logId} --output-dir {outputFolder}";
                }
                await ExecuteCliCommandAsync(downloadCommand);
                
                var logFilePath = Path.Combine(outputFolder, $"apex-{logId}.log");
                if (File.Exists(logFilePath))
                {
                    logFiles.Add(logFilePath);
                }
            }

            StatusChanged?.Invoke(this, $"✓ Downloaded {logFiles.Count} logs");
            return logFiles;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to download logs: {ex.Message}");
            return logFiles;
        }
    }

    private async Task<List<string>> ExecuteCliCommandAsync(string command)
    {
        var output = new List<string>();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        
        while (!process.StandardOutput.EndOfStream)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line))
            {
                output.Add(line);
            }
        }

        await process.WaitForExitAsync();
        return output;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;

        // Buffer log lines until we have a complete log
        // Start marker: EXECUTION_STARTED or timestamp pattern [HH:MM:SS.mmm]
        // End marker: EXECUTION_FINISHED or next EXECUTION_STARTED
        
        var line = e.Data;
        
        // Detect start of a new log
        if (line.Contains("EXECUTION_STARTED") || line.Contains("CODE_UNIT_STARTED"))
        {
            // If we were already buffering, emit the previous log
            if (_isBufferingLog && _logBuffer.Length > 0)
            {
                EmitBufferedLog();
            }
            
            _isBufferingLog = true;
            _logBuffer.Clear();
            _logBuffer.AppendLine(line);
        }
        else if (_isBufferingLog)
        {
            _logBuffer.AppendLine(line);
            
            // Detect end of log
            if (line.Contains("EXECUTION_FINISHED") || line.Contains("CODE_UNIT_FINISHED"))
            {
                EmitBufferedLog();
            }
        }
        else
        {
            // Forward non-log lines as status
            StatusChanged?.Invoke(this, line);
        }
    }
    
    private void EmitBufferedLog()
    {
        if (_logBuffer.Length == 0) return;
        
        var logContent = _logBuffer.ToString();
        
        // Only emit if it looks like a complete log (has meaningful content)
        if (logContent.Length > 100)
        {
            LogReceived?.Invoke(this, new LogReceivedEventArgs
            {
                LogContent = logContent,
                Timestamp = DateTime.UtcNow
            });
        }
        
        _logBuffer.Clear();
        _isBufferingLog = false;
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            StatusChanged?.Invoke(this, $"⚠️ {e.Data}");
        }
    }
}

/// <summary>
/// Event args for log received event
/// </summary>
public class LogReceivedEventArgs : EventArgs
{
    public string LogContent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

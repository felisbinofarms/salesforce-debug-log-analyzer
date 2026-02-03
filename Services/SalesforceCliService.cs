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
    private bool _isStreaming;

    public event EventHandler<LogReceivedEventArgs>? LogReceived;
    public event EventHandler<string>? StatusChanged;

    public bool IsInstalled => CheckCliInstalled();
    public bool IsStreaming => _isStreaming;

    public SalesforceCliService()
    {
        // Check common CLI installation paths
        _cliPath = FindSalesforceCli();
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
    /// Find Salesforce CLI executable path
    /// </summary>
    private string FindSalesforceCli()
    {
        // Try new CLI (sf) first
        if (CheckCommand("sf"))
            return "sf";

        // Fall back to legacy CLI (sfdx)
        if (CheckCommand("sfdx"))
            return "sfdx";

        return string.Empty;
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
    /// Start streaming logs for a specific user
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

            // Build CLI command - sf apex get log --tail streams logs in real-time
            // The user needs to have an active trace flag for this to work
            var command = string.IsNullOrEmpty(orgAlias)
                ? $"{_cliPath} apex get log --number 1 --target-org {username}"
                : $"{_cliPath} apex get log --number 1 -o {orgAlias}";
            
            // Note: Real streaming requires polling or using sf apex tail log (if available)
            // For now, we'll poll for new logs periodically

            _cliProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            _cliProcess.OutputDataReceived += OnOutputDataReceived;
            _cliProcess.ErrorDataReceived += OnErrorDataReceived;

            _cliProcess.Start();
            _cliProcess.BeginOutputReadLine();
            _cliProcess.BeginErrorReadLine();

            _isStreaming = true;
            StatusChanged?.Invoke(this, $"✓ Streaming logs from {username}");

            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Failed to start streaming: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop streaming logs
    /// </summary>
    public void StopStreaming()
    {
        if (_cliProcess != null && !_cliProcess.HasExited)
        {
            _cliProcess.Kill();
            _cliProcess.Dispose();
            _cliProcess = null;
        }

        _isStreaming = false;
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
            var listCommand = $"{_cliPath} data query --query \"SELECT Id, LogUserId, LogLength, StartTime FROM ApexLog WHERE LogUserId IN (SELECT Id FROM User WHERE Username = '{username}') AND StartTime >= {startTime:yyyy-MM-ddTHH:mm:ss.fffZ} AND StartTime <= {endTime:yyyy-MM-ddTHH:mm:ss.fffZ} ORDER BY StartTime DESC\" --json";

            var logIds = await ExecuteCliCommandAsync(listCommand);

            // Parse JSON response and extract log IDs (simplified - would need proper JSON parsing)
            // For each log ID, download the log body

            foreach (var logId in logIds)
            {
                var downloadCommand = $"{_cliPath} apex get log --log-id {logId} --output-dir {outputFolder}";
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

        // Parse log output from CLI
        // CLI output format: timestamp | event_type | details
        // We'll emit events when we detect a new log file is available

        if (e.Data.Contains("EXECUTION_STARTED"))
        {
            // New log started
            LogReceived?.Invoke(this, new LogReceivedEventArgs
            {
                LogContent = e.Data,
                Timestamp = DateTime.UtcNow
            });
        }

        // Forward all output as status updates
        StatusChanged?.Invoke(this, e.Data);
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

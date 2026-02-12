using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Bridge service that connects Black Widow desktop app to VSCode extension
/// Runs a WebSocket server on localhost:7777
/// </summary>
public class EditorBridgeService : IDisposable
{
    private HttpListener? _httpListener;
    private WebSocket? _connectedClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isRunning;
    private string? _workspacePath;

    public bool IsConnected => _connectedClient?.State == WebSocketState.Open;
    public string? WorkspacePath => _workspacePath;
    
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<string>? WorkspacePathReceived;

    /// <summary>
    /// Start the bridge server on port 7777
    /// </summary>
    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:7777/");
            _httpListener.Start();
            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            Console.WriteLine("✓ Editor Bridge Service started on http://localhost:7777");

            // Start accepting connections
            _ = Task.Run(async () => await AcceptConnectionsAsync(_cancellationTokenSource.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to start Editor Bridge Service: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Accept WebSocket connections from VSCode extension
    /// </summary>
    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener != null)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                // Handle WebSocket upgrade
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    _connectedClient = wsContext.WebSocket;
                    ConnectionStatusChanged?.Invoke(this, true);
                    Console.WriteLine("✓ VSCode extension connected");

                    // Handle messages from VSCode
                    _ = Task.Run(async () => await ReceiveMessagesAsync(wsContext.WebSocket, cancellationToken));
                }
                else
                {
                    // Regular HTTP request (fallback)
                    context.Response.StatusCode = 200;
                    var responseString = "{\"status\":\"Black Widow Editor Bridge Active\"}";
                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    context.Response.Close();
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"Error accepting connection: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Receive messages from VSCode extension
    /// </summary>
    private async Task ReceiveMessagesAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 4];
        var messageBuffer = new MemoryStream();

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
                    _connectedClient = null;
                    ConnectionStatusChanged?.Invoke(this, false);
                    Console.WriteLine("✗ VSCode extension disconnected");
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuffer.Write(buffer, 0, result.Count);

                    if (result.EndOfMessage)
                    {
                        var message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                        messageBuffer.SetLength(0);
                        HandleMessageFromVSCode(message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving message: {ex.Message}");
            _connectedClient = null;
            ConnectionStatusChanged?.Invoke(this, false);
        }
        finally
        {
            messageBuffer.Dispose();
        }
    }

    /// <summary>
    /// Handle incoming messages from VSCode extension
    /// </summary>
    private void HandleMessageFromVSCode(string message)
    {
        try
        {
            var json = JsonDocument.Parse(message);
            var root = json.RootElement;

            if (root.TryGetProperty("type", out var typeElement))
            {
                var type = typeElement.GetString();

                switch (type)
                {
                    case "workspacePath":
                        if (root.TryGetProperty("path", out var pathElement))
                        {
                            _workspacePath = pathElement.GetString();
                            WorkspacePathReceived?.Invoke(this, _workspacePath ?? "");
                            Console.WriteLine($"✓ Received workspace path: {_workspacePath}");
                        }
                        break;

                    case "pong":
                        Console.WriteLine("✓ VSCode extension is alive");
                        break;

                    case "error":
                        if (root.TryGetProperty("message", out var errorElement))
                        {
                            Console.WriteLine($"✗ VSCode error: {errorElement.GetString()}");
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing VSCode message: {ex.Message}");
        }
    }

    /// <summary>
    /// Send command to VSCode extension to open a file at specific line
    /// </summary>
    public async Task<bool> OpenFileInEditorAsync(string filePath, int line)
    {
        if (!IsConnected)
        {
            Console.WriteLine("✗ VSCode extension not connected");
            return false;
        }

        try
        {
            var command = new
            {
                type = "openFile",
                path = filePath,
                line = line
            };

            var json = JsonSerializer.Serialize(command);
            var buffer = Encoding.UTF8.GetBytes(json);

            await _connectedClient!.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            Console.WriteLine($"✓ Sent openFile command: {Path.GetFileName(filePath)}:{line}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to send openFile command: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Request workspace path from VSCode
    /// </summary>
    public async Task<bool> RequestWorkspacePathAsync()
    {
        if (!IsConnected)
        {
            return false;
        }

        try
        {
            var command = new { type = "getWorkspacePath" };
            var json = JsonSerializer.Serialize(command);
            var buffer = Encoding.UTF8.GetBytes(json);

            await _connectedClient!.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to request workspace path: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ping VSCode extension to check if alive
    /// </summary>
    public async Task<bool> PingAsync()
    {
        if (!IsConnected)
        {
            return false;
        }

        try
        {
            var command = new { type = "ping" };
            var json = JsonSerializer.Serialize(command);
            var buffer = Encoding.UTF8.GetBytes(json);

            await _connectedClient!.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Find Apex class file in workspace
    /// </summary>
    public string? FindApexFile(string className)
    {
        if (string.IsNullOrEmpty(_workspacePath))
        {
            return null;
        }

        // Remove method name if present (e.g., "MyClass.myMethod" → "MyClass")
        var classNameOnly = className.Split('.')[0];

        // Common Salesforce project structures
        var searchPaths = new[]
        {
            Path.Combine(_workspacePath, "force-app", "main", "default", "classes"),
            Path.Combine(_workspacePath, "src", "classes"),
            Path.Combine(_workspacePath, "classes")
        };

        foreach (var searchPath in searchPaths)
        {
            if (!Directory.Exists(searchPath)) continue;

            var filePath = Path.Combine(searchPath, $"{classNameOnly}.cls");
            if (File.Exists(filePath))
            {
                return filePath;
            }

            // Search recursively if not found
            var files = Directory.GetFiles(searchPath, $"{classNameOnly}.cls", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Stop the bridge server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        try
        {
            _cancellationTokenSource?.Cancel();
            _isRunning = false;

            // Close WebSocket gracefully without blocking (avoid UI deadlock)
            if (_connectedClient?.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    _ = _connectedClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                }
                catch { /* Best-effort close */ }
            }

            _httpListener?.Stop();
            _httpListener?.Close();

            Console.WriteLine("✓ Editor Bridge Service stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping Editor Bridge Service: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource?.Dispose();
        _connectedClient?.Dispose();
        _httpListener = null;
    }
}

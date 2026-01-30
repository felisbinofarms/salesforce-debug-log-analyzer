using System.Windows;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Web.WebView2.Core;
using SalesforceDebugAnalyzer.Services;

namespace SalesforceDebugAnalyzer.Views;

public partial class OAuthBrowserDialog : Window
{
    private readonly TaskCompletionSource<OAuthResult> _completionSource = new();
    private readonly bool _useSandbox;
    private string? _codeVerifier;
    private string? _expectedState;
    private HttpListener? _httpListener;
    private int _localPort;

    public OAuthBrowserDialog(bool useSandbox = false)
    {
        InitializeComponent();
        _useSandbox = useSandbox;
        Loaded += OAuthBrowserDialog_Loaded;
    }

    public Task<OAuthResult> AuthenticateAsync()
    {
        return _completionSource.Task;
    }

    private async void OAuthBrowserDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Initializing browser...";
            await WebBrowser.EnsureCoreWebView2Async(null);

            // Use fixed port for PlatformCLI
            _localPort = 1717;
            var redirectUri = "http://localhost:1717/OauthRedirect";
            
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://localhost:1717/");
                _httpListener.Start();
                StatusText.Text = "Local server started on port 1717...";
            }
            catch (HttpListenerException)
            {
                // Port might be in use, try a different approach
                _completionSource.SetResult(new OAuthResult
                {
                    Success = false,
                    Error = "Port 1717 is already in use. Please close other applications and try again."
                });
                Close();
                return;
            }
            
            // Start listening for callback in background
            _ = ListenForCallbackAsync();

            // Subscribe to navigation events
            WebBrowser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            WebBrowser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            // Generate PKCE parameters
            _codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(_codeVerifier);
            _expectedState = Guid.NewGuid().ToString("N");

            // Build authorization URL
            var authEndpoint = _useSandbox
                ? "https://test.salesforce.com/services/oauth2/authorize"
                : "https://login.salesforce.com/services/oauth2/authorize";

            var authUrl = $"{authEndpoint}?" +
                         $"response_type=code&" +
                         $"client_id=PlatformCLI&" +
                         $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                         $"scope={Uri.EscapeDataString("api refresh_token web")}&" +
                         $"code_challenge={codeChallenge}&" +
                         $"code_challenge_method=S256&" +
                         $"state={_expectedState}&" +
                         $"prompt=login";

            StatusText.Text = "Navigating to Salesforce login...";
            WebBrowser.CoreWebView2.Navigate(authUrl);
        }
        catch (Exception ex)
        {
            _completionSource.SetResult(new OAuthResult
            {
                Success = false,
                Error = $"Failed to initialize browser: {ex.Message}"
            });
            Close();
        }
    }

    private async Task ListenForCallbackAsync()
    {
        try
        {
            var context = await _httpListener!.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            // Parse query parameters
            var query = request.Url?.Query;
            var queryParams = System.Web.HttpUtility.ParseQueryString(query ?? "");

            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];

            // Send response to browser
            string responseString;
            if (!string.IsNullOrEmpty(error))
            {
                responseString = $"<html><body><h1>Authentication Failed</h1><p>Error: {error}</p><p>You can close this window.</p></body></html>";
                
                await Dispatcher.InvokeAsync(() =>
                {
                    _completionSource.SetResult(new OAuthResult
                    {
                        Success = false,
                        Error = $"OAuth error: {error}"
                    });
                    Close();
                });
            }
            else if (state != _expectedState)
            {
                responseString = "<html><body><h1>Authentication Failed</h1><p>Invalid state parameter</p><p>You can close this window.</p></body></html>";
                
                await Dispatcher.InvokeAsync(() =>
                {
                    _completionSource.SetResult(new OAuthResult
                    {
                        Success = false,
                        Error = "Invalid state parameter"
                    });
                    Close();
                });
            }
            else if (!string.IsNullOrEmpty(code) && _codeVerifier != null)
            {
                responseString = "<html><body><h1>Authentication Successful!</h1><p>Completing login... You can close this window.</p></body></html>";
                
                await Dispatcher.InvokeAsync(async () =>
                {
                    StatusText.Text = "Exchanging authorization code for token...";
                    var result = await ExchangeCodeForTokenAsync(code, _codeVerifier);
                    _completionSource.SetResult(result);
                    Close();
                });
            }
            else
            {
                responseString = "<html><body><h1>Authentication Failed</h1><p>No authorization code received</p></body></html>";
                
                await Dispatcher.InvokeAsync(() =>
                {
                    _completionSource.SetResult(new OAuthResult
                    {
                        Success = false,
                        Error = "No authorization code received"
                    });
                    Close();
                });
            }

            var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _completionSource.SetResult(new OAuthResult
                {
                    Success = false,
                    Error = $"Callback error: {ex.Message}"
                });
                Close();
            });
        }
        finally
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
    }

    private async void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        StatusText.Text = $"Loading...";
        
        // WebView will try to navigate to localhost callback, but we're handling it with HttpListener
        // So we can just show loading status here
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            StatusText.Text = "Ready";
        }
        else
        {
            StatusText.Text = $"Navigation failed";
        }
    }

    private async Task<OAuthResult> ExchangeCodeForTokenAsync(string code, string codeVerifier)
    {
        using var httpClient = new HttpClient();

        var tokenEndpoint = _useSandbox
            ? "https://test.salesforce.com/services/oauth2/token"
            : "https://login.salesforce.com/services/oauth2/token";

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "client_id", "PlatformCLI" },
            { "code_verifier", codeVerifier },
            { "redirect_uri", "http://localhost:1717/OauthRedirect" }
        };

        try
        {
            var content = new FormUrlEncodedContent(parameters);
            var response = await httpClient.PostAsync(tokenEndpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new OAuthResult
                {
                    Success = false,
                    Error = $"Token exchange failed: {responseContent}"
                };
            }

            var tokenResponse = System.Text.Json.JsonDocument.Parse(responseContent);
            var root = tokenResponse.RootElement;

            return new OAuthResult
            {
                Success = true,
                AccessToken = root.GetProperty("access_token").GetString() ?? "",
                RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
                InstanceUrl = root.GetProperty("instance_url").GetString() ?? ""
            };
        }
        catch (Exception ex)
        {
            return new OAuthResult
            {
                Success = false,
                Error = $"Exception during token exchange: {ex.Message}"
            };
        }
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Base64UrlEncode(bytes);
    }

    private string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.ASCII.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);
        return Base64UrlEncode(hash);
    }

    private string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    private int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _completionSource.SetResult(new OAuthResult
        {
            Success = false,
            Error = "Login cancelled by user"
        });
        Close();
    }
}

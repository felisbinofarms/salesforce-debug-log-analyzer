using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SalesforceDebugAnalyzer.Services;
using Serilog;

namespace SalesforceDebugAnalyzer.Views;

public partial class OAuthBrowserDialog : Window
{
    private readonly TaskCompletionSource<OAuthResult> _completionSource = new();
    private readonly bool _useSandbox;
    private string? _codeVerifier;
    private string? _expectedState;
    private HttpListener? _httpListener;

    public OAuthBrowserDialog() : this(false) { }

    public OAuthBrowserDialog(bool useSandbox)
    {
        InitializeComponent();
        _useSandbox = useSandbox;
        Loaded += OAuthBrowserDialog_Loaded;
    }

    public Task<OAuthResult> AuthenticateAsync()
    {
        return _completionSource.Task;
    }

    private void OAuthBrowserDialog_Loaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Starting local callback server...";
            LoadingProgressBar.IsVisible = true;

            var redirectUri = "http://localhost:1717/OauthRedirect";

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://localhost:1717/");
                _httpListener.Start();
                StatusText.Text = "Local server started on port 1717";
            }
            catch (HttpListenerException ex)
            {
                _completionSource.TrySetResult(new OAuthResult
                {
                    Success = false,
                    Error = $"Port 1717 is already in use. Please close other apps using this port.\n\nError: {ex.Message}"
                });
                Close();
                return;
            }

            _ = ListenForCallbackAsync();

            _codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(_codeVerifier);
            _expectedState = Guid.NewGuid().ToString("N");

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

            StatusText.Text = "Opening browser for login...";
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            StatusText.Text = "Complete login in your browser, then return here";
            LoadingProgressBar.IsVisible = true;
        }
        catch (Exception ex)
        {
            _completionSource.TrySetResult(new OAuthResult
            {
                Success = false,
                Error = $"Failed to start OAuth flow: {ex.Message}"
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

            var query = request.Url?.Query;
            var queryParams = HttpUtility.ParseQueryString(query ?? "");

            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];

            string responseString;
            if (!string.IsNullOrEmpty(error))
            {
                var safeError = WebUtility.HtmlEncode(error);
                responseString = $"<html><body><h1>Authentication Failed</h1><p>Error: {safeError}</p><p>You can close this window.</p></body></html>";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _completionSource.TrySetResult(new OAuthResult
                    {
                        Success = false,
                        Error = $"OAuth error: {error}"
                    });
                    Close();
                });
            }
            else if (state != _expectedState)
            {
                responseString = "<html><body><h1>Authentication Failed</h1><p>Invalid state parameter</p></body></html>";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _completionSource.TrySetResult(new OAuthResult
                    {
                        Success = false,
                        Error = "Invalid state parameter"
                    });
                    Close();
                });
            }
            else if (!string.IsNullOrEmpty(code) && _codeVerifier != null)
            {
                responseString = "<html><body><h1>Authentication Successful!</h1><p>You can close this window.</p></body></html>";

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    StatusText.Text = "Exchanging authorization code for token...";
                    var result = await ExchangeCodeForTokenAsync(code, _codeVerifier);
                    _completionSource.TrySetResult(result);
                    Close();
                });
            }
            else
            {
                responseString = "<html><body><h1>Authentication Failed</h1><p>No authorization code received</p></body></html>";

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _completionSource.TrySetResult(new OAuthResult
                    {
                        Success = false,
                        Error = "No authorization code received"
                    });
                    Close();
                });
            }

            var buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _completionSource.TrySetResult(new OAuthResult
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

            using var tokenResponse = JsonDocument.Parse(responseContent);
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

    private static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean up OAuth callback listener");
        }

        _completionSource.TrySetResult(new OAuthResult
        {
            Success = false,
            Error = "Login cancelled by user"
        });
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenInBrowser_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var authEndpoint = _useSandbox
                ? "https://test.salesforce.com/services/oauth2/authorize"
                : "https://login.salesforce.com/services/oauth2/authorize";

            if (string.IsNullOrEmpty(_codeVerifier))
            {
                _codeVerifier = GenerateCodeVerifier();
            }

            var codeChallenge = GenerateCodeChallenge(_codeVerifier);

            if (string.IsNullOrEmpty(_expectedState))
            {
                _expectedState = Guid.NewGuid().ToString("N");
            }

            var authUrl = $"{authEndpoint}?" +
                         $"response_type=code&" +
                         $"client_id=PlatformCLI&" +
                         $"redirect_uri={Uri.EscapeDataString("http://localhost:1717/OauthRedirect")}&" +
                         $"scope={Uri.EscapeDataString("api refresh_token web")}&" +
                         $"code_challenge={codeChallenge}&" +
                         $"code_challenge_method=S256&" +
                         $"state={_expectedState}&" +
                         $"prompt=login";

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            StatusText.Text = "Opened in browser - complete login there, then return here";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to open browser: {ex.Message}";
        }
    }
}

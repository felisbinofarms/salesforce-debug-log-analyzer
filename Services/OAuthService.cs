using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace SalesforceDebugAnalyzer.Services;

/// <summary>
/// Handles OAuth 2.0 authentication flow with Salesforce using PKCE (public client)
/// </summary>
public class OAuthService
{
    // Using Salesforce's public client ID for desktop apps (PKCE flow - no secret needed)
    private const string ClientId = "PlatformCLI";
    private const string RedirectUri = "http://localhost:8080/callback";
    
    private const string AuthorizationEndpoint = "https://login.salesforce.com/services/oauth2/authorize";
    private const string TokenEndpoint = "https://login.salesforce.com/services/oauth2/token";
    
    private HttpListener? _httpListener;

    /// <summary>
    /// Start the OAuth 2.0 authorization flow with PKCE
    /// </summary>
    public async Task<OAuthResult> AuthenticateAsync(bool useSandbox = false)
    {
        var authEndpoint = useSandbox 
            ? "https://test.salesforce.com/services/oauth2/authorize" 
            : AuthorizationEndpoint;
        
        var tokenEndpoint = useSandbox 
            ? "https://test.salesforce.com/services/oauth2/token" 
            : TokenEndpoint;

        // Generate state for CSRF protection
        var state = Guid.NewGuid().ToString("N");
        
        // Generate PKCE code verifier and challenge
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Build authorization URL with PKCE
        var authUrl = $"{authEndpoint}?" +
                     $"response_type=code&" +
                     $"client_id={Uri.EscapeDataString(ClientId)}&" +
                     $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
                     $"scope={Uri.EscapeDataString("api refresh_token web")}&" +
                     $"code_challenge={codeChallenge}&" +
                     $"code_challenge_method=S256&" +
                     $"state={state}";

        // Start local HTTP server to receive callback
        var (authCode, actualRedirectUri) = await StartLocalServerAndWaitForCallback(authUrl, state);

        if (string.IsNullOrEmpty(authCode))
        {
            return new OAuthResult { Success = false, Error = "Authorization was cancelled or failed" };
        }

        // Exchange authorization code for access token with PKCE
        return await ExchangeCodeForTokenAsync(authCode, codeVerifier, tokenEndpoint, actualRedirectUri);
    }

    /// <summary>
    /// Manual authentication when you already have an access token (for testing)
    /// </summary>
    public OAuthResult AuthenticateWithToken(string instanceUrl, string accessToken, string refreshToken = "")
    {
        return new OAuthResult
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            InstanceUrl = instanceUrl
        };
    }

    private async Task<(string? code, string redirectUri)> StartLocalServerAndWaitForCallback(string authUrl, string expectedState)
    {
        // Find available port
        var port = FindAvailablePort();
        var redirectUri = $"http://localhost:{port}/callback";

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{port}/");
        
        try
        {
            _httpListener.Start();

            // Open browser to authorization URL (updated with actual port)
            var actualAuthUrl = authUrl.Replace("8080", port.ToString());
            OpenBrowser(actualAuthUrl);

            // Wait for callback
            var tcs = new TaskCompletionSource<string?>();
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
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
                        tcs.SetResult(null);
                    }
                    else if (state != expectedState)
                    {
                        responseString = "<html><body><h1>Authentication Failed</h1><p>Invalid state parameter</p><p>You can close this window.</p></body></html>";
                        tcs.SetResult(null);
                    }
                    else
                    {
                        responseString = "<html><body><h1>Authentication Successful!</h1><p>You can close this window and return to the application.</p></body></html>";
                        tcs.SetResult(code);
                    }

                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            // Timeout after 5 minutes
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                return (null, redirectUri); // Timeout
            }

            return (await tcs.Task, redirectUri);
        }
        finally
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
    }

    private async Task<OAuthResult> ExchangeCodeForTokenAsync(string authCode, string codeVerifier, string tokenEndpoint, string actualRedirectUri)
    {
        using var httpClient = new HttpClient();
        
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", authCode },
            { "client_id", ClientId },
            { "code_verifier", codeVerifier },
            { "redirect_uri", actualRedirectUri }
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

            var tokenResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
            
            if (tokenResponse == null)
            {
                return new OAuthResult { Success = false, Error = "Failed to parse token response" };
            }

            return new OAuthResult
            {
                Success = true,
                AccessToken = tokenResponse["access_token"].ToString() ?? "",
                RefreshToken = tokenResponse.ContainsKey("refresh_token") ? tokenResponse["refresh_token"].ToString() ?? "" : "",
                InstanceUrl = tokenResponse["instance_url"].ToString() ?? "",
                IssuedAt = tokenResponse.ContainsKey("issued_at") ? tokenResponse["issued_at"].ToString() ?? "" : ""
            };
        }
        catch (Exception ex)
        {
            return new OAuthResult { Success = false, Error = $"Exception during token exchange: {ex.Message}" };
        }
    }

    /// <summary>
    /// Generate a cryptographically random code verifier for PKCE (43-128 characters)
    /// </summary>
    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32]; // 32 bytes = 43 characters in base64url
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Generate code challenge from verifier using SHA256
    /// </summary>
    private string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);
        return Base64UrlEncode(hash);
    }

    /// <summary>
    /// Base64 URL encode (RFC 4648 Section 5)
    /// </summary>
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

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // If default browser opening fails, try alternative methods
            try
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            catch
            {
                // Last resort - log the URL for manual opening
                Console.WriteLine($"Please open this URL manually: {url}");
            }
        }
    }
}

/// <summary>
/// Result of OAuth authentication attempt
/// </summary>
public class OAuthResult
{
    public bool Success { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string InstanceUrl { get; set; } = string.Empty;
    public string IssuedAt { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

# OAuth Setup Guide for Salesforce

To enable OAuth authentication with your Salesforce org, you need to create a Connected App.

## Step 1: Create a Connected App in Salesforce

1. Log into your Salesforce org (sandbox or production)
2. Navigate to **Setup** → **Apps** → **App Manager**
3. Click **New Connected App**
4. Fill in the required fields:
   - **Connected App Name**: `Debug Log Analyzer` (or any name)
   - **API Name**: Will auto-populate
   - **Contact Email**: Your email address

5. Enable OAuth Settings:
   - ✅ Check **Enable OAuth Settings**
   - **Callback URL**: `http://localhost:8080/callback`
   - **Selected OAuth Scopes**: Add these scopes:
     - `Full access (full)` OR both of these:
       - `Manage user data via APIs (api)`
       - `Perform requests at any time (refresh_token, offline_access)`

6. Click **Save**
7. Click **Continue**

## Step 2: Get Consumer Key and Secret

1. After creating the app, click **Manage Consumer Details**
2. You may need to verify your identity
3. Copy the **Consumer Key** and **Consumer Secret**

## Step 3: Update the Application

Open `Services/OAuthService.cs` and update lines 13-14:

```csharp
private const string ClientId = "YOUR_CONSUMER_KEY_HERE";
private const string ClientSecret = "YOUR_CONSUMER_SECRET_HERE";
```

Replace `YOUR_CONSUMER_KEY_HERE` and `YOUR_CONSUMER_SECRET_HERE` with the values you copied.

## Step 4: Test the Connection

1. Run the application: `dotnet run`
2. Click **Connect to Salesforce**
3. Choose the **OAuth** tab
4. Check **Use Sandbox** if connecting to a sandbox
5. Click **Login with Browser**
6. Your browser will open → Log in to Salesforce
7. Approve the app permissions
8. The browser will redirect back and the app will authenticate

## Alternative: Manual Token Entry

If you don't want to set up OAuth, you can use the **Manual Token** tab:

### Get Token from SF CLI:
```bash
sf org display --target-org YOUR_ORG_ALIAS --json
```

### Or from Workbench:
1. Go to https://workbench.developerforce.com/
2. Login → Utilities → REST Explorer
3. Copy the access token from the session info

Then paste into the Manual Token tab with your instance URL (e.g., `https://yourorg.my.salesforce.com`)

## Troubleshooting

**Browser doesn't redirect back**: Check that the callback URL is exactly `http://localhost:8080/callback`

**"Invalid client" error**: Verify the Consumer Key and Secret are correct

**"redirect_uri_mismatch"**: The callback URL in the Connected App must match `http://localhost:8080/callback` exactly

**Port already in use**: The app will automatically try ports 8080-8090. If all are in use, close other applications.

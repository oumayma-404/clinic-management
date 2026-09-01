# Google Calendar Integration Setup Guide


> ## ⚠️ Google → App is RETIRED — this guide now covers ONE direction
>
> « Importer depuis Google », `POST /api/googlecalendar/sync-from-google`, the 15-minute
> `GoogleCalendarImportJob` and the `import-settings` gate **no longer exist**. One press was a mass, unbounded,
> irreversible write: 97 days of a practice's calendar became appointment rows, and the past week of them landed on
> « À clôturer » as visits nobody could honestly close — so the cabinet cancelled them and inflated its own
> « taux d'absence ».
>
> **Still true and still needed:** every OAuth step below. App → Google runs inline on appointment create/update and
> needs exactly this setup. **No longer true:** anything describing a pull, a periodic import job, or two-way sync.
>
> The **undo** for imports already made survives (`GET /api/googlecalendar/imports`, `…/revert-preview`,
> `…/revert`). See [`features/calendar-import-revert/notes.md`](features/calendar-import-revert/notes.md).

This guide will help you set up two-way synchronization between the Clinic Management System and Google Calendar.

## Prerequisites

1. A Google Cloud Project
2. Google Calendar API enabled
3. OAuth 2.0 credentials

## Step 1: Create Google Cloud Project and Enable Calendar API

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Enable the Google Calendar API:
   - Navigate to "APIs & Services" > "Library"
   - Search for "Google Calendar API"
   - Click "Enable"

## Step 2: Configure OAuth Consent Screen

**IMPORTANT:** Before creating OAuth credentials, you must configure the OAuth consent screen.

1. Go to "APIs & Services" > "OAuth consent screen"
2. Choose "External" (for development) or "Internal" (if you have Google Workspace)
3. Fill in the required information:
   - **App name**: "Clinic Management" (or your preferred name)
   - **User support email**: Your email
   - **Developer contact information**: Your email
4. Click "Save and Continue"
5. On the "Scopes" page, click "Add or Remove Scopes"
6. Add these scopes:
   - `https://www.googleapis.com/auth/calendar`
   - `https://www.googleapis.com/auth/calendar.events`
7. Click "Update" then "Save and Continue"
8. On the "Test users" page, click "ADD USERS"
9. **Add your email** and any other emails that will need to use the application
10. Click "Save and Continue"
11. On the "Summary" page, click "Back to Dashboard"

**Note:** In test mode, only users added in "Test users" can use the application.

## Step 3: Create OAuth 2.0 Credentials

1. Go to "APIs & Services" > "Credentials"
2. Click "Create Credentials" > "OAuth client ID"
3. Choose "Web application" as the application type
4. **IMPORTANT:** Add the exact authorized redirect URI:
   - For local development: `http://localhost:5000/api/googlecalendar/callback`
   - For production: `https://yourdomain.com/api/googlecalendar/callback`
   - **The URI must match EXACTLY** (including protocol, port, and path)
5. Click "Create"
6. Save the **Client ID** and **Client Secret**

## Step 3.5: Configure Redirect URI in appsettings.json (Optional)

You can optionally configure the redirect URI in `appsettings.json` to ensure consistency:

```json
{
  "GoogleCalendar": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "RedirectUri": "http://localhost:5000/api/googlecalendar/callback",
    "CalendarId": "primary"
  }
}
```

If `RedirectUri` is not specified, the application will automatically construct it from the request.

## Step 4: Configure OAuth 2.0 Redirect URIs (Legacy - for OAuth Playground)

Before getting a refresh token, you need to add the OAuth 2.0 Playground redirect URI to your Google Cloud project:

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Select your project
3. Navigate to **APIs & Services** > **Credentials**
4. Click on your **OAuth 2.0 Client ID** (the one you created in Step 2)
5. Under **Authorized redirect URIs**, click **ADD URI**
6. Add this URI: `https://developers.google.com/oauthplayground`
7. Click **SAVE**

**Important:** This step is required before you can use OAuth 2.0 Playground to get a refresh token.

## Step 4: Get Refresh Token

You need to obtain a refresh token to allow the application to access Google Calendar on behalf of a user.

### Option A: Using OAuth 2.0 Playground (Recommended for testing)

1. Go to [OAuth 2.0 Playground](https://developers.google.com/oauthplayground/)
2. Click the gear icon (⚙️) in the top right
3. Check "Use your own OAuth credentials"
4. Enter your Client ID and Client Secret
5. In the left panel, find "Calendar API v3"
6. Select the following scopes:
   - `https://www.googleapis.com/auth/calendar`
   - `https://www.googleapis.com/auth/calendar.events`
7. Click "Authorize APIs"
8. Sign in with the Google account that has access to the calendar you want to sync
9. Click "Exchange authorization code for tokens"
10. Copy the **Refresh token**

### Option B: Using a Script

You can use a simple script to get the refresh token programmatically. See the example below:

```csharp
// This is a one-time setup script
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;

var clientSecrets = new ClientSecrets
{
    ClientId = "YOUR_CLIENT_ID",
    ClientSecret = "YOUR_CLIENT_SECRET"
};

var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
    clientSecrets,
    new[] { CalendarService.Scope.Calendar, CalendarService.Scope.CalendarEvents },
    "user",
    CancellationToken.None);

Console.WriteLine($"Refresh Token: {credential.Token.RefreshToken}");
```

## Step 6: Configure the Application

Add the following configuration to `appsettings.json`:

```json
{
  "GoogleCalendar": {
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "RefreshToken": "YOUR_REFRESH_TOKEN",
    "CalendarId": "primary"
  }
}
```

**Note:** 
- `CalendarId` can be:
  - `"primary"` - for the primary calendar
  - A specific calendar ID (found in Google Calendar settings)
  - An email address for a shared calendar

## Step 6: Create Database Migration

Run the following command to create a migration for the new `GoogleCalendarEventId` field:

```bash
dotnet ef migrations add AddGoogleCalendarEventId --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

Then apply the migration:

```bash
dotnet ef database update --project api/ClinicManagement.Infrastructure --startup-project api/ClinicManagement.API
```

## How It Works

### Sync from Clinic to Google Calendar

- When you create or update an appointment in the Clinic Management System, it automatically syncs to Google Calendar
- The appointment is created/updated in Google Calendar with:
  - Summary: "Appointment: [Patient Name]"
  - Description: Includes doctor name, notes, status, and patient ID
  - Location: Doctor name (if available)
  - Start/End times: Based on appointment date/time and duration

### Sync from Google Calendar to Clinic

- A background job runs every hour to check for changes in Google Calendar
- If a new event is found in Google Calendar that matches clinic appointment patterns, it creates an appointment in the system
- If an existing event is updated in Google Calendar, the corresponding appointment is updated

### Event Matching

The system matches events using:
1. **Google Calendar Event ID**: If an appointment already has a linked Google Calendar event ID
2. **Patient Name and Time**: If the event summary contains "Appointment: [Patient Name]" and the time matches within 30 minutes

## Troubleshooting

### "Google Calendar credentials are not configured"

- Make sure all required fields are filled in `appsettings.json`
- Check that the configuration section is named exactly `GoogleCalendar`

### "Invalid refresh token"

- The refresh token may have expired or been revoked
- Generate a new refresh token using the OAuth 2.0 Playground
- Make sure you're using the correct Google account

### Events not syncing

- Check the application logs for errors
- Verify that the Google Calendar API is enabled in your Google Cloud project
- Ensure the refresh token has the correct scopes
- Check the Hangfire dashboard (`/hangfire`) to see if the background job is running

### Error 403: access_denied - "App hasn't completed verification"

If you get the error:
> "Access blocked: clinic management hasn't completed Google's verification process"

**Solution:**
1. Go to Google Cloud Console > APIs & Services > OAuth consent screen
2. Click on the "Test users" tab
3. Click "ADD USERS"
4. Add your email and any other emails that will need to use the application
5. Click "ADD" then "SAVE"
6. Wait a few minutes for changes to take effect
7. Try again in OAuth 2.0 Playground

**Note:** In test mode, only users added in "Test users" can authorize the application. For production, you'll need to submit the application for Google verification.

### Calendar ID Issues

- If you want to sync to a specific calendar (not primary), find the calendar ID:
  1. Go to Google Calendar settings
  2. Click on the calendar you want to use
  3. Scroll down to "Integrate calendar"
  4. Copy the "Calendar ID"

## Security Notes

- **Never commit** `appsettings.json` with real credentials to version control
- Use environment variables or Azure Key Vault for production
- The refresh token provides long-term access - keep it secure
- Consider using service accounts for production deployments

## Production Deployment

For production, consider:

1. Using environment variables instead of `appsettings.json`:
   ```bash
   export GoogleCalendar__ClientId="your-client-id"
   export GoogleCalendar__ClientSecret="your-client-secret"
   export GoogleCalendar__RefreshToken="your-refresh-token"
   export GoogleCalendar__CalendarId="primary"
   ```

2. Using Azure Key Vault or similar secret management service

3. Setting up proper OAuth consent screen in Google Cloud Console for production use

4. Implementing proper error handling and retry logic for API calls

5. Monitoring sync status through logs and Hangfire dashboard


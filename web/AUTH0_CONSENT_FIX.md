# Fix Auth0 Consent Screen Issue

If you're seeing a consent page instead of the login page, follow these steps:

## Option 1: Disable Consent in Auth0 Dashboard (Recommended)

1. Go to your Auth0 Dashboard: https://manage.auth0.com/
2. Navigate to **Applications** > Your Application
3. Go to **Settings** tab
4. Scroll down to **Advanced Settings**
5. Click on **OAuth** tab
6. Enable **"Skip consent for first-party applications"**
7. Save changes

## Option 2: Remove Audience (If Not Needed)

If you don't need to call an API with the access token, you can remove the audience:

1. Check your `.env` file for `AUTH0_AUDIENCE`
2. If it's not needed, remove it or leave it empty
3. The code will automatically skip the audience parameter

## Option 3: Configure API to Skip Consent

If you need the audience (API):

1. Go to **APIs** in Auth0 Dashboard
2. Select your API
3. Go to **Settings**
4. Enable **"Skip consent for first-party applications"**
5. Save changes

## Current Code Configuration

The code is now configured to:
- Only include audience if `AUTH0_AUDIENCE` is set
- Force login prompt with `prompt: 'login'`
- Use standard scopes: `openid profile email`

After making changes in Auth0 dashboard, the consent screen should be skipped and users will see the login page directly.





# Google AI Studio Integration Setup

This guide will help you set up Google AI Studio (Gemini) integration for the AI assistant feature in the clinic management system.

## Prerequisites

1. A Google account
2. Access to [Google AI Studio](https://makersuite.google.com/app/apikey)

## Step 1: Get Your Google AI Studio API Key

1. Go to [Google AI Studio](https://makersuite.google.com/app/apikey)
2. Sign in with your Google account
3. Click **"Get API Key"** or **"Create API Key"**
4. Select or create a Google Cloud project
5. Copy your API key (it will look like: `AIza...`)

**Important**: Keep your API key secure and never commit it to version control!

## Step 2: Configure Backend (.NET API)

1. Open `api/ClinicManagement.API/appsettings.json`
2. Add or update the `GoogleAI` section:

```json
{
  "GoogleAI": {
    "ApiKey": "YOUR_GOOGLE_AI_STUDIO_API_KEY_HERE",
    "Model": "gemini-1.5-flash"
  }
}
```

**Available Models (for v1beta API):**
- `gemini-1.5-flash` (default) - Fast and efficient, good for most use cases
- `gemini-1.5-pro` - More capable, better for complex tasks
- `gemini-pro` - Previous generation model (may require v1 API)

**Note**: If you get a 404 error, try:
1. Use `gemini-1.5-flash` with `v1beta` API version (default)
2. Or use `gemini-1.5-pro` with `v1beta` API version
3. Check your API key has access to the model in Google AI Studio

## Step 3: Environment Variables (Optional)

For production or Docker deployments, you can use environment variables instead:

**Backend (.NET):**
```bash
GoogleAI__ApiKey=YOUR_API_KEY_HERE
GoogleAI__Model=gemini-1.5-flash
```

**Note**: In .NET, use double underscores (`__`) for nested configuration.

## Step 4: Test the Integration

1. Start the backend API:
   ```bash
   cd api/ClinicManagement.API
   dotnet run
   ```

2. Start the frontend:
   ```bash
   cd web
   npm run dev
   ```

3. Open the application in your browser
4. Look for the AI chat widget in the bottom-right corner
5. Try asking: "Hello, how can you help me?"

## Features

The AI assistant can help with:
- **Clinic Management**: Questions about appointments, patients, records
- **General Assistance**: Help with using the system
- **Context-Aware**: Can understand when you're working with specific patients or appointments

## API Usage

The AI chat uses the Google Gemini API with the following configuration:
- **Temperature**: 0.7 (balanced creativity)
- **Max Tokens**: 2048
- **Model**: Configurable (default: gemini-1.5-flash)

## Security Notes

1. **Never commit API keys** to version control
2. **Use environment variables** in production
3. **Restrict API key usage** in Google Cloud Console if possible
4. **Monitor usage** to prevent unexpected costs
5. **Rotate keys** if compromised

## Troubleshooting

### "Google AI API key is not configured"
- Make sure you've added the API key to `appsettings.json`
- Check that the key is correct (starts with `AIza`)
- Restart the backend API after adding the key

### "Error calling Google AI API"
- Verify your API key is valid
- Check your internet connection
- Review the API logs for detailed error messages
- Ensure you haven't exceeded API quotas

### Chat not appearing
- Check browser console for errors
- Verify the frontend is running
- Make sure you're logged in (AI chat requires authentication)

## Cost Considerations

Google AI Studio offers:
- **Free tier**: Limited requests per minute
- **Paid tier**: Pay-as-you-go pricing

Check [Google AI Studio Pricing](https://ai.google.dev/pricing) for current rates.

## Next Steps

- Customize the system prompt in `GoogleAIService.cs` to better suit your clinic's needs
- Add patient context when viewing patient details
- Integrate with appointment scheduling for AI-powered suggestions
- Add support for medical document analysis

## Support

For issues or questions:
1. Check the application logs
2. Review Google AI Studio documentation
3. Verify API key permissions in Google Cloud Console


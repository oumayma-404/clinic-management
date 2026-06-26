# Hugging Face AI Setup Guide

This guide will help you set up Hugging Face AI for the clinic management system.

## Prerequisites

1. A Hugging Face account (sign up at [https://huggingface.co](https://huggingface.co))
2. An API token from Hugging Face

## Step 1: Get Your Hugging Face API Token

1. Go to [https://huggingface.co/settings/tokens](https://huggingface.co/settings/tokens)
2. Click "New token" to create a new token
3. Give it a name (e.g., "Clinic Management API")
4. Select "Read" permission (or "Write" if you plan to use custom models)
5. Click "Generate token"
6. **Copy the token immediately** - you won't be able to see it again!

## Step 2: Configure the API

1. Open `api/ClinicManagement.API/appsettings.json`
2. Find the `HuggingFace` section:
   ```json
   "HuggingFace": {
     "ApiKey": "YOUR_HUGGING_FACE_API_KEY_HERE",
     "Model": "meta-llama/Llama-3.1-8B-Instruct"
   }
   ```
3. Replace `YOUR_HUGGING_FACE_API_KEY_HERE` with your API token from Step 1

## Step 3: Choose a Model (Optional)

The default model is `meta-llama/Llama-3.1-8B-Instruct`, which is a good general-purpose chat model.

You can change the model by updating the `Model` field in `appsettings.json`. Some popular alternatives:

- **meta-llama/Llama-3.1-8B-Instruct** (default) - Fast, good for general chat
- **meta-llama/Llama-3.1-70B-Instruct** - More capable but slower
- **mistralai/Mistral-7B-Instruct-v0.2** - Good balance of speed and quality
- **microsoft/Phi-3-mini-4k-instruct** - Very fast, good for simple tasks
- **google/gemma-7b-it** - Google's open model

**Note:** Make sure the model you choose:
- Supports chat/instruct format
- Is available on the Hugging Face Inference API
- Has appropriate licensing for your use case

## Step 4: Test the Integration

1. Restart your API server
2. Open the AI chat in your application
3. Send a test message like "Hello, can you help me?"
4. You should receive a response from the AI

**Note:** The service uses `https://router.huggingface.co` endpoint (the old `api-inference.huggingface.co` endpoint is no longer supported).

## Troubleshooting

### Error: "Hugging Face API key is not configured"
- Make sure you've added your API token to `appsettings.json`
- Check that the token is in the `HuggingFace:ApiKey` field
- Restart the API server after making changes

### Error: "Model is currently loading"
- Some models need to be "warmed up" on Hugging Face's servers
- Wait 30-60 seconds and try again
- The service will automatically retry once after a 5-second delay

### Error: "Model not found"
- Check that the model name is correct
- Verify the model exists on Hugging Face: [https://huggingface.co/models](https://huggingface.co/models)
- Make sure the model supports the Inference API

### Slow Responses
- Hugging Face free tier may have rate limits
- Consider upgrading to a paid plan for better performance
- Try a smaller/faster model if speed is important

### API Rate Limits
- Free tier has limited requests per day
- Consider upgrading to a paid plan for production use
- Monitor your usage at [https://huggingface.co/settings/billing](https://huggingface.co/settings/billing)

## Model Recommendations

### For Development/Testing:
- `microsoft/Phi-3-mini-4k-instruct` - Very fast, good for testing
- `meta-llama/Llama-3.1-8B-Instruct` - Good balance

### For Production:
- `meta-llama/Llama-3.1-70B-Instruct` - Best quality (if speed is acceptable)
- `mistralai/Mistral-7B-Instruct-v0.2` - Good quality and speed balance

## Additional Resources

- [Hugging Face Inference API Documentation](https://huggingface.co/docs/api-inference/index)
- [Available Models](https://huggingface.co/models?pipeline_tag=text-generation)
- [Hugging Face Community](https://discuss.huggingface.co/)


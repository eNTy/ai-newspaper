# Image Generator Azure Function

An Azure Function that generates age-appropriate illustration images for news articles using Claude AI and stores them in Azure Blob Storage.

## Inputs

The function accepts a POST request with the following JSON body:

```json
{
  "articleTitle": "Article Title",
  "simplifiedArticle": "The simplified article text...",
  "audienceAge": 12,
  "storageFolder": "images"
}
```

- `articleTitle` (string, required): The title of the article
- `simplifiedArticle` (string, required): The simplified article text
- `audienceAge` (int, required): The target audience age
- `storageFolder` (string, optional): Azure Storage folder path (default: "images")

## Output

Returns a JSON response with the image URL:

```json
{
  "articleTitle": "Article Title",
  "audienceAge": 12,
  "imageUrl": "https://account.blob.core.windows.net/article-images/images/Article_Title_abc123.svg",
  "storageFolder": "images"
}
```

## Features

- Generates age-appropriate image prompts based on article content
- Uses Claude AI to create detailed image descriptions
- Creates placeholder SVG images (ready for DALL-E/Stable Diffusion integration)
- Uploads images to Azure Blob Storage
- Returns publicly accessible image URL
- Age-specific styling (cartoon for kids, realistic for teens/adults)

## Local Development

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Claude API key
- Azure Storage Emulator (Azurite) for local testing

### Configuration

1. Update `local.settings.json` with your Claude API key:
   ```json
   {
     "Values": {
       "CLAUDE_API_KEY": "your-actual-api-key-here",
       "AzureWebJobsStorage": "UseDevelopmentStorage=true"
     }
   }
   ```

2. Start Azure Storage Emulator:
   ```bash
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

### Running Locally

```bash
cd lambdas/ImageGenerator
dotnet restore
dotnet build
cd bin/Debug/net8.0
func start --no-build --port 7073
```

### Testing

```bash
curl -X POST http://localhost:7073/api/ImageGenerator \
  -H "Content-Type: application/json" \
  -d '{
    "articleTitle": "Scientists Find DNA on Leonardo da Vinci Drawing",
    "simplifiedArticle": "Scientists discovered DNA on an old drawing...",
    "audienceAge": 12,
    "storageFolder": "news-images"
  }'
```

## Production Integration

**Note:** This function currently generates placeholder SVG images with Claude-generated descriptions.

To integrate with actual image generation:

1. **DALL-E Integration** (OpenAI):
   - Replace `CreatePlaceholderImageAsync` with OpenAI API calls
   - Use the Claude-generated description as the DALL-E prompt

2. **Stable Diffusion** (Stability AI):
   - Integrate Stability AI SDK
   - Use text-to-image generation endpoint

3. **Azure AI Image Generation**:
   - Use Azure OpenAI Service
   - Leverage DALL-E 3 through Azure

## Deployment

1. Deploy to Azure:
   ```bash
   func azure functionapp publish <YOUR_FUNCTION_APP_NAME>
   ```

2. Configure application settings:
   ```bash
   az functionapp config appsettings set \
     --name <YOUR_FUNCTION_APP_NAME> \
     --resource-group <YOUR_RESOURCE_GROUP> \
     --settings "CLAUDE_API_KEY=your-key" \
                "AzureWebJobsStorage=your-storage-connection-string"
   ```

## How It Works

1. **Receive Request**: Gets article title, simplified text, and age
2. **Generate Prompt**: Creates age-appropriate image generation prompt
3. **Claude AI**: Generates detailed image description
4. **Create Image**: Produces placeholder SVG (or calls image generation API)
5. **Upload to Storage**: Stores image in Azure Blob Storage
6. **Return URL**: Provides publicly accessible image URL

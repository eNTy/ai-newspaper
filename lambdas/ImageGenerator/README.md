# Image Generator Azure Function

An Azure Function that generates age-appropriate illustration images for news articles using OpenAI DALL-E and stores them in Azure Blob Storage.

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
- `storageFolder` (string, optional): Azure Storage folder path (default: empty, saves to container root)

**Note:** The image is always saved as `image.png` in the specified folder and will overwrite any existing file with the same name.

## Output

Returns a JSON response with the image URL:

```json
{
  "articleTitle": "Article Title",
  "audienceAge": 12,
  "imageUrl": "https://account.blob.core.windows.net/batch-runs/images/image.png",
  "storageFolder": "images"
}
```

## Features

- Generates age-appropriate image prompts based on article content
- Uses OpenAI DALL-E to create actual images
- Uploads PNG images to Azure Blob Storage with fixed filename (`image.png`)
- Returns publicly accessible image URL
- Age-specific styling (cartoon for kids, realistic for teens/adults)
- Supports custom storage folders
- Configurable blob container name via environment variable
- Overwrites existing images in the same folder

## Local Development

### Prerequisites

- .NET 8.0 SDK
- Azure Functions Core Tools v4
- OpenAI API key
- Azure Storage Emulator (Azurite) for local testing

### Configuration

1. Update `local.settings.json` with your configuration:
   ```json
   {
     "Values": {
       "OPENAI_API_KEY": "your-actual-api-key-here",
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "BLOB_CONTAINER_NAME": "batch-runs"
     },
     "Host": {
       "LocalHttpPort": 7071
     }
   }
   ```

2. Start Azure Storage Emulator:
   ```bash
   azurite
   ```

### Running Locally

```bash
cd lambdas/ImageGenerator
func start --port 7071
```

Or use the VS Code debugger configuration.

### Testing

```bash
curl -X POST http://localhost:7071/api/ImageGenerator \
  -H "Content-Type: application/json" \
  -d '{
    "articleTitle": "Scientists Find DNA on Leonardo da Vinci Drawing",
    "simplifiedArticle": "Scientists discovered DNA on an old drawing...",
    "audienceAge": 12,
    "storageFolder": "news-images"
  }'
```

## Deployment

The function is deployed via GitHub Actions workflow. See `.github/workflows/deploy-azure-functions.yml`.

### Environment Variables

Required in Azure:
- `OPENAI_API_KEY`: Your OpenAI API key
- `AzureWebJobsStorage`: Azure Storage connection string
- `BLOB_CONTAINER_NAME`: Container name for images (default: "batch-runs")

Configure via Azure Portal or CLI:
```bash
az functionapp config appsettings set \
  --name <YOUR_FUNCTION_APP_NAME> \
  --resource-group <YOUR_RESOURCE_GROUP> \
  --settings "OPENAI_API_KEY=your-key" \
             "BLOB_CONTAINER_NAME=batch-runs"
```

## How It Works

1. **Receive Request**: Gets article title, simplified text, age, and optional storage folder
2. **Generate Prompt**: Creates age-appropriate image generation prompt using GPT-4o
3. **Refine Prompt**: GPT-4o refines the prompt for optimal DALL-E generation
4. **DALL-E Generation**: Calls OpenAI DALL-E 3 to generate PNG image
5. **Upload to Storage**: Stores image as `image.png` in the specified folder within the configured container
6. **Return URL**: Provides publicly accessible image URL

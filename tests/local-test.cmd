RssProcessor
curl.exe -X POST http://localhost:7073/api/RssProcessor -H "Content-Type: application/json" -d '{\"rssUrl\": \"https://ct24.ceskatelevize.cz/rss/tema/vyber-redakce-84313\", \"audienceAge\": 12}'

ArticleSimplifier
curl.exe -X POST http://localhost:7072/api/ArticleSimplifier -H "Content-Type: application/json" -d '{\"articleUrl\": \"https://ct24.ceskatelevize.cz/clanek/domaci/slova-primo-z-ruske-propagandy-rika-k-turkovym-vyrokum-v-kyjeve-gregorova-369112\", \"audienceAge\": 12}'

ImageGenerator
curl.exe -X POST http://localhost:7071/api/ImageGenerator -H "Content-Type: application/json" -d @ImageGenerator.json

TextToSpeech
curl.exe -X POST http://localhost:7075/api/TextToSpeech -H "Content-Type: application/json" -d @ImageGenerator.json

NewspaperOrchestratorFunction
curl.exe -X POST http://localhost:7074/api/StartNewspaperBatch -H "Content-Type: application/json" -d '{\"rssUrl\": \"https://ct24.ceskatelevize.cz/rss/tema/vyber-redakce-84313\", \"audienceAge\": 12, \"storageFolder\": \"age-12/2026-01-10\" }'



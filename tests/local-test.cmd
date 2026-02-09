NewspaperOrchestratorFunction
curl -s -w "\nHTTP Status: %{http_code}" -X POST http://localhost:7074/admin/functions/DailyNewspaperScheduler_Age16 -H "Content-Type: application/json" -d "{}"
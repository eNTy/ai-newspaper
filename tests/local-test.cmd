NewspaperOrchestratorFunction
F5
Wait
Attach to process

Run in new terminal:
curl.exe -s -w "\nHTTP Status: %{http_code}" -X POST http://localhost:7074/admin/functions/DailyNewspaperScheduler_Age16_Weekdays -H "Content-Type: application/json" -d "{}"

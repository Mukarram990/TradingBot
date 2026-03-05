# TradingBot

Automated crypto trading bot with a .NET backend and a frontend monitoring terminal.

## Current Version
- Backend: stabilized worker-driven pipeline (latest)
- Frontend: **Dashboard Version 1** (`dashboard/`)

## Solution Structure
- `TradingBot/` - ASP.NET API host, controllers, workers, middleware
- `TradingBot.Infrastructure/` - Binance integrations, AI providers, strategy/scanner/indicators
- `TradingBot.Persistence/` - EF Core context, migrations, seeders
- `TradingBot.Domain/` - entities, enums, interfaces
- `Application/` - performance analytics utility
- `dashboard/` - frontend terminal (HTML/CSS/JS)

## Automated Runtime Flow
1. Application startup
   - DB migrations are applied
   - Seeders run (risk profile, trading pairs)
2. Workers start
   - `SignalGenerationWorker` (5 min)
   - `TradeMonitoringWorker` (10 sec)
   - `DailyPerformanceWorker` (midnight UTC)
3. Signal pipeline
   - scan pairs -> calculate indicators -> strategy evaluation -> AI validation -> risk checks -> open trade on Binance -> persist trade/order
4. Monitoring pipeline
   - check open trades -> auto close on TP/SL -> persist closing order and PnL
5. Performance pipeline
   - aggregate closed trades by `ExitTime` into `DailyPerformance`

## Security and Auth
All internal APIs require API key auth.

Required request header:

```http
X-API-KEY: <plain_api_key>
```

Notes:
- Database stores only `ApiKeyHash` (SHA256 Base64), not plaintext key.
- Plain API key must be sent by frontend/client.

## Dashboard Version 1
Path: `dashboard/`

### Features
- System health and worker status
- Account overview and PnL cards
- Active trades, trade history, orders tables (sortable)
- AI verification panel
- Risk panel
- Strategy signals panel
- Live price chart (Chart.js)
- Logs panel
- Auto-refresh with rate-limit-aware polling

### Run Dashboard
- Serve `dashboard/` via static server (for example VS Code Live Server)
- Set API base URL (example: `https://localhost:7282`)
- Enter API key in dashboard connect form

## API Key Hash SQL (Manual Bootstrap)
Use this when you want to create/rotate API credentials manually in SQL Server.

### Generate hash from plaintext key
```sql
DECLARE @PlainKey NVARCHAR(200) = N'your_new_plain_api_key_here';

DECLARE @HashBytes VARBINARY(32) = HASHBYTES('SHA2_256', @PlainKey);
DECLARE @HashBase64 VARCHAR(88) =
    CAST('' AS XML).value('xs:base64Binary(sql:variable("@HashBytes"))', 'VARCHAR(88)');

SELECT @HashBase64 AS ApiKeyHash;
```

### Insert first admin user
```sql
DECLARE @PlainKey NVARCHAR(200) = N'your_new_plain_api_key_here';
DECLARE @HashBytes VARBINARY(32) = HASHBYTES('SHA2_256', @PlainKey);
DECLARE @HashBase64 VARCHAR(88) =
    CAST('' AS XML).value('xs:base64Binary(sql:variable("@HashBytes"))', 'VARCHAR(88)');

INSERT INTO UserAccounts (Username, IsActive, ApiKeyHash, ApiKeyGeneratedAt, CreatedAt)
VALUES ('admin', 1, @HashBase64, SYSUTCDATETIME(), SYSUTCDATETIME());
```

### Rotate existing admin key
```sql
DECLARE @PlainKey NVARCHAR(200) = N'your_new_plain_api_key_here';
DECLARE @HashBytes VARBINARY(32) = HASHBYTES('SHA2_256', @PlainKey);
DECLARE @HashBase64 VARCHAR(88) =
    CAST('' AS XML).value('xs:base64Binary(sql:variable("@HashBytes"))', 'VARCHAR(88)');

UPDATE UserAccounts
SET ApiKeyHash = @HashBase64,
    ApiKeyGeneratedAt = SYSUTCDATETIME(),
    IsActive = 1
WHERE Username = 'admin';
```

## Build and Run
```bash
dotnet build TradingBot.sln
dotnet run --project TradingBot
```

## Operational Notes
- Candles are fetched live from Binance and are not persisted as history.
- Trade execution is protected by risk management and Binance quantity filters.
- Direct raw manual trade-open path is disabled to avoid pipeline bypass.

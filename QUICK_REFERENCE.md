# 🚀 TradingBot - Quick Reference Commands

## 📋 TABLE OF CONTENTS

1. [Build & Run](#build--run)
2. [Database Management](#database-management)
3. [User Secrets (API Keys)](#user-secrets-api-keys)
4. [API Testing](#api-testing)
5. [Project Info](#project-info)
6. [Troubleshooting](#troubleshooting)

---

## BUILD & RUN

### Build Project
```bash
# Full clean rebuild
dotnet clean
dotnet build

# Build specific project
dotnet build TradingBot

# Build with output
dotnet build -o ./bin/Debug
```

### Run Application
```bash
# Run main project
dotnet run --project TradingBot

# Run with watch mode (auto-restart on changes)
dotnet watch --project TradingBot

# Run with specific configuration
dotnet run --project TradingBot --configuration Release

# Run specific port
dotnet run --project TradingBot -- --urls http://localhost:5000
```

### Run Tests
```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test TradingBot.Tests

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "TestClassName.TestMethodName"
```

---

## DATABASE MANAGEMENT

### Entity Framework Commands

```bash
# Install EF Tools (one-time)
dotnet tool install --global dotnet-ef
# OR
dotnet tool install dotnet-ef

# Create new migration
dotnet ef migrations add <MigrationName> -p TradingBot.Persistence -s TradingBot

# Example: Create migration for critical fixes
dotnet ef migrations add AddCriticalFixes -p TradingBot.Persistence -s TradingBot

# List all migrations
dotnet ef migrations list -p TradingBot.Persistence

# Remove last migration (if not applied to DB)
dotnet ef migrations remove -p TradingBot.Persistence

# Update database to latest migration
dotnet ef database update -p TradingBot.Persistence -s TradingBot

# Update to specific migration
dotnet ef database update <MigrationName> -p TradingBot.Persistence -s TradingBot

# Revert to previous migration
dotnet ef database update <PreviousMigrationName> -p TradingBot.Persistence -s TradingBot

# Drop database (WARNING - deletes everything!)
dotnet ef database drop -p TradingBot.Persistence -s TradingBot --force

# Generate migration script (without applying)
dotnet ef migrations script -o script.sql -p TradingBot.Persistence -s TradingBot

# Generate script between migrations
dotnet ef migrations script FromMigration ToMigration -o script.sql -p TradingBot.Persistence
```

### SQL Server Connection
```bash
# Test connection with SQLCMD
sqlcmd -S localhost\MSSQLSERVER01 -U sa -P <password>

# Backup database
sqlcmd -S localhost\MSSQLSERVER01 -U sa -Q "BACKUP DATABASE TradingBotDb TO DISK='C:\backups\tradingbot.bak'"

# Restore database
sqlcmd -S localhost\MSSQLSERVER01 -U sa -Q "RESTORE DATABASE TradingBotDb FROM DISK='C:\backups\tradingbot.bak'"

# Reset database
sqlcmd -S localhost\MSSQLSERVER01 -U sa -Q "DROP DATABASE TradingBotDb"
```

---

## USER SECRETS (API KEYS)

### Initialize Secrets
```bash
# Navigate to main project
cd TradingBot

# Initialize user secrets (one-time per project)
dotnet user-secrets init

# This creates: %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json
```

### Set Secrets
```bash
# Set Binance API Key
dotnet user-secrets set "Binance:ApiKey" "your-actual-testnet-key"

# Set Binance API Secret
dotnet user-secrets set "Binance:ApiSecret" "your-actual-testnet-secret"

# Set custom secret
dotnet user-secrets set "SectionName:KeyName" "value"
```

### List Secrets
```bash
# List all set secrets
dotnet user-secrets list

# Output: (shows masked values)
# Binance:ApiKey = ***
# Binance:ApiSecret = ***
```

### Remove Secret
```bash
# Remove specific secret
dotnet user-secrets remove "Binance:ApiKey"

# Clear all secrets
dotnet user-secrets clear
```

### Verify Secrets Location
```bash
# Windows
dir %APPDATA%\Microsoft\UserSecrets\

# Mac/Linux
ls ~/.microsoft/usersecrets/
```

---

## API TESTING

### Using PowerShell
```powershell
$API = "http://localhost:5000/api"

# Test API connectivity
Invoke-RestMethod -Uri "$API/risk/profile" -TimeoutSec 5

# Create portfolio snapshot
Invoke-RestMethod -Method Post -Uri "$API/portfolio/snapshot"

# Get market price
Invoke-RestMethod -Uri "$API/market/price/BTCUSDT"

# Open trade
$trade = Invoke-RestMethod -Method Post -Uri "$API/trade/open" `
  -ContentType "application/json" `
  -Body '{"symbol":"BTCUSDT","action":1,"entryPrice":43250,"stopLoss":42900,"takeProfit":43600,"quantity":0.001,"accountBalance":10000,"aiConfidence":85}'

# Close trade
Invoke-RestMethod -Method Post -Uri "$API/trade/close/1"

# Get risk profile
Invoke-RestMethod -Uri "$API/risk/profile" | ConvertTo-Json

# Update risk profile
Invoke-RestMethod -Method Put -Uri "$API/risk/profile" `
  -ContentType "application/json" `
  -Body '{"maxRiskPerTradePercent":0.025,"maxDailyLossPercent":0.06,"maxTradesPerDay":6,"circuitBreakerLossCount":2,"isEnabled":true}'
```

### Using curl
```bash
# Create portfolio snapshot
curl -X POST http://localhost:5000/api/portfolio/snapshot

# Get risk profile
curl -X GET http://localhost:5000/api/risk/profile

# Get market price
curl -X GET http://localhost:5000/api/market/price/BTCUSDT

# Open trade
curl -X POST http://localhost:5000/api/trade/open \
  -H "Content-Type: application/json" \
  -d '{"symbol":"BTCUSDT","action":1,"entryPrice":43250,"stopLoss":42900,"takeProfit":43600,"quantity":0.001,"accountBalance":10000,"aiConfidence":85}'

# Close trade
curl -X POST http://localhost:5000/api/trade/close/1

# Update risk profile
curl -X PUT http://localhost:5000/api/risk/profile \
  -H "Content-Type: application/json" \
  -d '{"maxRiskPerTradePercent":0.025,"maxDailyLossPercent":0.06,"maxTradesPerDay":6,"circuitBreakerLossCount":2,"isEnabled":true}'
```

### Using Postman
```
Base URL: http://localhost:5000/api

Endpoints:
GET    /risk/profile
PUT    /risk/profile
POST   /portfolio/snapshot
GET    /market/price/{symbol}
POST   /trade/open
POST   /trade/close/{id}
GET    /trade/{id}
POST   /trade/cancel/{id}
```

---

## PROJECT INFO

### Solution Structure
```bash
# List projects
dotnet sln list

# Add project to solution
dotnet sln add path/to/project.csproj

# Remove project from solution
dotnet sln remove path/to/project.csproj

# Restore all dependencies
dotnet restore

# Clean all output
dotnet clean

# Check project dependencies
dotnet dependency-graph
```

### Package Management
```bash
# List installed packages
dotnet package search TradingBot

# Add NuGet package
dotnet add package PackageName

# Add specific version
dotnet add package PackageName --version 1.0.0

# Remove package
dotnet remove package PackageName

# Update all packages
dotnet package update

# Update specific package
dotnet add package PackageName --version latest
```

### Project Info
```bash
# Show version info
dotnet --version
dotnet --info

# Show project details
dotnet list reference

# Show solution info
dotnet sln info
```

---

## TROUBLESHOOTING

### Build Issues
```bash
# Clear cache and rebuild
dotnet clean
dotnet restore
dotnet build

# Check for errors in detail
dotnet build --verbosity=diagnostic

# Force re-download packages
rd /s /q %USERPROFILE%\.nuget\packages
dotnet restore

# Clear obj/bin folders manually
rd /s /q obj bin
dotnet build
```

### Runtime Issues
```bash
# Run with detailed logging
dotnet run --project TradingBot --loglevel Debug

# Run with console logging
dotnet run --project TradingBot --configuration Debug

# Attach to running process
dotnet run --project TradingBot --no-build -- --debug
```

### Database Issues
```bash
# Check connection
dotnet ef database update -p TradingBot.Persistence -s TradingBot --dry-run

# Show pending migrations
dotnet ef migrations list -p TradingBot.Persistence

# Reset database (destructive!)
dotnet ef database drop -p TradingBot.Persistence --force
dotnet ef database update -p TradingBot.Persistence

# Generate migration script for review
dotnet ef migrations script -o migration.sql -p TradingBot.Persistence
```

### API Key Issues
```bash
# Verify secrets are set
dotnet user-secrets list -p TradingBot

# Clear and re-set secrets
dotnet user-secrets clear
dotnet user-secrets set "Binance:ApiKey" "your-key"

# Check secrets file location
echo %APPDATA%\Microsoft\UserSecrets\

# Manual secrets.json edit
notepad %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json
```

### Port Already in Use
```bash
# Find process using port
netstat -ano | findstr :5000

# Kill process (replace PID)
taskkill /PID <PID> /F

# Use different port
dotnet run --project TradingBot -- --urls http://localhost:5001
```

---

## 🎯 QUICK START CHECKLIST

```bash
# 1. Install EF Tools
dotnet tool install --global dotnet-ef

# 2. Create migration for critical fixes
dotnet ef migrations add AddCriticalFixes -p TradingBot.Persistence -s TradingBot

# 3. Apply migration to database
dotnet ef database update -p TradingBot.Persistence -s TradingBot

# 4. Initialize user secrets
cd TradingBot
dotnet user-secrets init

# 5. Set API keys
dotnet user-secrets set "Binance:ApiKey" "your-key"
dotnet user-secrets set "Binance:ApiSecret" "your-secret"

# 6. Build
dotnet build

# 7. Run
dotnet run --project TradingBot

# 8. Test (in another terminal)
curl http://localhost:5000/api/risk/profile
```

---

## 📊 PERFORMANCE COMMANDS

```bash
# Profile application startup
dotnet run --project TradingBot -- --profile

# Measure build time
Measure-Command { dotnet build }

# Check memory usage
# Windows Task Manager: devenv.exe or dotnet.exe

# Run performance tests
dotnet test TradingBot.Tests --filter "Category=Performance"

# Generate performance report
dotnet test --logger trx:filename=performance.trx
```

---

## 🔍 DEBUG COMMANDS

```bash
# Enable detailed logging
$env:DOTNET_CLI_VERBOSITY = "diagnostic"
dotnet run --project TradingBot

# Break on first-chance exceptions
# Visual Studio: Debug → Windows → Exception Settings

# Attach debugger
# Visual Studio: Debug → Attach to Process → dotnet.exe

# Debug with breakpoints
# Just F5 in Visual Studio!

# Console output during debug
Debug.WriteLine("Message");
Debug.Assert(condition, "Message");
```

---

## 📁 FILE MANAGEMENT

```bash
# View project structure
tree /F /A

# Find large files
dir /s /b | sort /R | head -20

# List recent changes
git log --oneline -20

# See what changed
git diff HEAD~1

# Check status
git status

# Stage changes
git add .
git add path/to/file

# Commit
git commit -m "feat: message"

# Push to remote
git push origin main

# Pull latest
git pull origin main
```

---

## 🎉 SUCCESS INDICATORS

```
✅ Build succeeds         → "Build succeeded"
✅ App starts             → "Now listening on: http://localhost:5000"
✅ Worker runs            → "Trade Monitoring Worker started"
✅ API responds           → curl returns 200 status
✅ DB updated             → "Done" after migrations
✅ Secrets set            → dotnet user-secrets list shows keys
```

---

## 📞 QUICK REFERENCE TABLE

| Task | Command |
|------|---------|
| Build | `dotnet build` |
| Run | `dotnet run --project TradingBot` |
| Test | `dotnet test` |
| Create Migration | `dotnet ef migrations add Name -p TradingBot.Persistence -s TradingBot` |
| Update DB | `dotnet ef database update -p TradingBot.Persistence -s TradingBot` |
| Set Secret | `dotnet user-secrets set "Key" "Value"` |
| List Secrets | `dotnet user-secrets list` |
| Clean | `dotnet clean` |
| Restore | `dotnet restore` |
| Add Package | `dotnet add package Name` |

---

**Last Updated**: February 23, 2025  
**Status**: ✅ Complete and Ready  
**Next Phase**: Database Migration & Testing

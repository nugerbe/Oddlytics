# NFL Odds Movement Tracker

A Discord bot that tracks NFL odds movement across major sportsbooks and generates charts on demand using natural language queries.

## Features

- Natural language queries powered by Claude
- Real-time odds from DraftKings, FanDuel, BetMGM, and Caesars
- Supports spreads, totals, and moneylines
- Chart generation via QuickChart
- Redis caching to minimize API calls

## Architecture

```
User Message → Discord Bot → Claude (parse intent) → Check Redis Cache
                                                          ↓
                    Return Chart ← Generate Chart ← Fetch from The-Odds-API
```

## Prerequisites

1. **.NET 8 SDK** - https://dotnet.microsoft.com/download
2. **Redis** - Local instance or cloud (Redis Cloud free tier works)
3. **Discord Bot** - Create at https://discord.com/developers/applications
4. **Claude API Key** - From https://console.anthropic.com
5. **The-Odds-API Key** - Free tier at https://the-odds-api.com

## Setup

### 1. Clone and restore

```bash
cd odds-tracker
dotnet restore
```

### 2. Configure secrets

For development, use user secrets:

```bash
cd src/OddsTracker
dotnet user-secrets init
dotnet user-secrets set "AppSettings:DiscordToken" "your-discord-token"
dotnet user-secrets set "AppSettings:ClaudeApiKey" "your-claude-key"
dotnet user-secrets set "AppSettings:OddsApiKey" "your-odds-api-key"
dotnet user-secrets set "AppSettings:RedisConnection" "localhost:6379"
```

Or set environment variables:

```bash
export AppSettings__DiscordToken="your-discord-token"
export AppSettings__ClaudeApiKey="your-claude-key"
export AppSettings__OddsApiKey="your-odds-api-key"
export AppSettings__RedisConnection="localhost:6379"
```

### 3. Create Discord Bot

1. Go to https://discord.com/developers/applications
2. Create New Application
3. Go to Bot tab, create bot, copy token
4. Enable MESSAGE CONTENT INTENT under Privileged Gateway Intents
5. Go to OAuth2 → URL Generator
6. Select scopes: `bot`
7. Select permissions: `Send Messages`, `Attach Files`, `Read Message History`
8. Copy URL and invite bot to your server

### 4. Run Redis

```bash
# Docker
docker run -d -p 6379:6379 redis:alpine

# Or install locally
# macOS: brew install redis && brew services start redis
# Ubuntu: sudo apt install redis-server
```

### 5. Run the bot

```bash
cd src/OddsTracker
dotnet run
```

## Usage

Mention the bot or DM it with queries like:

- `@OddsBot Show me Chiefs spread movement`
- `@OddsBot Eagles moneyline over the past week`
- `@OddsBot How has the over/under moved for the Bills game`
- `@OddsBot 49ers spread last 5 days`

## Project Structure

```
odds-tracker/
├── OddsTracker.sln
└── src/
    └── OddsTracker/
        ├── Program.cs              # Entry point, DI setup
        ├── appsettings.json        # Configuration template
        ├── Models/
        │   ├── OddsModels.cs       # Data models
        │   └── AppSettings.cs      # Config model
        └── Services/
            ├── IntentParser.cs     # Claude-powered NLP
            ├── OddsService.cs      # The-Odds-API client
            ├── CacheService.cs     # Redis cache wrapper
            ├── ChartService.cs     # QuickChart integration
            ├── OddsOrchestrator.cs # Ties it all together
            └── DiscordBotService.cs# Discord bot
```

## Extending

### Adding player props

1. Upgrade to paid The-Odds-API tier (or switch to SportsData.io)
2. Add new `MarketType` enum values
3. Update `TheOddsApiService` to fetch props markets
4. Update `ClaudeIntentParser` system prompt with new market types

### Adding more sports

1. Update `TheOddsApiService` to accept sport parameter
2. Add sport detection to `ClaudeIntentParser`
3. Update Discord command examples

## API Costs

- **The-Odds-API**: 500 free credits/month (~500 requests)
- **Claude API**: ~$0.003 per query (Sonnet)
- **QuickChart**: Free for basic usage
- **Redis**: Free tier available on Redis Cloud

## License

MIT

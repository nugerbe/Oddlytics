# OddsTracker Platform - Scaling & Operations Guide

## Scaling Considerations (50 → 5,000 Users)

### Current Architecture (50 users)
- Single Azure App Service (B1)
- Single Redis instance (Basic)
- Azure SQL Basic tier
- Estimated cost: ~$50/month

### Growth Phase (500 users)
- Azure App Service (S1)
- Redis Cache (Standard C1)
- Azure SQL S1
- Estimated cost: ~$150/month

### Scale Phase (5,000 users)
- Azure Container Apps (auto-scale)
- Redis Cache (Premium P1)
- Azure SQL S3 or Cosmos DB
- Azure Functions for background jobs
- Estimated cost: ~$500/month

## Cost Optimization

### API Costs (Predictable)

| Service | Cost Model | Optimization |
|---------|------------|--------------|
| Odds API | Per request | Cache raw odds 30s, poll smart intervals |
| Claude API | Per token | Cache explanations 1hr, limit to Core+ |
| QuickChart | Free tier | Cache charts 15min |

### Estimated Monthly API Costs at Scale

```
Odds API (paid tier):
- 30 polls/min × 60 min × 24 hr × 30 days = 1.3M requests
- With smart caching: ~100K requests = $100/month

Claude API:
- 500 explanations/day × 30 days = 15K calls
- ~$0.003/call = $45/month

Total API: ~$150/month at 5K users
```

## Caching Strategy Summary

### Cache Layers

```
Layer 1: In-Memory (per instance)
├── Hot data: Current fingerprints, active alerts
├── TTL: 10 seconds
└── Purpose: Reduce Redis round-trips

Layer 2: Redis (shared)
├── Raw odds: 30 seconds
├── Fingerprints: 24 hours
├── Confidence scores: 5 minutes
├── AI explanations: 1 hour
├── User subscriptions: 5 minutes
└── Alert deduplication: 1 hour

Layer 3: Database (persistent)
├── Historical signals: Forever
├── User data: Forever
└── Performance stats: Forever
```

### Cache Invalidation Rules

| Event | Invalidate |
|-------|------------|
| New odds arrive | Raw odds cache |
| Fingerprint changes | Confidence score cache |
| Market closes | All caches for event |
| Subscription changes | User subscription cache |
| Payment fails | User subscription cache |

## Polling Strategy

### Smart Interval Selection

```csharp
TimeSpan GetPollingInterval(DateTime gameTime)
{
    var timeToGame = gameTime - DateTime.UtcNow;
    
    return timeToGame switch
    {
        < 2 hours => 30 seconds,    // Active/imminent
        < 24 hours => 2 minutes,    // Upcoming
        _ => 15 minutes             // Future
    };
}
```

### API Call Budget

```
Free tier: 500 requests/month
Paid tier: 10,000+ requests/month

Budget allocation:
- 60% for active games (high frequency)
- 30% for upcoming games (medium frequency)
- 10% for user queries (on-demand)
```

## Idempotency Strategy

### Fingerprint-Based Change Detection

```csharp
// Only process if fingerprint hash changed
string GenerateHash(MarketFingerprint fp)
{
    return SHA256(
        fp.ConsensusLine + 
        fp.FirstMoverBook + 
        fp.ConfirmingBooks +
        string.Join(",", fp.BookSnapshots.Select(b => $"{b.Name}:{b.Line}"))
    );
}

// Stored in Redis with 1-hour TTL
bool HasProcessed(string hash) => Redis.Exists($"processed:{hash}");
void MarkProcessed(string hash) => Redis.Set($"processed:{hash}", "1", 1hr);
```

### Alert Deduplication

```
Dedupe Key Format: {eventId}:{marketType}:{alertType}:{confidenceLevel}
Example: "abc123:spread:SharpActivity:High"

Rules:
1. Same key within 1 hour = duplicate
2. Higher confidence same market = allow (escalation)
3. Different alert type same market = allow
```

## Assumptions & Risks

### Assumptions

1. **Odds API reliability**: 99.9% uptime, <500ms latency
2. **User behavior**: Peak usage during game days (Sun/Mon/Thu)
3. **Alert frequency**: ~10-20 material changes per day per sport
4. **Subscription distribution**: 70% Starter, 20% Core, 10% Sharp

### Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Odds API outage | No new data | Graceful degradation, show cached |
| Redis failure | Lost state | Azure Redis HA, local fallback |
| Alert spam | User churn | Dedup + cooldowns + testing |
| Payment fraud | Revenue loss | Stripe Radar, role verification |
| Rate limit abuse | API costs | Per-user limits, tier enforcement |

### Technical Debt Considerations

1. **Eventual consistency**: Fingerprints may lag by polling interval
2. **Clock skew**: Use UTC everywhere, NTP sync
3. **Partial failures**: Alert sent but not marked = duplicate possible

## Monitoring & Observability

### Key Metrics

```
Business:
- Daily active users by tier
- Alerts sent per day
- Query success rate
- Subscription conversions

Technical:
- API call count (Odds, Claude)
- Cache hit rate
- Alert latency (detection → delivery)
- Error rate by service
```

### Azure Monitor Alerts

```
Critical:
- Odds API error rate > 5%
- Alert delivery failure > 1%
- Redis connection failures

Warning:
- API quota > 80%
- Cache hit rate < 70%
- Query latency p95 > 2s
```

## Deployment Checklist

### Pre-Launch
- [ ] Configure Key Vault secrets
- [ ] Set up Stripe webhooks
- [ ] Create Discord roles (Starter, Core, Sharp)
- [ ] Create Discord channels (#odds-alerts, #sharp-signals)
- [ ] Test tier enforcement
- [ ] Load test with 100 concurrent users

### Post-Launch
- [ ] Monitor error rates
- [ ] Review API costs weekly
- [ ] Gather user feedback
- [ ] Tune alert thresholds based on feedback

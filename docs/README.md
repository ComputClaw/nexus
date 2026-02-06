# Nexus Documentation

Data ingestion service for OpenClaw agents.

## Functional Areas

| Area | Purpose | Documentation |
|------|---------|---------------|
| **Authentication** | Security model and API keys | [📄](authentication.md) |
| **Email & Calendar** | Microsoft Graph integration | [📄](email-calendar.md) |
| **Meetings** | Fireflies.ai transcripts | [📄](meetings.md) |
| **Sessions** | OpenClaw session storage | [📄](sessions.md) |  
| **Agent Integration** | How agents consume data | [📄](agent-integration.md) |
| **Administration** | Management and monitoring | [📄](administration.md) |

## Quick Start

**For Agents:**
1. Configure credentials in sync script
2. Run `node scripts/nexus-sync.js --with-body`
3. Process files from `data/inbox/`

**For Administrators:**
1. Set up Azure Function App with required settings
2. Bootstrap Graph subscriptions
3. Configure webhooks for external services

## Data Flow

```
External Sources → Nexus Functions → Table/Blob Storage → Agent APIs → Local Processing
```

**Sources:** Microsoft Graph, Fireflies.ai, OpenClaw sessions, generic webhooks
**Storage:** Azure Table Storage (metadata) + Blob Storage (full content)  
**Consumption:** REST APIs + sync scripts + local worker

## Status

| Integration | Status | Notes |
|-------------|--------|-------|
| Email/Calendar | ✅ Live | Microsoft Graph webhooks |
| Meetings | ⬜ Pending | Requires Fireflies API key |
| Sessions | 📝 Designed | Endpoint + worker spec complete |
| Webhooks | 📝 Designed | Generic webhook receiver |
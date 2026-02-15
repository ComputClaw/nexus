# Nexus Memory

Project learnings, decisions, and context that should persist across sessions.

## Key Decisions

- **2026-02-15**: Removed hardcoded agent validation — any agent name is accepted in webhook relay and feed management
- **2026-02-15**: Named function keys per service (graph, github, fireflies) instead of sharing the default key
- **2026-02-15**: Custom domain `nexus.comput.sh` via Cloudflare (DNS only, not proxied) + Azure managed SSL
- **2026-02-15**: Graph subscriptions route through `/api/webhook/{agent}/graph/{type}`, not a separate `/api/notifications` endpoint
- **2026-02-15**: SubscriptionService stores `AgentName` and `WebhookType` in tracking table for correct recreation

## Azure Resources

- **Resource group**: OpenClaw
- **Function app**: nexusrelay (Linux, Consumption plan, West Europe)
- **App registration**: NexusClaw (`533e8946-33b4-4643-94c0-442fd310fbb7`)
- **App registration permissions**: Mail.Read, Calendars.Read (application)
- **Storage account**: nexusrelay

## Cloudflare

- **Zone**: comput.sh (`0f9d8123b221a4df410a4215c227a0ac`)
- **Account**: `44b45c6ec07a24597e061138627d8ab1`
- **DNS**: nexus.comput.sh CNAME → nexusrelay.azurewebsites.net (DNS only)
- **API token**: requires account-level DNS permissions + IP allowlisting

## Active Graph Subscriptions

- Email: `users/mb@muneris.dk/messages` → agent `comput`, type `email`
- Calendar: `users/mb@muneris.dk/events` → agent `comput`, type `calendar`
- Renewed daily at 08:00 UTC by SubscriptionRenewal timer

## Tables in Use

| Table | Purpose |
|---|---|
| Items | Processed webhook data for agent consumption |
| Subscriptions | Graph subscription tracking for renewal |
| FeedConfigs | Atom/RSS feed configuration |
| Whitelist | Sender whitelist (domain + email) |
| PendingEmails | Emails from non-whitelisted senders |

## Lessons Learned

- PowerShell commands don't work in this shell — use Unix syntax
- `az functionapp config appsettings` uses `__` as nested config separator
- Azure managed SSL certs take ~30 seconds to provision
- Cloudflare API tokens need account-level DNS permissions (not just zone-level) to manage records
- Graph subscription validation requires the endpoint to be deployed and responding before creating the subscription
- The workflow must point at `src/Nexus.Ingest.csproj` (not `src/`) because the folder contains both a .sln and .csproj

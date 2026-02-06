# Generic Webhook Ingestion for Nexus

Design for a generic webhook endpoint that allows external services to push data to Nexus, routed to specific agents.

## Use Case

put.io transfer completes → calls Nexus webhook → FlickClaw consumes and announces to group.

But generic enough for any external service (GitHub, Stripe, etc.) to push data to any agent.

## API Design

### Endpoint

```
POST /api/webhook/{agentId}
```

**Path parameters:**
- `agentId` — target agent (e.g., `flickclaw`, `stewardclaw`, `main`)

**Query parameters:**
- `source` — identifier for the calling service (e.g., `putio`, `github`)

**Headers:**
- `X-Webhook-Secret` — optional secret for verification (per-agent or per-source)
- Standard headers captured for debugging

**Body:**
- Any JSON payload — stored as-is

### Example

```bash
POST /api/webhook/flickclaw?source=putio
Content-Type: application/json
X-Webhook-Secret: abc123

{
  "transfer_id": 12345,
  "file_id": 67890,
  "name": "Movie.2024.1080p.mkv",
  "size": 5000000000,
  "parent_id": 930386098,
  "status": "COMPLETED"
}
```

## Storage

**Table Storage** — simple, raw JSON in a column.

### Table: WebhookItems

| Field | Type | Description |
|-------|------|-------------|
| PartitionKey | string | `{agentId}` |
| RowKey | string | `{timestamp}_{guid}` |
| Source | string | e.g., `putio`, `github` |
| ReceivedAt | datetime | When Nexus received it |
| Payload | string (JSON) | Raw webhook body — stored as-is, not parsed |
| Processed | bool | Has agent consumed this item? |

**Note:** Max entity size ~1MB. Webhook payloads are typically small (< 10KB). No blob storage needed.

## Security

1. **Per-source secrets** — each `{agentId}/{source}` combo can have a secret
2. **Stored in Function App settings** — `WEBHOOK_SECRET_flickclaw_putio=xxx`
3. **Validation** — if secret configured, reject requests without matching `X-Webhook-Secret`
4. **No secret = open** — for low-risk sources (internal services, etc.)

## Consumer API

Worker (not agents) pulls items from Nexus:

```
GET /api/webhook/pending
  → Returns items grouped by agentId, with full payloads

DELETE /api/webhook/items
  → Body: {"ids": ["id1", "id2"]}
  → Marks items as processed (removes from table)
```

Agents don't call the API — worker writes to their local inbox.

## Agent Notification

Instead of agents polling via cron, a local worker pushes to agents when new data arrives.

See [worker.md](./worker.md) for the worker design.

**Summary:**
1. Worker polls Nexus for pending items
2. Worker writes JSON files to agent's `inbox/webhooks/`
3. Worker spawns isolated task via `sessions_spawn`
4. Agent reads local files, processes, archives
5. Worker marks items processed in Nexus

## FlickClaw Integration

Once webhook ingestion exists:

1. Set put.io `callback_url` to `https://nexusassistant.../api/webhook/flickclaw?source=putio`
2. Local worker detects new items, spawns FlickClaw task
3. FlickClaw fetches details, announces to plex-3000 group, refreshes inventory

## Implementation Phases

### Phase 1: Webhook Receiver
- [ ] New HTTP trigger: `WebhookIngest`
- [ ] Store to `WebhookItems` table
- [ ] Basic secret validation
- [ ] Return 200 OK

### Phase 2: Consumer API (for worker)
- [ ] GET `/webhook/pending` — pending items grouped by agent, with full payloads
- [ ] DELETE `/webhook/items` — bulk delete by IDs

### Phase 3: Local Worker (in nexus repo)
- [ ] `worker/nexus-worker.py` — polls Nexus, writes to agent inboxes
- [ ] `sessions_spawn` to notify agents
- [ ] Run as systemd service or cron (every 5 min)
- [ ] Config: agent workspace paths, Nexus API key

### Phase 4: put.io Integration
- [ ] Configure put.io `callback_url` 
- [ ] FlickClaw task: fetch items, announce to group, refresh inventory

## Decisions

- **Storage**: Table Storage (simple, raw JSON column)
- **Parsing**: None — payload stored as-is
- **Notification**: Local worker + `sessions_spawn` (not agent polling)
- **Data delivery**: Worker writes to agent's local inbox (agent reads files, no API)
- **Session type**: Isolated (not main DM) — agent processes in background

---

*Status: Design ready*

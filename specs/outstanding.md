# Outstanding Items

Tracking open items, decisions needed, and gaps across Nexus components.

---

## Worker

### Implementation

| # | Item | Notes | Status |
|---|------|-------|--------|
| W1 | Core worker implementation | Python scheduler, job framework | ⬜ |
| W2 | session_upload job | Upload completed sessions | ⬜ |
| W3 | webhook_pull job | Deliver webhook items | ⬜ |
| W4 | FlickClaw session path | Docker-sandboxed — verify host can access | ⬜ |
| W5 | Backfill historical sessions | Upload existing sessions on first run | ⬜ |

### Error Handling

| # | Item | Notes | Status |
|---|------|-------|--------|
| W6 | Upload failure retry | Exponential backoff? Max retries? | ⬜ |
| W7 | Idempotency | Track uploaded session IDs | ⬜ |
| W8 | Large files | Streaming for long sessions | ⬜ |

### Operational

| # | Item | Notes | Status |
|---|------|-------|--------|
| W9 | Logging | Stdout to systemd journal | ⬜ |
| W10 | Health check | How to know worker is running | ⬜ |
| W11 | Alerting | Notify on repeated failures | ⬜ |

---

## Sessions Endpoint

| # | Item | Notes | Status |
|---|------|-------|--------|
| S1 | Endpoint implementation | POST /api/sessions | ✅ |
| S2 | Storage model | Table with date partitioning | ✅ |
| S3 | Validation | Required fields, UUID format | ✅ |
| S4 | Size limit | 1MB max transcript | ✅ |
| S5 | Deduplication | 409 Conflict on duplicate | ✅ |

---

## Webhook Ingestion

| # | Item | Notes | Status |
|---|------|-------|--------|
| H1 | Webhook endpoint | POST /webhook/ingest | ⬜ |
| H2 | WebhookItems table | Store pending items | ⬜ |
| H3 | Worker pull job | Deliver to agent inboxes | ⬜ |
| H4 | put.io integration | Register webhook URL | ⬜ |

---

## Analytics (Future)

| # | Item | Notes | Status |
|---|------|-------|--------|
| A1 | Query endpoints | Token usage per agent/day/model | ⬜ |
| A2 | Dashboard | UI for usage visualization | ⬜ |
| A3 | Cost alerts | Notify on spend thresholds | ⬜ |

---

## Priority Order

**Immediate:**
1. Worker core implementation
2. session_upload job implementation
3. Deploy and test end-to-end

**Next:**
4. Webhook ingestion endpoint
5. webhook_pull job implementation
6. put.io integration

**Later:**
7. Analytics endpoints

---

*Last updated: 2026-02-06*
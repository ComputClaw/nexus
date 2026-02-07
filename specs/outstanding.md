# Outstanding Items

Tracking open items, decisions needed, and gaps across Nexus components.

---

## Worker

### Implementation

| # | Item | Notes | Status |
|---|------|-------|--------|
| W1 | Core worker implementation | Python scheduler, job framework | ✅ |
| W2 | session_upload job | Upload completed sessions | ✅ |
| W3 | webhook_pull job | Deliver webhook items | ⬜ |
| W4 | FlickClaw session path | Docker-sandboxed — verify host can access | ⬜ |
| W5 | Backfill historical sessions | Upload existing sessions on first run | ⬜ |
| W6 | Deploy worker | Deploy to OpenClaw host, test end-to-end | ⬜ |

### Error Handling

| # | Item | Notes | Status |
|---|------|-------|--------|
| W7 | Upload failure retry | Exponential backoff? Max retries? | ⬜ |
| W8 | Idempotency | 409 treated as success, archives file | ✅ |
| W9 | Large files | Worker pre-checks file size (10MB limit) | ✅ |

### Operational

| # | Item | Notes | Status |
|---|------|-------|--------|
| W10 | Logging | Stdout to systemd journal | ⬜ |
| W11 | Health check | How to know worker is running | ⬜ |
| W12 | Alerting | Notify on repeated failures | ⬜ |

---

## Sessions Endpoint

| # | Item | Notes | Status |
|---|------|-------|--------|
| S1 | Endpoint implementation | POST /api/sessions | ✅ |
| S2 | Storage model | Blob Storage (sessions/inbox/{agentId}/{sessionId}.jsonl) | ✅ |
| S3 | Validation | Required fields, UUID format | ✅ |
| S4 | Size limit | 10MB max transcript | ✅ |
| S5 | Deduplication | 409 Conflict on duplicate (overwrite: false) | ✅ |

---

## Deploy

| # | Item | Notes | Status |
|---|------|-------|--------|
| D1 | Fix deploy workflow | Path changed from src/Nexus.Ingest to src/function-app | ⬜ |
| D2 | Deploy worker to host | systemd service on OpenClaw host | ⬜ |

---

## Webhook Ingestion

| # | Item | Notes | Status |
|---|------|-------|--------|
| H1 | Webhook endpoint | POST /api/webhook/{agentId} | ⬜ |
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
1. Fix deploy workflow (D1)
2. Deploy worker and test end-to-end (W6, D2)

**Next:**
3. Webhook ingestion endpoint (H1, H2)
4. webhook_pull job (W3, H3)
5. put.io integration (H4)

**Later:**
6. Analytics endpoints

---

*Last updated: 2026-02-07*

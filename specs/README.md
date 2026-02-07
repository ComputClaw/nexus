# Nexus Specifications

Technical specifications for developing the Nexus data ingestion service.

## Structure

```
specs/
├── function-app-sessions.md        # Session transcript storage
├── function-app-administration.md   # Admin, subscriptions, monitoring
├── worker.md                        # Worker architecture
├── job-session-upload.md            # Upload session transcripts job
├── job-webhook-pull.md              # Pull webhook items job
└── outstanding.md                   # Open items tracking
```

## Function App (Azure Functions)

| Area | Status | Specification |
|------|--------|---------------|
| **Sessions** | ✅ Implemented | [📄](function-app-sessions.md) |
| **Administration** | ✅ Implemented | [📄](function-app-administration.md) |

## Worker (Python)

| Area | Status | Specification |
|------|--------|---------------|
| **Worker core** | ✅ Implemented | [📄](worker.md) |
| **session_upload job** | ✅ Implemented | [📄](job-session-upload.md) |
| **webhook_pull job** | ⬜ Not implemented | [📄](job-webhook-pull.md) |

Worker core is in `src/worker/` (entry point, config, scheduler). Jobs are in `src/jobs/` (base class, session_upload). Needs end-to-end testing and deployment.

## Implementation Priority

**Immediate:**
1. Fix deploy workflow (path changed to `src/function-app`)
2. Deploy worker and test end-to-end

**Next:**
3. Webhook ingestion endpoint (function app)
4. webhook_pull job

See [outstanding.md](outstanding.md) for detailed tracking.

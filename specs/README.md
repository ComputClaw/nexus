# Nexus Specifications

Technical specifications for developing the Nexus data ingestion service.

## Structure

```
specs/
├── server/              # Cloud service (Azure Functions)
│   ├── sessions.md      # Session transcript storage
│   └── administration.md
├── client/              # Worker and jobs (Python)
│   ├── worker.md        # Worker architecture
│   └── jobs/
│       ├── session-upload.md
│       └── webhook-pull.md
└── outstanding.md       # Open items tracking
```

## Server (Cloud Service)

Azure Functions that receive and store data.

| Area | Status | Specification |
|------|--------|---------------|
| **Sessions** | ✅ Implemented | [📄](server/sessions.md) |
| **Administration** | ✅ Implemented | [📄](server/administration.md) |

## Client (Worker)

Python service that syncs data between OpenClaw host and Nexus.

| Area | Status | Specification |
|------|--------|---------------|
| **Worker** | 📝 Spec Complete | [📄](client/worker.md) |
| **session_upload job** | 📝 Spec Complete | [📄](client/jobs/session-upload.md) |
| **webhook_pull job** | 📝 Spec Complete | [📄](client/jobs/webhook-pull.md) |

## Implementation Priority

**Immediate:**
1. Worker core implementation
2. session_upload job
3. Deploy and test end-to-end

**Next:**
4. Webhook ingestion endpoint (server)
5. webhook_pull job (client)

See [outstanding.md](outstanding.md) for detailed tracking.
# Nexus Specifications

Technical specifications for developing the Nexus data ingestion service.

## Structure

```
specs/
├── server/              # Cloud service (Azure Functions)
│   ├── sessions.md      # Session transcript storage
│   ├── authentication.md
│   ├── agent-integration.md
│   └── administration.md
├── client/              # Worker and jobs (Python)
│   ├── worker-spec.md   # Worker architecture
│   └── jobs/
│       ├── session-upload-spec.md
│       └── webhook-pull-spec.md
└── outstanding.md       # Open items tracking
```

## Server (Cloud Service)

Azure Functions that receive and store data.

| Area | Status | Specification |
|------|--------|---------------|
| **Sessions** | ✅ Implemented | [📄](server/sessions.md) |
| **Authentication** | ✅ Implemented | [📄](server/authentication.md) |
| **Agent Integration** | ✅ Implemented | [📄](server/agent-integration.md) |
| **Administration** | ✅ Implemented | [📄](server/administration.md) |

## Client (Worker)

Python service that syncs data between OpenClaw host and Nexus.

| Area | Status | Specification |
|------|--------|---------------|
| **Worker** | 📝 Spec Complete | [📄](client/worker-spec.md) |
| **session_upload job** | 📝 Spec Complete | [📄](client/jobs/session-upload-spec.md) |
| **webhook_pull job** | 📝 Spec Complete | [📄](client/jobs/webhook-pull-spec.md) |

## Implementation Priority

**Immediate:**
1. Worker core implementation
2. session_upload job
3. Deploy and test end-to-end

**Next:**
4. Webhook ingestion endpoint (server)
5. webhook_pull job (client)

See [outstanding.md](outstanding.md) for detailed tracking.
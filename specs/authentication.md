# Authentication

Authentication model for all Nexus endpoints.

## Function Key Authentication

All endpoints require an Azure Function key:

```
GET /api/items?code=<function-key>
POST /api/sessions?code=<function-key>
```

**Function keys are Azure-managed** and control access to the entire Function App.

## Webhook Authentication

Webhook endpoints use source-specific validation:

### Microsoft Graph
- **ClientState validation** - `clientState` header must match subscription
- **Subscription validation** - `subscriptionId` must be known

### Fireflies.ai
- **HMAC signature** - `X-Fireflies-Signature` header validated against webhook secret

## Security Model

| Endpoint Type | Function Key | Special |
|---------------|--------------|---------|
| **Webhooks** | ✅ Required | Source validation |
| **Agent APIs** | ✅ Required | - |
| **Admin APIs** | ✅ Required | - |

## Configuration

**Function keys** — Managed in Azure Portal under Function App > App keys

**Webhook secrets** — Stored in Function App settings:
- `GraphClientState` - Microsoft Graph validation token
- `FirefliesWebhookSecret` - Fireflies HMAC secret

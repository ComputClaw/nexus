# Authentication

Authentication model for all Nexus endpoints.

## Function Key Authentication

All endpoints require an Azure Function key:

```
GET /api/items?code=<function-key>
POST /api/sessions?code=<function-key>
```

**Function keys are Azure-managed** and control access to the entire Function App.

## Application Key Authentication

Most APIs also require an application key in the header:

```
X-Api-Key: <application-key>
```

**Application keys are Nexus-specific** and provide additional security.

## Webhook Authentication

Webhook endpoints use source-specific validation:

### Microsoft Graph
- **ClientState validation** - `clientState` header must match subscription
- **Subscription validation** - `subscriptionId` must be known

### Fireflies.ai
- **HMAC signature** - `X-Fireflies-Signature` header validated against webhook secret

## Security Model

| Endpoint Type | Function Key | App Key | Special |
|---------------|--------------|---------|---------|
| **Webhooks** | ✅ Required | ❌ None | Source validation |
| **Agent APIs** | ✅ Required | ✅ Required | - |
| **Admin APIs** | ✅ Required | ✅ Required | - |

## Configuration

**Function keys** — Managed in Azure Portal under Function App > App keys

**Application key** — Stored in Function App settings as `IngestApiKey`

**Webhook secrets** — Stored in Function App settings:
- `GraphClientState` - Microsoft Graph validation token
- `FirefliesWebhookSecret` - Fireflies HMAC secret
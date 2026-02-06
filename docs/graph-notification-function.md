# GraphNotificationFunction

Receives webhook notifications from Microsoft Graph for email and calendar events.

## Endpoint

```
POST /api/notifications?code={functionKey}
```

## Purpose

Entry point for Microsoft Graph subscription webhooks. Validates notifications and enqueues them for async processing.

## Authentication

- **Function Key** — Required as `code` query parameter
- **Graph Validation** — Validates notification signature and subscription ID

## Request

**Headers:**
- `clientState` — Subscription verification token

**Body:**
```json
{
  "value": [
    {
      "subscriptionId": "79b74671-...",
      "resource": "me/messages/AAMkAD...",
      "changeType": "created",
      "resourceData": {
        "id": "AAMkAD...",
        "@odata.type": "#Microsoft.Graph.Message"
      }
    }
  ]
}
```

## Response

**Success (202 Accepted):**
```json
{
  "processed": 1,
  "queued": 1
}
```

**Validation Error (400 Bad Request):**
```json
{
  "error": "Invalid subscription",
  "subscriptionId": "unknown-sub"
}
```

## Processing

1. **Validate subscription** — Check subscriptionId against known subscriptions
2. **Validate client state** — Match header against expected value  
3. **Enqueue notification** — Send to appropriate queue (email-ingest, calendar-ingest)
4. **Return confirmation** — Count of notifications processed

## Queues

| Resource Type | Queue | Processor |
|---------------|-------|-----------|
| `#Microsoft.Graph.Message` | email-ingest | EmailProcessorFunction |
| `#Microsoft.Graph.Event` | calendar-ingest | CalendarProcessorFunction |

## Error Handling

- Invalid subscription → 400 Bad Request
- Missing client state → 400 Bad Request
- Queue failure → 500 Internal Server Error (notification will retry)

## Storage

Does not directly write to storage — enqueues for async processing.
# WhitelistFunction

Manages email whitelist for filtering inbound messages.

## Endpoints

### Add to Whitelist

```
POST /api/whitelist?code={functionKey}
```

**Request:**
```json
{
  "type": "domain",
  "value": "example.com"
}
```

**Or:**
```json
{
  "type": "email", 
  "value": "user@example.com"
}
```

### Remove from Whitelist

```
DELETE /api/whitelist?code={functionKey}
```

**Request:** Same as POST

### List Whitelist

```
GET /api/whitelist?code={functionKey}
```

**Query Parameters:**
- `type` — Filter by type (domain, email)

**Response:**
```json
{
  "entries": [
    {
      "partitionKey": "domain",
      "rowKey": "example.com", 
      "timestamp": "2026-02-06T10:00:00Z"
    },
    {
      "partitionKey": "email",
      "rowKey": "user@example.com",
      "timestamp": "2026-02-06T10:00:00Z"
    }
  ],
  "count": 2
}
```

## Purpose

Controls which emails are ingested vs parked. Two-level whitelist:
- **Domain level** — `example.com` allows all emails from that domain
- **Email level** — `user@example.com` allows that specific address

Email addresses are auto-populated from outbound emails, calendar events, and meeting participants.

## Authentication

- **Function Key** — Required as `code` query parameter  
- **Application Key** — Required as `X-Api-Key` header

## Storage

### WhitelistedDomains Table

| Field | Type | Description |
|-------|------|-------------|
| **PartitionKey** | string | `domain` or `email` |
| **RowKey** | string | Domain name or email address |
| **Timestamp** | datetime | When added |

## Processing

**Add:** Insert new entry (idempotent)
**Remove:** Delete entry (idempotent)  
**List:** Query by partition key

## Auto-Population

Email addresses are automatically added when they appear in:
- Outbound email recipients (TO, CC)
- Calendar event attendees
- Meeting participants

This ensures responses and related emails are not filtered out.
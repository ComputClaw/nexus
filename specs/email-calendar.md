# Email & Calendar Integration

Microsoft Graph integration for email and calendar events.

## Overview

Nexus receives real-time notifications from Microsoft Graph when emails arrive or calendar events change. It processes them asynchronously and stages the data for agent consumption.

## Webhooks

### Graph Notifications Endpoint
```
POST /api/notifications
```

Receives webhook notifications from Microsoft Graph subscriptions.

**Subscription validation:**
- Graph sends `validationToken` query parameter
- Endpoint returns the token to complete subscription setup

**Change notifications:**
- Routes by resource type to appropriate queue
- Email changes → `email-ingest` queue
- Calendar changes → `calendar-ingest` queue

## Processing

### Email Processing
1. **Fetch email** via Graph API
2. **Whitelist check** - sender domain or email must be whitelisted
3. **Deduplication** - only new content stored (uniqueBody field)
4. **Storage** - metadata in Items table, full body in blob storage

### Calendar Processing  
1. **Fetch event** via Graph API
2. **Auto-whitelist attendees** - add attendee emails to whitelist
3. **Storage** - event details in Items table

## Whitelist Management

Two-level whitelist system:

### Domain Whitelist
```
POST /api/whitelist
{
  "domains": ["example.com", "acme.org"]
}
```

Allows all emails from specified domains.

### Email Whitelist
Automatically populated from:
- Outbound email recipients (TO, CC)  
- Calendar event attendees
- Meeting participants

### Whitelist API

**List entries:**
```
GET /api/whitelist
```

**Add entries:**
```
POST /api/whitelist
{
  "domains": ["example.com"], 
  "emails": ["user@other.com"]
}
```

**Remove entry:**
```
DELETE /api/whitelist/{type}/{value}
```

## Data Storage

### Items Table
Email and calendar events stored with:
- **PartitionKey:** `email` or `calendar`
- **RowKey:** Graph resource ID
- **Metadata:** Subject, From, ReceivedAt, etc.
- **BodyText:** Summary or snippet

### Blob Storage
Full content stored in blobs:
- **email-bodies/** - Complete email HTML/text
- **calendar-bodies/** - Extended event details

## Agent Consumption

Agents use the Items API to fetch processed data:

```
GET /api/items?type=email
GET /api/items?type=calendar
GET /api/items/body?type=email&id=<rowKey>
DELETE /api/items?type=email&id=<rowKey>
```

See [agent-integration.md](agent-integration.md) for details.

## Subscriptions

Graph subscriptions are managed automatically:
- Created on startup via bootstrap function
- Renewed every 5 days via timer function  
- Lifecycle events handled via lifecycle endpoint

## Non-Whitelisted Emails

Emails from non-whitelisted senders are parked in `PendingEmails` table, not dropped. When a sender gets whitelisted, their historical emails are promoted to the Items table.
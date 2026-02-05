# API Reference

Base URL: `https://nexusassistant.azurewebsites.net/api/`

## Authentication

All non-webhook endpoints use **two layers** of authentication:

1. **Azure Function key** — `?code=<key>` query parameter (platform-level)
2. **Application API key** — `X-Api-Key: <key>` header (app-level)

Both must be present. Webhook endpoints use different validation (see below).

---

## Graph Notifications

### `POST /api/notifications`

Microsoft Graph webhook receiver. Handles subscription validation (`validationToken`) and change notifications.

- **Auth:** `AuthorizationLevel.Function` + `clientState` validation
- **Behavior:**
  - Routes notifications by `ODataType` — email changes go to the `email-ingest` queue, calendar changes go to the `calendar-ingest` queue
  - Responds to validation requests with the token (required by Graph)
  - Ignores notifications with invalid `clientState`

### `POST /api/lifecycle`

Handles Graph lifecycle events:

- **Reauthorization required** — re-consents to subscriptions
- **Subscription removed** — logs for investigation
- **Missed notifications** — logs (items will be caught on next notification or manual sync)

---

## Fireflies Webhook

### `POST /api/fireflies`

Fireflies.ai webhook receiver for meeting transcript notifications.

- **Auth:** `AuthorizationLevel.Function` + HMAC-SHA256 signature validation via `x-hub-signature` header
- **Behavior:** Validates the HMAC signature against the configured webhook secret, then enqueues the meeting ID to the `meeting-ingest` queue

---

## Items API

The Items API is the primary interface for the sync consumer. Items represent ingested emails, calendar events, and meetings staged in Table Storage.

### `GET /api/items`

List pending items.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `type` | query | *(all)* | Filter by type: `email`, `calendar`, or `meeting` |
| `top` | query | `100` | Max items to return (max 500) |

**Response:** `200 OK`
```json
{
  "items": [
    {
      "partitionKey": "email",
      "rowKey": "AAMkAGQ5...",
      "Subject": "Re: Q4 budget",
      "From": "alice@example.com",
      "Direction": "inbound",
      "ReceivedAt": "2025-01-15T10:30:00Z",
      "FileName": "2025-01-15-re-q4-budget.md",
      ...
    }
  ]
}
```

### `GET /api/items/body`

Fetch full body text or meeting transcript from blob storage. Falls back to the `BodyText` table property if no blob exists.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `type` | query | yes | `email` or `meeting` |
| `id` | query | yes | RowKey of the item |

**Response:** `200 OK` — plain text body/transcript

### `DELETE /api/items`

Remove an item after successful sync. Idempotent — returns `204 No Content` even if the item was already deleted.

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `type` | query | yes | `email`, `calendar`, or `meeting` |
| `id` | query | yes | RowKey of the item |

**Response:** `204 No Content`

---

## Whitelist API

The whitelist controls which inbound emails are accepted. It has two partitions:

- **`domain`** — manually managed (e.g., "example.com"). Any email from that domain passes.
- **`email`** — auto-populated from outbound emails, calendar attendees, and meeting participants. Matches exact email addresses.

### `GET /api/whitelist`

List all whitelist entries (both domain and email partitions).

**Response:** `200 OK`
```json
{
  "entries": [
    { "type": "domain", "value": "example.com", "addedAt": "2025-01-10T08:00:00Z", "addedBy": "api" },
    { "type": "email", "value": "alice@other.com", "addedAt": "2025-01-12T14:30:00Z", "addedBy": "outbound", "emailCount": 3 }
  ]
}
```

### `POST /api/whitelist`

Add whitelist entries. Supports both domains and individual email addresses.

**Request body:**
```json
{
  "domains": ["example.com", "acme.org"],
  "emails": ["bob@specificcompany.com"]
}
```

**Side effect:** After adding entries, promotes any matching emails from `PendingEmails` to `Items` (so previously parked emails become available for sync).

**Response:** `200 OK`

### `DELETE /api/whitelist/{type}/{value}`

Remove a whitelist entry.

| Parameter | Type | Description |
|-----------|------|-------------|
| `type` | path | `domain` or `email` |
| `value` | path | Domain name or email address |

**Response:** `204 No Content`

---

## Subscriptions

### `POST /api/subscriptions/bootstrap`

Create initial Microsoft Graph subscriptions for inbox, sent items, and calendar events.

- **Auth:** Function key + `X-Api-Key`
- **Behavior:** Creates three Graph subscriptions and stores their IDs in the `Subscriptions` table for renewal tracking
- **Use:** Run once on initial setup, or to recreate subscriptions after expiration

---

## Timer Trigger

### `SubscriptionRenewal`

Automatic subscription renewal timer.

- **Schedule:** `0 0 8 */5 * *` (every 5 days at 08:00 UTC)
- **Behavior:** Renews all tracked Graph subscriptions. If renewal fails (e.g., subscription expired), recreates the subscription from scratch.

---

## Queue Processors (Internal)

These are not HTTP endpoints — they're queue-triggered functions that run automatically.

### `EmailProcessor`

- **Queue:** `email-ingest`
- **Behavior:**
  - Fetches the full email from Microsoft Graph
  - Checks whitelist (sender's email address OR sender's domain)
  - **Whitelisted:** Writes to `Items` table. Stores `uniqueBody` in `BodyText`, full body as blob in `email-bodies/`
  - **Not whitelisted:** Writes to `PendingEmails` table (parked for later promotion)
  - **Outbound email:** Auto-whitelists TO + CC recipients by full email address (use BCC to avoid auto-whitelisting; not domain-level)

### `CalendarProcessor`

- **Queue:** `calendar-ingest`
- **Behavior:**
  - Fetches the event from Microsoft Graph
  - Writes to `Items` table with attendee details
  - Handles event deletions as "cancelled" action
  - Auto-whitelists attendee email addresses

### `MeetingProcessor`

- **Queue:** `meeting-ingest`
- **Behavior:**
  - Fetches meeting details from Fireflies GraphQL API (summary, action items, participants, transcript)
  - Stores full transcript as blob in `transcripts/`
  - Writes summary and metadata to `Items` table
  - Auto-whitelists participant email addresses

### Poison Queue Handlers

- `EmailPoisonHandler` — logs failed messages from `email-ingest-poison`
- `CalendarPoisonHandler` — logs failed messages from `calendar-ingest-poison`
- `MeetingPoisonHandler` — logs failed messages from `meeting-ingest-poison`

---

## Table Schemas

### Items

Staging table for all ingested data, consumed by the sync script.

| Column | Description |
|--------|-------------|
| `PartitionKey` | `email`, `calendar`, or `meeting` |
| `RowKey` | Sanitized source ID (Graph message/event ID or Fireflies meeting ID) |
| `FileName` | Suggested markdown filename |
| `Action` | `created`, `updated`, `cancelled` (calendar) |
| `Source` | `graph` or `fireflies` |
| `SourceId` | Original unsanitized ID |
| `Direction` | `inbound` or `outbound` (email only) |
| `ConversationId` | Graph conversation thread ID (email only) |
| `Subject` | Email subject or event/meeting title |
| `From` | Sender email or organizer |
| `To` | JSON array of recipients |
| `Cc` | JSON array of CC recipients |
| `BodyText` | Email `uniqueBody` or meeting summary |
| `FullBodyBlob` | Blob path for full email body |
| `Participants` | JSON array of attendees/participants (calendar/meeting) |
| `Summary` | Meeting summary (meeting only) |
| `ActionItems` | Meeting action items (meeting only) |
| `TranscriptBlob` | Blob path for full transcript (meeting only) |
| `StartTime` | Event/meeting start (calendar/meeting) |
| `EndTime` | Event/meeting end (calendar/meeting) |
| `Location` | Event location (calendar only) |
| `ReceivedAt` | When the original item was received/occurred |
| `IngestedAt` | When Nexus processed it |

### PendingEmails

Emails from non-whitelisted senders, held for promotion.

| Column | Description |
|--------|-------------|
| `PartitionKey` | Sender's domain |
| `RowKey` | Sanitized Graph message ID |
| *(remaining)* | Same fields as Items |

### Whitelist

Dual-partition whitelist for email filtering.

| Column | Description |
|--------|-------------|
| `PartitionKey` | `domain` (manual) or `email` (auto-populated) |
| `RowKey` | Domain name or full email address |
| `AddedAt` | When the entry was created |
| `AddedBy` | Source: `api`, `outbound`, `calendar`, `meeting` |
| `EmailCount` | Number of emails associated with this entry |

### Subscriptions

Tracks active Graph subscriptions for renewal.

| Column | Description |
|--------|-------------|
| `PartitionKey` | `subscription` |
| `RowKey` | Graph subscription ID |
| `Resource` | Graph resource path (e.g., `me/mailFolders/inbox/messages`) |
| `ChangeType` | `created`, `updated`, `deleted` |
| `ExpiresAt` | Subscription expiration timestamp |
| `CreatedAt` | When the subscription was created |

---

## Blob Containers

### `email-bodies/`

Full email body text for emails where `uniqueBody` alone isn't sufficient.

- **Path format:** `{yyyy-MM}/{sanitizedId}.txt`
- **Content:** Plain text email body

### `transcripts/`

Full meeting transcripts from Fireflies.

- **Path format:** `{yyyy-MM}/{firefliesId}.txt`
- **Content:** Plain text transcript

---

## Whitelist Logic Summary

1. **Outbound email** → auto-whitelist TO + CC recipients by full email address (use BCC to avoid; not domain)
2. **Calendar events** → auto-whitelist all attendee email addresses
3. **Meetings** → auto-whitelist all participant email addresses
4. **Manual API** → supports both domain-level and email-level entries
5. **Inbound check** → sender's full email **OR** sender's domain — either match passes
6. **Non-whitelisted** → parked in `PendingEmails` (not dropped)
7. **Promotion** → when a new email/domain is whitelisted, all matching `PendingEmails` are moved to `Items`

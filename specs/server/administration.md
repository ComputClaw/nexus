# Administration

Management and monitoring functions for Nexus.

## Subscription Management

Microsoft Graph subscriptions are managed automatically but can be controlled via admin endpoints.

### Bootstrap Subscriptions
```
POST /api/admin/bootstrap?code=<function-key>
```

Creates initial Graph subscriptions for:
- Inbox messages (`me/messages`)
- Sent items (`me/sentitems`) 
- Calendar events (`me/calendar/events`)

**Response:**
```json
{
  "subscriptions": [
    {
      "id": "79b74671-...",
      "resource": "me/messages",
      "expirationDateTime": "2026-02-11T15:00:00Z"
    }
  ]
}
```

### Subscription Timer
Automatic function that runs every 5 days to renew subscriptions before they expire.

**Trigger:** Timer (0 0 */5 * * *) 
**Action:** Extends subscription expiration by 5 more days

### Lifecycle Events
```
POST /api/lifecycle?code=<function-key>
```

Handles Graph lifecycle notifications:
- **Reauthorization required** - Re-consent needed
- **Subscription removed** - Subscription deleted by Graph
- **Missed notifications** - Some notifications were dropped

## Data Repair

### Repair Function
```
POST /api/admin/repair?code=<function-key>
```

Utility functions for data consistency:
- Promote pending emails when whitelist updated
- Fix orphaned blob references
- Cleanup incomplete items

## Monitoring

### Health Checks
Built into each function:
- Storage connectivity
- Graph API availability
- Queue health

### Logs
Function App logs capture:
- **INFO** - Normal operations (items processed, subscriptions renewed)
- **WARN** - Non-critical issues (duplicate items, failed deletions)
- **ERROR** - Critical failures (storage outages, auth failures)

### Metrics
Azure Monitor provides:
- Function execution count/duration
- Queue message count/age  
- Storage request count/latency
- HTTP response codes

### Alerts
Recommended alerting:
- Failed function executions > threshold
- Queue message age > 30 minutes
- Storage failures > threshold
- Subscription expiration warnings

## Poison Queue Handling

Failed queue messages are automatically retried with exponential backoff. After max retries, messages move to poison queues:

- `email-ingest-poison`
- `calendar-ingest-poison`
- `meeting-ingest-poison`

**Poison queue handlers** log failures and can:
- Manual retry after fixing issues
- Dead letter for investigation
- Discard if determined invalid

## Configuration

### Application Settings

| Setting | Description |
|---------|-------------|
| `GraphTenantId` | Azure AD tenant |
| `GraphClientId` | Azure AD app registration |
| `GraphClientSecret` | Azure AD app secret |
| `GraphUserId` | Target user for subscriptions |
| `GraphClientState` | Subscription validation token |
| `FirefliesApiKey` | Fireflies API key |
| `FirefliesWebhookSecret` | Fireflies webhook secret |

### Connection Strings
- `AzureWebJobsStorage` - Function App storage
- `NexusStorage` - Nexus data storage (tables + blobs)

## Security

### Access Control
- **Function keys** - Control endpoint access
- **Managed identity** - Azure resource access
- **Graph permissions** - Limited to required scopes

### Data Protection
- **Encryption at rest** - Azure Storage encryption
- **Encryption in transit** - HTTPS only
- **Access logging** - All API calls logged
- **Data retention** - Configurable cleanup policies
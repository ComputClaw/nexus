# webhook_pull Job

Pull pending webhook items from Nexus and deliver to agent inboxes.

## Purpose

Fetches webhook notifications intended for a specific agent, writes them as JSON files to the agent's inbox, and spawns a task to notify the agent of new data.

## Arguments (Positional)

| Position | Name | Description |
|----------|------|-------------|
| 0 | agentId | Agent to fetch items for |
| 1 | workspace | Agent workspace path |
| 2 | inboxDir | Relative inbox path (optional, default: `inbox/webhooks`) |

## Example Configuration

```json
{
  "id": "flickclaw-webhooks",
  "type": "webhook_pull",
  "description": "Deliver webhook items to FlickClaw",
  "intervalMinutes": 5,
  "notifyAgentId": "flickclaw",
  "args": [
    "flickclaw",
    "/home/martin/.openclaw/workspace-flickclaw",
    "inbox/webhooks"
  ]
}
```

## Processing Logic

1. **Fetch pending items** - GET `{nexus_url}/webhook/pending?agentId={agentId}&code={api_key}`
2. **Write to inbox:**
   a. Ensure `{workspace}/{inboxDir}` directory exists
   b. For each item: write to `{workspace}/{inboxDir}/{source}_{id}.json`
   c. Collect item IDs for cleanup
3. **Mark as processed** - DELETE `{nexus_url}/webhook/items?code={api_key}` with item IDs
4. **Return results** - Count of delivered items and any errors

## Nexus API Requests

### Get Pending Items
```
GET /api/webhook/pending?agentId={agentId}&code={api_key}
```

**Response:**
```json
{
  "flickclaw": [
    {
      "id": "2026-02-06T12:00:00_abc123",
      "source": "putio",
      "receivedAt": "2026-02-06T12:00:00Z",
      "payload": {
        "transfer_id": 12345,
        "file_id": 67890,
        "name": "Movie.2024.1080p.mkv",
        "status": "COMPLETED"
      }
    }
  ]
}
```

### Mark Items Processed
```
DELETE /api/webhook/items?code={api_key}
Content-Type: application/json

{
  "ids": ["2026-02-06T12:00:00_abc123", "..."]
}
```

## File Output

### Location
```
{workspace}/{inboxDir}/{source}_{id}.json
```

**Example:**
```
/home/martin/.openclaw/workspace-flickclaw/inbox/webhooks/putio_2026-02-06T12:00:00_abc123.json
```

### Content
Raw webhook payload as received by Nexus (JSON, pretty-printed):

```json
{
  "id": "2026-02-06T12:00:00_abc123",
  "source": "putio", 
  "receivedAt": "2026-02-06T12:00:00Z",
  "payload": {
    "transfer_id": 12345,
    "file_id": 67890,
    "name": "Movie.2024.1080p.mkv",
    "status": "COMPLETED"
  }
}
```

## Error Handling

### Network Errors
- **GET fails** - Log error, retry next cycle
- **DELETE fails** - Log warning, items will be re-delivered next run

### File System Errors
- **Inbox directory missing** - Create it
- **Write permission denied** - Log error, skip that item
- **Disk full** - Log error, retry next cycle

### Response Handling
- **No items** - Return success with 0 processed
- **Malformed response** - Log error, skip processing
- **Partial failures** - Process successful items, log failures

## Job Result

```python
@dataclass
class JobResult:
    job_id: str = "webhook_pull"
    success: bool = True
    message: str = "Delivered 2 webhook items to flickclaw"
    items_processed: int = 2
    errors: list[str] = []
```

**Example results:**
```json
{
  "jobId": "flickclaw-webhooks",
  "success": true,
  "message": "Delivered 2 webhook items to flickclaw", 
  "itemsProcessed": 2,
  "errors": []
}
```

```json
{
  "jobId": "flickclaw-webhooks",
  "success": true,
  "message": "Delivered 1 item, 1 failed",
  "itemsProcessed": 1,
  "errors": ["Failed to write putio_abc123.json: Permission denied"]
}
```

## Agent Notification

After successful delivery, the job returns and the worker may spawn a task (if `notifyAgentId` is configured):

```bash
openclaw sessions spawn --agent flickclaw --task "New webhook items (2) in inbox/webhooks/. Process and archive."
```

The spawned task should:
1. Read JSON files from inbox
2. Process webhook data (e.g., announce transfers, refresh inventory)  
3. Archive or delete processed files
4. Return results

## Implementation Notes

- **Atomic delivery** - Write to temp file + rename for consistency
- **Idempotent** - Safe to re-run, handles duplicate deliveries
- **Graceful degradation** - Partial failures don't stop processing
- **Directory creation** - Ensure inbox structure exists
- **File naming** - Use source + ID to avoid conflicts

## Testing

**Test cases:**
- No pending items
- Multiple webhook sources (putio, github, etc.)
- Network failures during fetch
- Network failures during cleanup
- Inbox directory missing
- File write permissions
- Malformed webhook payloads
- Agent workspace doesn't exist
# Sessions Upload Job

Upload completed session transcripts from all agents to Nexus for processing and analytics.

## Overview

The `session_upload` job scans all agent session directories, finds completed sessions, and uploads the raw `.jsonl` files to Nexus. The worker does not parse the content — it ships raw files and lets Nexus handle extraction and analytics.

---

## Job Logic

1. **Iterate through all agents** configured in the worker
2. For each agent:
   a. Read `sessions.json` from sessions directory → list of active session IDs
   b. List all `*.jsonl` files in sessions directory
   c. Filter to files whose name (minus extension) is NOT in active sessions list
   d. For each completed session:
      - Read raw file content
      - POST to Nexus with agent ID, session ID, and raw transcript
      - On success: move file to archive directory
      - On failure: log error, continue to next file

## Session File Location

```
~/.openclaw/agents/<agentId>/sessions/<SessionId>.jsonl
```

## Identifying Completed Sessions

`sessions.json` in each agent's sessions directory tracks active sessions. A session is complete when:
- Its `.jsonl` file exists in the sessions directory  
- Its session ID (first 36 characters of filename) is NOT listed in `sessions.json`

**Example:**
- File: `0fca86c2-49d4-4985-b7ea-2ea80fa8556b.jsonl.deleted.2026-02-02T20-29-47.344Z`
- Session ID: `0fca86c2-49d4-4985-b7ea-2ea80fa8556b`

## After Upload

Successfully uploaded sessions are moved to a local archive folder:
```
~/.openclaw/agents/<agentId>/sessions/archive/<sessionId>.jsonl
```

This preserves local copies while keeping the active sessions directory clean.

## Nexus Request

The worker sends raw transcript content to Nexus:

```
POST /api/sessions?code={apiKey}
Content-Type: application/json

{
  "agentId": "main",
  "sessionId": "abc123", 
  "transcript": "...raw jsonl content as string..."
}
```

The worker does not parse or extract metrics from the JSONL — it ships the raw file content. Nexus handles all parsing, metric extraction, and storage.

## Job Result

Returns standard `JobResult`:

```json
{
  "jobId": "session_upload",
  "success": true,
  "message": "Uploaded 5 sessions across 3 agents",
  "itemsProcessed": 5,
  "errors": []
}
```

Errors include individual file upload failures but don't stop processing other files.

---

*Part of the Nexus worker system*
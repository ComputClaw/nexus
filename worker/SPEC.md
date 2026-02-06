# Nexus Worker Specification

Local Python process that syncs data from Nexus (Azure) to agent workspaces and triggers processing via OpenClaw.

## Overview

The worker bridges Nexus (cloud storage) and OpenClaw agents (local). It:

1. Polls Nexus for pending items
2. Writes items to agent inbox folders
3. Spawns isolated agent tasks
4. Marks items as processed

**Key benefits:**
- **Runs in the main agent** — can be triggered via cron or heartbeat, no separate daemon needed
- **Receiving agents need no credentials** — data is delivered as local files to their inbox

## Requirements

### Functional

| ID | Requirement |
|----|-------------|
| W-01 | Poll Nexus API at configurable interval (default: 5 min) |
| W-02 | Fetch pending items grouped by target agent |
| W-03 | Write each item as JSON file to agent's `inbox/webhooks/` folder |
| W-04 | Create inbox directory if it doesn't exist |
| W-05 | Spawn isolated agent task via `sessions_spawn` after writing |
| W-06 | Mark items as processed in Nexus after successful spawn |
| W-07 | Continue running on errors (log and retry next cycle) |
| W-08 | Support `--once` flag for single poll (cron mode) |

### Configuration

| ID | Requirement |
|----|-------------|
| C-01 | Nexus API URL (required) |
| C-02 | Nexus API key (required) |
| C-03 | Poll interval in seconds (default: 300) |
| C-04 | Agent workspace paths (map of agentId → path) |
| C-05 | Config file path via `--config` flag (default: `config.json`) |

### Error Handling

| ID | Requirement |
|----|-------------|
| E-01 | Log all errors to stderr |
| E-02 | Don't exit on transient errors (network, API) |
| E-03 | Exit on config errors (missing file, invalid JSON) |
| E-04 | Retry failed items on next poll cycle |

## API Dependencies

### Nexus API

```
GET /api/webhook/pending?code={apiKey}

Response:
{
  "flickclaw": [
    {
      "id": "2026-02-06T12:00:00_abc123",
      "source": "putio",
      "receivedAt": "2026-02-06T12:00:00Z",
      "payload": { ... }
    }
  ],
  "stewardclaw": [ ... ]
}
```

```
DELETE /api/webhook/items?code={apiKey}

Body:
{
  "ids": ["2026-02-06T12:00:00_abc123", ...]
}
```

### OpenClaw Gateway

```bash
openclaw gateway call sessions_spawn --params '{
  "agentId": "flickclaw",
  "task": "New webhook items (2) in inbox/webhooks/. Process and archive.",
  "cleanup": "delete"
}'
```

## File Output

### Location

```
{agent_workspace}/inbox/webhooks/{source}_{id}.json
```

Example:
```
/home/martin/.openclaw/workspace-flickclaw/inbox/webhooks/putio_2026-02-06T12:00:00_abc123.json
```

### Content

Raw webhook payload as received by Nexus (JSON, pretty-printed).

## Configuration File

```json
{
  "nexus_url": "https://nexusassistant.azurewebsites.net/api",
  "api_key": "your-function-key",
  "poll_interval_seconds": 300,
  "agents": {
    "flickclaw": "/home/martin/.openclaw/workspace-flickclaw",
    "stewardclaw": "/home/martin/.openclaw/workspace-stewardclaw",
    "main": "/home/martin/.openclaw/workspace"
  }
}
```

## CLI Interface

```
usage: nexus-worker.py [-h] [--config CONFIG] [--once] [--verbose]

Nexus Worker - syncs webhook data to agent workspaces

optional arguments:
  -h, --help       show this help message and exit
  --config CONFIG  path to config file (default: config.json)
  --once           run once and exit (for cron)
  --verbose        enable debug logging
```

## Deployment Options

### Systemd Service (recommended)

```ini
[Unit]
Description=Nexus Worker
After=network.target

[Service]
Type=simple
User=martin
WorkingDirectory=/home/martin/.openclaw/workspace/repos/nexus/worker
ExecStart=/usr/bin/python3 nexus-worker.py
Restart=always
RestartSec=10
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
```

### Cron (alternative)

```
*/5 * * * * cd /path/to/nexus/worker && python3 nexus-worker.py --once >> /var/log/nexus-worker.log 2>&1
```

## Logging

Format: `{timestamp} [{level}] {message}`

```
2026-02-06T13:00:00 [INFO] Polling Nexus for pending items
2026-02-06T13:00:01 [INFO] Found 2 items for flickclaw
2026-02-06T13:00:01 [INFO] Wrote: inbox/webhooks/putio_abc123.json
2026-02-06T13:00:01 [INFO] Wrote: inbox/webhooks/putio_def456.json
2026-02-06T13:00:02 [INFO] Spawned task for flickclaw
2026-02-06T13:00:02 [INFO] Marked 2 items as processed
2026-02-06T13:00:02 [INFO] Next poll in 300s
```

## Security

- API key stored in config file (not in code)
- Config file should be readable only by worker user
- Worker runs on same host as OpenClaw (localhost communication)
- No external network exposure required

## Dependencies

- Python 3.8+
- `requests` library
- OpenClaw CLI installed and configured

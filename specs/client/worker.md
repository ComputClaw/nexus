# Nexus Worker

Local Python service that syncs data between the OpenClaw host and Nexus.

## Purpose

Multi-job worker running on the OpenClaw host. Jobs are configured independently — each has its own schedule, type, and settings.

**Key principle:** Worker posts raw data, Nexus processes. Logic lives in Nexus, worker stays dumb.

## Repo Location

```
ComputClaw/nexus/
└── worker/
    ├── nexus_worker/
    │   ├── __init__.py
    │   ├── main.py           # Entry point, job scheduler
    │   ├── config.py         # Config loading
    │   └── jobs/
    │       ├── __init__.py
    │       ├── session_upload.py
    │       └── webhook_pull.py
    ├── config.json
    └── requirements.txt
```

## Configuration

```json
{
  "nexus": {
    "endpoint": "https://nexusassistant.azurewebsites.net/api",
    "apiKey": "..."
  },
  "agents": {
    "<agent-id>": {
      "workspace": "/path/to/workspace",
      "sessionsDir": "/path/to/sessions"
    }
  },
  "jobs": [
    {
      "id": "session-upload",
      "type": "session_upload",
      "enabled": true,
      "intervalMinutes": 60,
      "config": {
        "agents": ["<agent-id>"]
      }
    },
    {
      "id": "webhook-pull",
      "type": "webhook_pull",
      "enabled": false,
      "intervalMinutes": 5,
      "config": {}
    }
  ]
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Nexus Worker                           │
│                                                             │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐       │
│  │   Config    │   │  Scheduler  │   │   Logger    │       │
│  │   Loader    │──▶│  (main loop)│──▶│  (stdout)   │       │
│  └─────────────┘   └──────┬──────┘   └─────────────┘       │
│                           │                                 │
│         ┌─────────────────┼─────────────────┐              │
│         ▼                 ▼                 ▼              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐       │
│  │  session_   │   │  webhook_   │   │  (future    │       │
│  │  upload     │   │  pull       │   │   jobs)     │       │
│  └─────────────┘   └─────────────┘   └─────────────┘       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
                    ┌─────────────┐
                    │   Nexus     │
                    │  (Azure)    │
                    └─────────────┘
```

## Job Types

| Type | Direction | Description | Details |
|------|-----------|-------------|---------|
| `session_upload` | Push | Upload completed session transcripts | [session-upload.md](./jobs/session-upload.md) |
| `webhook_pull` | Pull | Fetch webhook items, deliver to agents | [webhook-pull.md](./jobs/webhook-pull.md) |

## Main Loop

```python
def main():
    config = load_config()
    jobs = initialize_jobs(config)
    
    while True:
        for job in jobs:
            if job.enabled and job.due():
                try:
                    job.run()
                    job.mark_success()
                except Exception as e:
                    job.mark_failure(e)
                    log_error(job.id, e)
        
        sleep(60)  # Check every minute
```

## Job Interface

Each job implements:

```python
class Job:
    id: str
    type: str
    enabled: bool
    interval_minutes: int
    last_run: datetime | None
    
    def due(self) -> bool:
        """Check if job should run based on interval."""
    
    def run(self) -> None:
        """Execute the job. Raise on failure."""
    
    def mark_success(self) -> None:
        """Update last_run timestamp."""
    
    def mark_failure(self, error: Exception) -> None:
        """Log failure, maybe notify."""
```

## Deployment

Systemd service:

```ini
[Unit]
Description=Nexus Worker
After=network.target

[Service]
Type=simple
User=<user>
WorkingDirectory=/path/to/nexus/worker
ExecStart=/usr/bin/python3 -m nexus_worker.main
Restart=always
RestartSec=30
Environment=PYTHONUNBUFFERED=1

[Install]
WantedBy=multi-user.target
```

**Commands:**
```bash
# Install
sudo cp nexus-worker.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable nexus-worker

# Control
sudo systemctl start nexus-worker
sudo systemctl stop nexus-worker
sudo systemctl restart nexus-worker

# Monitor
sudo systemctl status nexus-worker
journalctl -u nexus-worker -f
```

## Dependencies

```
# requirements.txt
requests>=2.28.0
```

## Status

📝 Designed, pending implementation

---

*Part of the Nexus integration service*
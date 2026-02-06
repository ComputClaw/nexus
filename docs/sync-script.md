# Sync Script

Automated data synchronization between Nexus and your local workspace.

## Overview

The sync script (`scripts/nexus-sync.js`) is a zero-dependency Node.js script that:

1. Fetches new data from Nexus
2. Saves it as markdown files in your workspace
3. Deletes processed items from Nexus

**Key benefit:** Set it up once, run it on a schedule, and always have fresh data.

## Setup

### 1. Copy Configuration

```bash
cd scripts/
cp .nexus-config.example.json .nexus-config.json
```

### 2. Edit Configuration

```json
{
  "nexusUrl": "https://nexusassistant.azurewebsites.net/api",
  "functionKey": "your-function-key-here",
  "apiKey": "your-api-key-here", 
  "outputDir": "../data/inbox"
}
```

Get the keys from your administrator.

### 3. Test Connection

```bash
node nexus-sync.js --dry-run
```

Should show available items without downloading anything.

## Usage

### Basic Sync

```bash
node nexus-sync.js
```

Downloads basic metadata for all data types.

### With Full Content

```bash
node nexus-sync.js --with-body
```

Downloads complete email bodies and meeting transcripts.

### Filter by Type

```bash
node nexus-sync.js --type email
node nexus-sync.js --type calendar
node nexus-sync.js --type meeting
```

### Preview Mode

```bash
node nexus-sync.js --dry-run --with-body
```

Shows what would be downloaded without actually doing it.

## Output Format

Files are saved as markdown with YAML frontmatter:

```markdown
---
id: msg123
type: email
subject: "Project Update"
from: alice@example.com
to: team@company.com
receivedAt: 2026-02-06T14:30:00Z
fileName: 2026-02-06-project-update.md
---

# Project Update

Hi team,

Here's the latest on our project...
```

## File Naming

Files are named for easy browsing:

- **Emails:** `YYYY-MM-DD-subject-slug.md`
- **Calendar:** `YYYY-MM-DD-event-title-slug.md`  
- **Meetings:** `YYYY-MM-DD-meeting-title-slug.md`

## Automation

### Cron Schedule

Add to your crontab for automatic syncing:

```bash
# Every 5 minutes
*/5 * * * * cd /path/to/nexus/scripts && node nexus-sync.js --with-body

# Every 15 minutes (lighter load)
*/15 * * * * cd /path/to/nexus/scripts && node nexus-sync.js --with-body
```

### Systemd Timer

Create `nexus-sync.service`:

```ini
[Unit]
Description=Nexus Sync

[Service]
Type=oneshot
User=your-user
WorkingDirectory=/path/to/nexus/scripts
ExecStart=/usr/bin/node nexus-sync.js --with-body
```

Create `nexus-sync.timer`:

```ini
[Unit]
Description=Run Nexus Sync every 5 minutes

[Timer]
OnCalendar=*:0/5
Persistent=true

[Install]
WantedBy=timers.target
```

Enable:

```bash
sudo systemctl enable --now nexus-sync.timer
```

## Error Handling

The script handles common issues gracefully:

- **Network errors:** Retries with exponential backoff
- **Delete failures:** Item will be re-synced next run (file overwritten)
- **Invalid data:** Logs error and continues with other items

## Logs

Check script output for issues:

```bash
node nexus-sync.js --with-body 2>&1 | tee sync.log
```

Common log messages:

```
[INFO] Found 5 items to sync
[INFO] Downloaded: 2026-02-06-project-update.md
[WARN] Delete failed for item msg123, will retry next run
[ERROR] Network error, retrying in 2s...
```

## Troubleshooting

**No items found:**
- Check your credentials in config file
- Verify whitelist settings (for email)
- Run with `--dry-run` to test connection

**Download failures:**
- Check network connectivity
- Verify API keys haven't expired
- Try with smaller batch size

**Permission errors:**
- Ensure output directory is writable
- Check file permissions on config file
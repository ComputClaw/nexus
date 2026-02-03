# Sync Consumer

The sync consumer is the agent-side component that pulls data from the Nexus backend and stages it locally for processing. It bridges the Azure Function App (cloud) with the agent workspace (local).

## Sync Script

**Location:** `scripts/nexus-sync.js`

A zero-dependency Node.js script that:

1. Calls `GET /api/items` to list all pending items
2. Formats each item as a markdown file with YAML frontmatter
3. Writes all files to `data/inbox/` (flat directory, no date subfolders)
4. Calls `DELETE /api/items` for each item after writing

**Consumed = removed.** Once a file is written to inbox, the item is deleted from the backend. If the delete fails, the item will be re-synced on the next run (the file gets overwritten — no duplicates).

### Flags

| Flag | Description |
|------|-------------|
| `--with-body` | Also fetch full body/transcript from blob storage via `GET /api/items/body` |
| `--type TYPE` | Only sync a specific type: `email`, `calendar`, or `meeting` |
| `--dry-run` | Show what would be synced without writing files or deleting items |

### Configuration

Create `scripts/.nexus-config.json` (see `.nexus-config.example.json`):

```json
{
    "apiBaseUrl": "https://nexusassistant.azurewebsites.net/api",
    "functionKey": "<your-function-key>",
    "apiKey": "<your-api-key>"
}
```

> ⚠️ This file contains secrets — it's gitignored and should never be committed.

### Usage

```bash
# Sync everything with full bodies
node scripts/nexus-sync.js --with-body

# Sync only emails
node scripts/nexus-sync.js --type email

# Preview without writing
node scripts/nexus-sync.js --dry-run
```

---

## Directory State Machine

Items flow through a series of directories as they're processed:

```
data/inbox/                   → New, unprocessed (sync lands items here)
data/parked/                  → Waiting for user input (ambiguous items)
data/emails/YYYY/MM/DD/       → Processed emails
data/calendar/YYYY/MM/DD/     → Processed calendar events
data/meetings/YYYY/MM/DD/     → Processed meeting transcripts
```

### Processing Workflow

1. **Sync** — `nexus-sync.js` writes new items to `data/inbox/`
2. **Process** — Agent reads each file in `data/inbox/` and extracts commitments (deadlines, promises, action items)
3. **Triage** — Three confidence tiers:
   - **Obvious** → update commitments file silently, move to final folder (e.g., `data/emails/2025/01/15/`)
   - **Ambiguous** → ask user for clarification (inline buttons), move to `data/parked/`
   - **Not a commitment** → move to final folder without updating commitments
4. **Resolve** — When the user responds to a parked item → process the commitment → move to final folder

---

## Commitments File

The agent maintains a commitments file tracking obligations extracted from emails, calendar events, and meetings.

```markdown
## User Owes

### [C-001] Send Q4 budget revision to Alice
- **Due:** 2025-01-20
- **Source:** data/emails/2025/01/15/re-q4-budget.md
- **Status:** 🟡 due in 2 days

### [C-002] Review draft proposal
- **Due:** 2025-01-22
- **Source:** data/emails/2025/01/16/draft-proposal.md
- **Status:** 🟢 5 days remaining

## Owed to User

### [C-003] Bob to send updated contract
- **Due:** 2025-01-18
- **Owner:** Bob Smith
- **Source:** data/emails/2025/01/14/contract-update.md
- **Status:** ⏳ waiting

### [C-004] Legal review completion
- **Due:** 2025-01-25
- **Owner:** Legal team
- **Source:** data/meetings/2025/01/13/project-kickoff.md
- **Status:** ⏳ waiting
```

**Status indicators:**
- 🟢 On track (> 3 days remaining)
- 🟡 Due soon (≤ 3 days)
- 🔴 Overdue
- ⏳ Waiting on someone else
- ✅ Completed

---

## Markdown File Formats

### Email

```markdown
---
type: email
direction: inbound
from: alice@example.com
to: bob@example.com, carol@example.com
date: 2025-01-15 10:30:00 UTC
subject: Re: Q4 budget
---

# Re: Q4 budget

**From:** alice@example.com
**To:** bob@example.com, carol@example.com
**Date:** 2025-01-15 10:30:00 UTC

---

(email body content)
```

### Calendar Event

```markdown
---
type: calendar
action: created
organizer: alice@example.com
start: 2025-01-20 14:00:00 UTC
end: 2025-01-20 15:00:00 UTC
location: Conference Room B
subject: Q4 Review
---

# Q4 Review

**Organizer:** alice@example.com
**When:** 2025-01-20 14:00:00 UTC — 2025-01-20 15:00:00 UTC
**Location:** Conference Room B
**Status:** created

## Attendees

- Alice Smith (alice@example.com) — accepted
- Bob Jones (bob@example.com) — tentative
```

### Meeting Transcript

```markdown
---
type: meeting
organizer: alice@example.com
date: 2025-01-15 14:00:00 UTC
subject: Project Kickoff
---

# Project Kickoff

**Organizer:** alice@example.com
**Date:** 2025-01-15 14:00:00 UTC

## Participants

- Alice Smith (alice@example.com)
- Bob Jones (bob@example.com)

## Summary

(AI-generated meeting summary from Fireflies)

## Action Items

(extracted action items)

---

## Full Transcript

(full transcript if --with-body was used)
```

---

## Recommended Cron Schedule

| Schedule | Purpose | Behavior |
|---|---|---|
| `*/15 * * * *` | Sync + process inbox | Run sync script, process new items, alert if urgent |
| `0 8 * * *` | Morning brief | Today's calendar, open commitments, emails needing reply |
| `0 12 * * *` | Midday check-in | New items since morning, upcoming meetings |
| `0 18 * * *` | Evening wrap-up | Day summary, items pending for tomorrow |
| `0 8,14,20 * * *` | Parked items nudge | Remind user about unresolved ambiguous items |

### Example Cron Entry (sync)

```bash
*/15 * * * * cd /path/to/workspace && node scripts/nexus-sync.js --with-body >> logs/nexus-sync.log 2>&1
```

---

## Error Handling

- **Sync script crashes** → No data loss. Items remain in the backend and will be picked up on the next successful run.
- **Delete fails after write** → Item will be re-synced next run. The file gets overwritten (same filename), so no duplicates.
- **Backend unavailable** → Sync script exits with an error. Graph notifications continue to queue; items accumulate and will sync when the backend is reachable again.
- **Malformed item** → Logged and skipped. Other items continue processing.

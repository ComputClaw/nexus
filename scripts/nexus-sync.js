#!/usr/bin/env node
/**
 * nexus-sync.js — Pull pending items from Nexus API and write to inbox
 *
 * Usage: node scripts/nexus-sync.js [--with-body] [--type email|calendar|meeting] [--dry-run]
 *
 * Config: reads from scripts/.nexus-config.json
 * Output: writes markdown to data/inbox/ (all items land here for processing)
 *
 * Flags:
 *   --with-body   Also fetch full body/transcript from blob storage
 *   --type TYPE   Only sync a specific item type
 *   --dry-run     Show what would be synced without writing files
 */

const https = require('https');
const fs = require('fs');
const path = require('path');

// Paths relative to workspace root
const WORKSPACE = path.resolve(__dirname, '..');
const CONFIG_PATH = path.join(__dirname, '.nexus-config.json');
const INBOX_DIR = path.join(WORKSPACE, 'data', 'inbox');

// Parse args
const args = process.argv.slice(2);
const withBody = args.includes('--with-body');
const dryRun = args.includes('--dry-run');
const typeIdx = args.indexOf('--type');
const typeFilter = typeIdx >= 0 ? args[typeIdx + 1] : null;

// Load config
if (!fs.existsSync(CONFIG_PATH)) {
    console.error(`Config not found: ${CONFIG_PATH}`);
    console.error('Create .nexus-config.json with: apiBaseUrl, functionKey');
    process.exit(1);
}
const config = JSON.parse(fs.readFileSync(CONFIG_PATH, 'utf8'));

// ----- HTTP helpers -----

function apiRequest(method, endpoint, body = null) {
    return new Promise((resolve, reject) => {
        const sep = endpoint.includes('?') ? '&' : '?';
        const urlStr = `${config.apiBaseUrl}/${endpoint}${sep}code=${encodeURIComponent(config.functionKey)}`;
        const url = new URL(urlStr);

        const options = {
            hostname: url.hostname,
            port: 443,
            path: url.pathname + url.search,
            method,
            headers: {
                'Content-Type': 'application/json',
            },
        };

        const req = https.request(options, (res) => {
            let data = '';
            res.on('data', (chunk) => (data += chunk));
            res.on('end', () => {
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    if (!data || data.trim() === '') {
                        resolve(null);
                    } else {
                        try {
                            resolve(JSON.parse(data));
                        } catch {
                            resolve(data);
                        }
                    }
                } else {
                    reject(new Error(`HTTP ${res.statusCode}: ${data}`));
                }
            });
        });

        req.on('error', reject);
        if (body) req.write(JSON.stringify(body));
        req.end();
    });
}

// ----- Markdown formatters -----

function formatEmail(item, body) {
    const to = tryParseJson(item.To, []).join(', ');
    const cc = tryParseJson(item.Cc, []);
    const ccLine = cc.length > 0 ? `**CC:** ${cc.join(', ')}\n` : '';

    return `---
type: email
direction: ${item.Direction || 'unknown'}
from: ${item.From || 'unknown'}
to: ${to}
date: ${formatDate(item.ReceivedAt)}
subject: ${item.Subject || 'No subject'}
---

# ${item.Subject || 'No subject'}

**From:** ${item.From || 'unknown'}
**To:** ${to}
${ccLine}**Date:** ${formatDate(item.ReceivedAt)}

---

${body || item.BodyText || '(no content)'}
`;
}

function formatCalendar(item) {
    const participants = tryParseJson(item.Participants, []);
    const attendeeLines = participants
        .map((p) => `- ${p.name || 'Unknown'} (${p.email || '?'}) — ${p.status || '?'}`)
        .join('\n');

    return `---
type: calendar
action: ${item.Action || 'created'}
organizer: ${item.From || 'unknown'}
start: ${formatDate(item.StartTime)}
end: ${formatDate(item.EndTime)}
location: ${item.Location || 'N/A'}
subject: ${item.Subject || 'Untitled'}
---

# ${item.Subject || 'Untitled'}

**Organizer:** ${item.From || 'unknown'}
**When:** ${formatDate(item.StartTime)} — ${formatDate(item.EndTime)}
**Location:** ${item.Location || 'N/A'}
**Status:** ${item.Action || 'created'}

## Attendees

${attendeeLines || '(none)'}

---

${item.BodyText || ''}
`;
}

function formatMeeting(item, body) {
    const participants = tryParseJson(item.Participants, []);
    const attendeeLines = participants
        .map((p) => `- ${p.name || 'Unknown'} (${p.email || '?'})`)
        .join('\n');

    return `---
type: meeting
organizer: ${item.From || 'unknown'}
date: ${formatDate(item.StartTime)}
subject: ${item.Subject || 'Untitled'}
---

# ${item.Subject || 'Untitled'}

**Organizer:** ${item.From || 'unknown'}
**Date:** ${formatDate(item.StartTime)}

## Participants

${attendeeLines || '(none)'}

## Summary

${item.Summary || '(no summary)'}

## Action Items

${item.ActionItems || '(none)'}

---

${body ? '## Full Transcript\n\n' + body : ''}
`;
}

// ----- File helpers -----

function getFileName(item) {
    return item.FileName || `${item.rowKey.substring(0, 12)}-item.md`;
}

function writeFile(filePath, content) {
    const dir = path.dirname(filePath);
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(filePath, content, 'utf8');
}

// ----- Utility -----

function formatDate(d) {
    if (!d) return 'unknown';
    try {
        return new Date(d).toISOString().replace('T', ' ').replace(/\.\d+Z$/, ' UTC');
    } catch {
        return String(d);
    }
}

function tryParseJson(val, fallback) {
    if (!val) return fallback;
    if (Array.isArray(val)) return val;
    try {
        return JSON.parse(val);
    } catch {
        return fallback;
    }
}

// ----- Main -----

async function main() {
    console.log(`Nexus Sync — ${new Date().toISOString()}`);
    if (withBody) console.log('Fetching full bodies enabled');
    if (typeFilter) console.log(`Type filter: ${typeFilter}`);
    if (dryRun) console.log('DRY RUN — no files will be written');
    console.log('---');

    // 1. Fetch all items (everything in the table is pending)
    let endpoint = 'items?top=100';
    if (typeFilter) endpoint += `&type=${typeFilter}`;

    const result = await apiRequest('GET', endpoint);
    const items = result.items || [];

    console.log(`Found ${items.length} items`);
    if (items.length === 0) {
        console.log('Nothing to sync.');
        return;
    }

    let processed = 0;
    let errors = 0;

    for (const item of items) {
        try {
            // 2. Optionally fetch full body
            let body = null;
            if (withBody && (item.partitionKey === 'email' || item.partitionKey === 'meeting')) {
                try {
                    body = await apiRequest(
                        'GET',
                        `items/body?type=${item.partitionKey}&id=${encodeURIComponent(item.rowKey)}`
                    );
                } catch (err) {
                    console.warn(`  Warning: Could not fetch body: ${err.message}`);
                }
            }

            // 3. Format markdown
            let markdown;
            switch (item.partitionKey) {
                case 'email':
                    markdown = formatEmail(item, body);
                    break;
                case 'calendar':
                    markdown = formatCalendar(item);
                    break;
                case 'meeting':
                    markdown = formatMeeting(item, body);
                    break;
                default:
                    console.warn(`  Unknown type: ${item.partitionKey}, skipping`);
                    continue;
            }

            // 4. Write to inbox (all items land here for NexusClaw to process)
            const fileName = getFileName(item);
            const filePath = path.join(INBOX_DIR, fileName);

            if (dryRun) {
                console.log(`  [DRY] inbox/${fileName}`);
                processed++;
                continue;
            }

            writeFile(filePath, markdown);
            console.log(`  Wrote: inbox/${fileName}`);

            // 5. Delete from backend (file written = safe to remove)
            try {
                await apiRequest(
                    'DELETE',
                    `items?type=${item.partitionKey}&id=${encodeURIComponent(item.rowKey)}`
                );
            } catch (err) {
                // Non-fatal — item will be re-synced next run (file gets overwritten)
                console.warn(`  Warning: Delete failed (will retry next sync): ${err.message}`);
            }

            processed++;
        } catch (err) {
            console.error(`  Error: ${item.partitionKey}/${item.rowKey}: ${err.message}`);
            errors++;
        }
    }

    console.log(`\nDone. ${processed} processed, ${errors} errors.`);
}

main().catch((err) => {
    console.error('Sync failed:', err.message);
    process.exit(1);
});

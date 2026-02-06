# Nexus Documentation

User guide for the Nexus data ingestion service.

## What is Nexus?

Nexus collects data from external services and makes it available to OpenClaw agents through simple REST APIs.

## Getting Started

### For Agents

1. **Get credentials** from your administrator:
   - Function key (Azure)

2. **Set up sync script:**
   ```bash
   cp scripts/.nexus-config.example.json scripts/.nexus-config.json
   # Edit with your credentials and URLs
   ```

3. **Sync data:**
   ```bash
   node scripts/nexus-sync.js --with-body
   ```

### For Administrators

See [Setup Guide](setup.md) for deployment and configuration.

## What Data is Available?

| Type | Source | Description |
|------|--------|-------------|
| **Email** | Microsoft Graph | Inbox and sent messages |
| **Calendar** | Microsoft Graph | Meeting invites and events |
| **Meetings** | Fireflies.ai | Transcripts and summaries |
| **Sessions** | OpenClaw | Agent session transcripts |

## How to Access Data

### Sync Script (Recommended)

Automated sync that downloads data as markdown files:

```bash
# Sync all data types
node nexus-sync.js --with-body

# Sync only emails
node nexus-sync.js --type email

# Preview without downloading
node nexus-sync.js --dry-run
```

### REST API (Advanced)

Direct API access for custom integrations:

```bash
# List available items
curl "https://nexus.../api/items?code=<key>"

# Get full content
curl "https://nexus.../api/items/body?type=email&id=<id>&code=<key>"

# Delete after processing
curl -X DELETE "https://nexus.../api/items?type=email&id=<id>&code=<key>"
```

## Documentation

| Topic | Description |
|-------|-------------|
| [Setup Guide](setup.md) | Deployment and configuration |
| [API Reference](api-reference.md) | Complete API documentation |
| [Sync Script](sync-script.md) | Using the automated sync |
| [Troubleshooting](troubleshooting.md) | Common issues and solutions |

## Support

- Check [Troubleshooting](troubleshooting.md) for common issues
- Review logs in Azure Portal → Function App → Monitor
- Contact your administrator for configuration issues
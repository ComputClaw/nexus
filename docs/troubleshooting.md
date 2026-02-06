# Troubleshooting

Common issues and solutions for Nexus.

## No Data Appearing

### Email Not Syncing

**Symptoms:** No emails in sync results, empty inbox.

**Causes & Solutions:**

1. **Whitelist not configured**
   ```bash
   # Check current whitelist
   curl "https://nexus.../api/whitelist?code=<key>"

   # Add your domain
   curl -X POST "https://nexus.../api/whitelist?code=<key>" \
     -H "Content-Type: application/json" \
     -d '{"domains": ["yourdomain.com"]}'
   ```

2. **Graph subscriptions expired**
   ```bash
   # Re-bootstrap subscriptions
   curl -X POST "https://nexus.../api/admin/bootstrap?code=<key>"
   ```

3. **Graph permissions missing**
   - Check Azure AD app registration has `Mail.Read` permission
   - Ensure admin consent granted

### Calendar Not Syncing

**Check subscriptions:**
```bash
# Look for calendar subscription in logs
# Should see "me/calendar/events" subscription
```

**Verify permissions:**
- Azure AD app needs `Calendars.Read` permission
- Admin consent granted

### Meetings Not Available

**Status:** Meetings integration requires Fireflies.ai setup.

**Check configuration:**
```bash
# Verify Fireflies settings exist
az functionapp config appsettings list --name nexusassistant | grep Fireflies
```

## Authentication Errors

### 401 Unauthorized

**Symptoms:** API calls return "Unauthorized"

**Solutions:**

1. **Check function key**
   ```bash
   # Get valid function key from Azure Portal
   az functionapp keys list --resource-group nexus-rg --name nexusassistant
   ```

2. **Test with valid key**
   ```bash
   curl "https://nexus.../api/items?code=<function-key>"
   ```

## Sync Script Issues

### Connection Failures

**Symptoms:** "Network error" or "Connection refused"

**Solutions:**

1. **Check URL**
   ```json
   {
     "nexusUrl": "https://nexusassistant.azurewebsites.net/api"
   }
   ```

2. **Test connectivity**
   ```bash
   curl https://nexusassistant.azurewebsites.net/api/items?code=test
   ```

### Permission Errors

**Symptoms:** "Cannot write to output directory"

**Solutions:**

1. **Check directory exists and is writable**
   ```bash
   mkdir -p data/inbox
   chmod 755 data/inbox
   ```

2. **Check config file permissions**
   ```bash
   chmod 600 scripts/.nexus-config.json
   ```

### No Files Downloaded

**Symptoms:** Script runs but no files appear

**Solutions:**

1. **Run with dry-run to debug**
   ```bash
   node nexus-sync.js --dry-run --verbose
   ```

2. **Check for items**
   ```bash
   curl "https://nexus.../api/items?code=<key>"
   ```

## Performance Issues

### Slow Sync

**Symptoms:** Sync script takes very long

**Solutions:**

1. **Filter by type**
   ```bash
   node nexus-sync.js --type email  # Only emails
   ```

2. **Skip body for large emails**
   ```bash
   node nexus-sync.js  # Without --with-body
   ```

3. **Reduce batch size**
   ```bash
   curl "https://nexus.../api/items?top=50&code=<key>"
   ```

### Function Timeouts

**Symptoms:** Function execution timeouts in Azure

**Solutions:**

1. **Check queue message age**
   - High queue age indicates processing backlog

2. **Scale up Function App**
   ```bash
   az functionapp plan update \
     --resource-group nexus-rg \
     --name nexus-plan \
     --sku P1V2
   ```

## Data Issues

### Missing Email Body

**Symptoms:** Email shows in list but body request fails

**Solutions:**

1. **Check blob storage**
   - Verify storage account connectivity
   - Check blob container exists

2. **Fallback to table data**
   ```bash
   # API automatically falls back to BodyText field if no blob
   ```

### Duplicate Items

**Symptoms:** Same email appears multiple times

**Causes:**
- Sync script failed to delete after processing
- Function processed same notification multiple times

**Solutions:**

1. **Manual cleanup**
   ```bash
   curl -X DELETE "https://nexus.../api/items?type=email&id=<id>&code=<key>"
   ```

2. **Check function logs for delete failures**

## Monitoring and Logs

### Function Logs

**Azure Portal:** Function App → Monitor → Logs

**Common log patterns:**
```
[INFO] Processed email: subject=...
[WARN] Duplicate notification ignored: id=...
[ERROR] Storage account unreachable: ...
```

### Application Insights

**Queries:**
```sql
// Failed function executions
requests
| where success == false
| summarize count() by name

// High-latency functions  
requests
| where duration > 30000
| project timestamp, name, duration
```

### Queue Monitoring

**Check queue depth:**
```bash
# Azure Portal → Storage Account → Queues
# Look for high message counts in:
# - email-ingest
# - calendar-ingest
# - meeting-ingest
```

## Getting Help

1. **Check function logs** in Azure Portal
2. **Run sync script with --dry-run** to isolate issues
3. **Test API directly** with curl to verify connectivity
4. **Review configuration** against setup guide
5. **Contact administrator** with specific error messages
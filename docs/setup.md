# Setup Guide

How to deploy and configure Nexus for your organization.

## Prerequisites

- Azure subscription with Function App and Storage Account
- Microsoft 365 tenant with admin access
- (Optional) Fireflies.ai account for meeting transcripts

## Azure Setup

### 1. Create Function App

```bash
az functionapp create \
  --resource-group nexus-rg \
  --name nexusassistant \
  --storage-account nexusstorage \
  --consumption-plan-location westeurope \
  --runtime dotnet-isolated \
  --runtime-version 8.0
```

### 2. Create Storage Account

```bash
az storage account create \
  --resource-group nexus-rg \
  --name nexusassistantstorage \
  --location westeurope \
  --sku Standard_LRS
```

### 3. Configure Application Settings

```bash
az functionapp config appsettings set \
  --resource-group nexus-rg \
  --name nexusassistant \
  --settings \
    NexusStorage="<connection-string>" \
    GraphTenantId="<tenant-id>" \
    GraphClientId="<app-id>" \
    GraphClientSecret="<app-secret>" \
    GraphUserId="<user-id>" \
    GraphClientState="<random-string>"
```

## Microsoft Graph Setup

### 1. Register App

In Azure AD → App registrations:

1. **Name:** NexusIngestion
2. **Account types:** Single tenant
3. **Redirect URI:** None

### 2. Configure Permissions

Add **Application permissions** (not delegated):

- `Mail.Read` - Access user's inbox and sent items
- `Calendars.Read` - Access user's calendar events

**Grant admin consent** for all permissions.

### 3. Create Client Secret

App registration → Certificates & secrets → New client secret

Save the secret value - you'll need it in Function App settings.

### 4. Find User ID

```bash
az ad user show --id user@yourdomain.com --query objectId -o tsv
```

## Deploy Function App

### 1. Build and Deploy

```bash
cd src/Nexus.Ingest
func azure functionapp publish nexusassistant
```

### 2. Initialize Subscriptions

```bash
curl -X POST "https://nexusassistant.azurewebsites.net/api/admin/bootstrap?code=<function-key>"
```

Verify subscriptions are created successfully.

## Configure Email Whitelist

### 1. Add Your Domain

```bash
curl -X POST "https://nexusassistant.azurewebsites.net/api/whitelist?code=<function-key>" \
  -H "Content-Type: application/json" \
  -d '{"domains": ["yourdomain.com"]}'
```

### 2. Verify Whitelist

```bash
curl "https://nexusassistant.azurewebsites.net/api/whitelist?code=<function-key>"
```

## Optional: Fireflies.ai Setup

### 1. Get API Key

From Fireflies.ai dashboard → Integrations → API

### 2. Configure Webhook

**URL:** `https://nexusassistant.azurewebsites.net/api/fireflies?code=<function-key>`  
**Secret:** Generate random string for HMAC validation

### 3. Add Settings

```bash
az functionapp config appsettings set \
  --resource-group nexus-rg \
  --name nexusassistant \
  --settings \
    FirefliesApiKey="<api-key>" \
    FirefliesWebhookSecret="<webhook-secret>"
```

## Agent Setup

### 1. Create Credentials

Give agents:
- **Function key:** From Azure Portal → Function App → App keys

### 2. Test Connection

```bash
curl "https://nexusassistant.azurewebsites.net/api/items?code=<function-key>"
```

Should return `{"items": [], "count": 0}` for new setup.

## Monitoring

### 1. Enable Application Insights

```bash
az functionapp config appsettings set \
  --resource-group nexus-rg \
  --name nexusassistant \
  --settings \
    APPINSIGHTS_INSTRUMENTATIONKEY="<instrumentation-key>"
```

### 2. Set Up Alerts

Monitor for:
- Function failures
- High queue message age
- Storage account errors
- Graph subscription expiry

## Security

### 1. Function App Settings

- Enable **HTTPS only**
- Set **Minimum TLS version** to 1.2
- Enable **Managed Identity**

### 2. Storage Account

- Enable **Secure transfer required**
- Set **Minimum TLS version** to 1.2
- Configure **Network access** (if needed)

### 3. Key Management

- Rotate function keys periodically
- Monitor access logs

## Backup and Recovery

### 1. Function App Code

Store in source control (Git repository).

### 2. Configuration

Export application settings:

```bash
az functionapp config appsettings list \
  --resource-group nexus-rg \
  --name nexusassistant \
  > nexus-settings-backup.json
```

### 3. Data

Configure storage account backup policy for tables and blobs.
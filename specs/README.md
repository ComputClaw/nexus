# Nexus Specifications

Technical specifications for developing the Nexus data ingestion service.

## Purpose

These specifications define what to build, not how to use existing functionality. Each spec covers:
- Technical requirements
- API designs  
- Storage schemas
- Processing logic
- Implementation details

## Functional Areas

| Area | Implementation Status | Specification |
|------|---------------------|---------------|
| **Authentication** | ✅ Implemented | [📄](authentication.md) |
| **Email & Calendar** | ✅ Implemented | [📄](email-calendar.md) |
| **Meetings** | 📝 Spec Complete, Not Implemented | [📄](meetings.md) |
| **Sessions** | 📝 Spec Complete, Not Implemented | [📄](sessions.md) |
| **Agent Integration** | ✅ Implemented | [📄](agent-integration.md) |
| **Administration** | ✅ Implemented | [📄](administration.md) |

## Implementation Priority

**Ready for implementation:**
1. **Sessions endpoint** - POST /api/sessions for transcript storage (spec complete)
2. **Sessions worker** - Python service for session upload (spec complete)
3. **Meetings integration** - Fireflies.ai webhook processing (spec complete, needs API key)

**Completed implementations:**
- Email/calendar ingestion (Microsoft Graph)
- Items API (agent consumption)
- Authentication system
- Admin functions

## Development Workflow

1. **Read relevant specs** for the feature being built
2. **Follow technical requirements** defined in specs
3. **Implement according to schemas** and API designs
4. **Test against spec requirements**
5. **Update implementation status** when complete

## Spec Format

Each specification includes:
- **Overview** - Purpose and scope
- **Technical Requirements** - What must be built
- **API Design** - Endpoints, requests, responses
- **Storage Schema** - Table/blob structure
- **Processing Logic** - Step-by-step algorithms
- **Error Handling** - Failure modes and responses
- **Integration Points** - How it connects to other components

## Implementation Notes

- Specs define contracts, not implementation details
- Follow existing patterns from completed areas
- Storage uses Azure Table Storage + Blob Storage
- All endpoints require Function Key + API Key authentication
- Error responses follow standard JSON format
# Story 007: Organization and Team Support

**Priority:** Medium
**Component:** Backend API, Frontend, Database, andy-rbac integration
**Labels:** feature, multi-tenancy

## Description

Add support for organizations and teams so that repositories, settings, and enrichments can be scoped to organizational boundaries. Users can belong to one or more organizations, and within each organization, they can be assigned to teams. Repositories are owned by organizations, and access is controlled through team membership and roles.

This integrates with the existing andy-rbac RBAC system for role and permission management.

## Acceptance Criteria

### Data Model
- [ ] `Organization` entity: `Id`, `Name`, `Slug`, `Description`, `CreatedAt`, `CreatedById`
- [ ] `OrganizationMember` entity: `OrganizationId`, `UserId`, `Role` (owner, admin, member), `JoinedAt`
- [ ] `Team` entity: `Id`, `OrganizationId`, `Name`, `Description`, `CreatedAt`
- [ ] `TeamMember` entity: `TeamId`, `UserId`, `Role` (lead, member), `JoinedAt`
- [ ] `Repository` entity gains `OrganizationId` FK (nullable for backward compatibility)
- [ ] Database migrations for all new entities

### API Endpoints
- [ ] `POST /api/v1/organizations` -- Create organization
- [ ] `GET /api/v1/organizations` -- List user's organizations
- [ ] `GET /api/v1/organizations/{id}` -- Get organization details
- [ ] `PUT /api/v1/organizations/{id}` -- Update organization
- [ ] `POST /api/v1/organizations/{id}/members` -- Invite member
- [ ] `DELETE /api/v1/organizations/{id}/members/{userId}` -- Remove member
- [ ] `POST /api/v1/organizations/{id}/teams` -- Create team
- [ ] `GET /api/v1/organizations/{id}/teams` -- List teams
- [ ] `POST /api/v1/organizations/{id}/teams/{teamId}/members` -- Add team member
- [ ] `DELETE /api/v1/organizations/{id}/teams/{teamId}/members/{userId}` -- Remove team member
- [ ] Repository endpoints filter by organization context when `X-Organization` header is present

### Frontend
- [ ] Organization switcher in the top navigation bar
- [ ] Organization settings page (name, members, teams)
- [ ] Team management within organization settings
- [ ] Repository list filtered by active organization
- [ ] Invite member flow with email input

### RBAC Integration
- [ ] Organization roles are registered in andy-rbac as application-scoped roles
- [ ] Permission checks include organization context
- [ ] MCP tools respect organization boundaries

### Testing & Documentation
- [ ] Unit tests for organization service, team service, membership management
- [ ] Integration tests for all new API endpoints
- [ ] Frontend tests for organization switcher and management pages
- [ ] `docs/design.md` updated with multi-tenancy architecture
- [ ] `docs/security.md` updated with organization-level access control
- [ ] `docs/requirements.md` updated with organization requirements
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Consider using `X-Organization` header or query parameter for org context
- Personal repositories (no organization) should continue to work as before
- andy-rbac already has team support -- leverage existing team/group models
- For the initial version, a user can be in multiple organizations but has one active at a time
- Organization slug should be URL-safe and unique

## Test Plan

- Unit: Organization CRUD, member management, team management
- Unit: Repository filtering by organization
- Integration: Create org, add member, create team, add repo, verify scoping
- Integration: User without org membership cannot access org repos
- Frontend: Organization switcher changes context, repo list updates
- RBAC: Verify permissions are scoped correctly through andy-rbac

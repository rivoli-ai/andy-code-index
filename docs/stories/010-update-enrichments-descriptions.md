# Story 010: Update Enrichments Type Descriptions

**Priority:** Low
**Component:** Frontend (Angular), Backend API
**Labels:** documentation, UX

## Description

Ensure the enrichment type descriptions displayed on the Enrichments page are accurate, complete, and up to date with all enrichment subtypes defined in the backend `EnrichmentSubtype` enum. The info section should always be visible (not collapsed) and should match the actual behavior of each enrichment handler.

## Acceptance Criteria

- [ ] Every value in the `EnrichmentSubtype` enum has a corresponding description in the info section
- [ ] Current enum values to verify against:
  - Architecture: Physical, DatabaseSchema, Dependencies, Ownership, Security
  - Development: Chunk, Snippet, SnippetSummary, Example, ExampleSummary, Quality
  - History: CommitDescription, CommitHistory
  - Usage: Cookbook, APIDocs, Wiki, Operations
- [ ] Descriptions accurately reflect what each enrichment handler produces (cross-reference with handler source code)
- [ ] The info section is always visible (no collapsible `<details>` element)
- [ ] Font sizes use CSS variables consistent with the rest of the app
- [ ] The `subtypeLabels` map in the component includes all subtypes
- [ ] The `typeToSubtypes` filter mapping includes all subtypes under their correct parent type
- [ ] Summary bar displays counts for all subtypes that have data
- [ ] Unit test verifies the descriptions section renders with all expected types
- [ ] `docs/design.md` updated if enrichment types have changed
- [ ] `README.md` reviewed and up to date; Apache 2.0 license confirmed

## Technical Notes

- Compare `EnrichmentSubtype` enum values with the frontend's `subtypeLabels` and `typeToSubtypes` maps
- Review each handler in `src/Andy.CodeIndex.Infrastructure/Handlers/` to verify descriptions match behavior
- The info section was previously behind a `<details>` toggle -- ensure it is now a static section
- If new enrichment types are added in the future, consider generating the descriptions from the backend

## Test Plan

- Unit: EnrichmentBrowserComponent renders all expected type descriptions
- Manual: Cross-reference each description with its handler's prompt/logic
- Regression: Adding a new enrichment type to the enum should cause a test failure until the description is added

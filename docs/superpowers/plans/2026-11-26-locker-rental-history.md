# Locker rental history implementation plan

**Goal:** Add the previous-contract tab required by UC 5.1, as requested on 2026-11-26.

**Design:** Keep the current-rental view. Add a history tab with ten closed rentals per page, newest first, and read-only details for a selected rental. Only the authenticated active Member can read their contracts. Reuse `LockerRentalView`, `PagedResponse<T>`, the existing repository, service, API client and Pager. No database changes.

**History:** Terminated, Abandoned or Cancelled contracts with a recorded handover or a paid rental transaction. Exclude unfinished/unpaid checkout attempts and soft-deleted records. Past contracts remain visible when their locker is deactivated or their membership expires.

**Interfaces:** `GET /api/lockers/my-rentals/history?page=1` returns `PagedResponse<LockerRentalView>` with fixed page size 10. `GET /api/lockers/my-rentals/{id}` returns an owned `LockerRentalView`, or 404 for a missing or another member's rental. Existing `/my-rental` stays compatible.

## Tasks

- [x] Add integration tests for paging, owner isolation, terminal states, empty history and authorization. Run to confirm missing endpoints fail.
- [x] Implement the repository queries, service validation and Member endpoints. Run the integration tests.
- [x] Add the API client calls and history tab with read-only details and empty/loading/error states. Verify client calls and rendering, then build.
- [x] Update the Markdown SRS and audit decisions for the user's confirmed rules. Preserve unresolved GBR-10 and interview-note/phone-search questions.
- [x] Review the diff and report verification results. Do not commit or push.

## Verification

- Integration: `dotnet test tests/VFMS.IntegrationTests/VFMS.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~Locker --verbosity minimal -m:1 -nr:false`: 29 passed, 0 failed.
- Client: `dotnet test tests/VFMS.ClientTests/VFMS.ClientTests.csproj --no-restore --filter FullyQualifiedName~LockerClientTests --verbosity minimal -m:1 -nr:false`: 19 passed, 0 failed. Includes rendered empty state and paging to the last contract.
- Both API and Blazor client compiled during test runs. Used tests outside the sandbox after the sandbox blocked testhost IPC.
- Scoped `git diff --check` passed. Integration storage is SQLite; no browser or SQL Server concurrency run. Existing unrelated compiler/analyzer warnings remain.
- Existing UC 4.13 working changes were preserved. No commit or push. GBR-10 remains unresolved; interview note and phone search were explained without changing their code.

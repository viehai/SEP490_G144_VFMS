# PT schema implementation report

## Changes

- `PTPurchase` projects package name, purchased credits and price from its order. Checkout captures credits in `OrderItem.ComponentTermsSnapshot`. Contract progress and attendance settlement derive credits from the purchased terms and recorded sessions. Counter quotes use the order item price and captured voucher discount until a payment row exists. Checkout and staff history retain that discount after the voucher hold is released.
- `PTSession` derives member identity from its purchase and proposal end time from the proposed start and original duration. `CancellationPending` and `CancellationRequested` preserve the original occupancy and reserved credits. Session and dashboard responses keep the existing status strings and carry `Cancellation` in the proposal type. Both parties' decisions, expiry and attendance use the expanded states.
- `PTFreeze` derives the start date for approved, active and completed requests. The response uses the purchase member and freeze limits. The six withdraw and early-resume decision states share `ChangeState`, while the original freeze review keeps `RequestStatus`. Early-resume responses read `ResumeDate` from the saved JSON payload.
- `PTAvailability` derives the one-hour end time. Weekly queries continue to require `DayOfWeek`. PT package management and checkout read the shared component freeze limits. The generic catalog retains its PT field aliases, while Gym and Yoga keep their existing values and policy paths.

## Files

- Domain entities `PTPurchase.cs`, `PTSession.cs`, `PTFreeze.cs`, `PTAvailability.cs`, `PackageComponent.cs` and `Enums.cs`.
- Application `PersonalTrainingRules.cs` and the `PersonalTrainingService` core, catalog, checkout, early resume, freeze, freeze change, reschedule and session partials.
- Infrastructure `PersonalTrainingRepository.cs`, `DashboardQueries.Trainer.cs`, `FeedbackRepository.PackageReviews.cs`, `MembershipPlanRepository.cs` and `MembershipPurchaseRepository.StaffOrders.cs`.
- Tests `CheckInTests.cs`, `SqliteTestContext.cs`, `PackageCatalogApiTests.Reviews.cs` and the `MembershipPurchaseApiTests` harness and Dashboards, Feature4Audit, Feature4Review, PersonalTraining, PersonalTrainingCounter, PersonalTrainingFlows and PersonalTrainingReviewFixes partials.

The production change adds no service or repository interface. Read-only projections are `PTPurchase.PurchasedItem`, `PackageNameSnapshot`, `TotalSessions` and `PurchasePrice`, `PTSession.MemberUserId`, `Member` and `ProposedEndTime`, `PTFreeze.ActualStartDate` and `PTAvailability.EndTime`. Tests build explicit order graphs with `TestSeed.PaidPTOrder`. The public catalog regression uses the production membership plan module in the integration test host.

## Verification

- The root recorded the schema tests failing against the original EF model before production edits. The first behavioral-regression attempt encountered compile errors from concurrent Trainer edits. The later API model test run passed after the shared catalog test host registered the membership plan module.
- `dotnet test tests/VFMS.UnitTests/VFMS.UnitTests.csproj --no-restore -m:1 -nodeReuse:false -v:q` passed all 787 tests.
- The integration command below passed all 44 selected tests. The selection included the 24 model assertions and API cases for the PT catalog and checkout freeze limits, payment and released-voucher amount, catalog edits after purchase, attendance without Gym membership, cancellation, early resume and withdrawal, contract and package reviews, contract history and trainer dashboard.
- An earlier broader membership, catalog and model run reported 509 passed, five skipped and seven failed. The newly added public catalog test passed after the test host fix. `ConcurrentManagerApprovalsOfOnePTFreezeAcceptExactlyOneDecision` required the isolated SQL Server connection configuration. The four locker theory failures came from the test fixture querying the derived `RentalType` and reading a rental without its pricing policy. These were handed to the root. `InvoicePdfPreservesMaximumPersistedDecimalPrecisionWithLongHistoricalItemName` failed on the SQLite decimal conversion and has the same recorded failure at `TestResults-baseline.log:113` and in `TestResults/baseline/baseline.trx`.

```powershell
$ptTests = @(
    'FeatureSchemaSimplificationTests'
    'PTCatalogAndCheckoutReadTheSharedComponentFreezeLimits'
    'ThirteenCreditContractKeepsItsFreezePolicyAfterManagerEditsThePackage'
    'EitherPartyCanAskToCancelAtLeast12HoursAheadAndAcceptanceReturnsAMakeUp'
    'PTCounter'
    'PTAttendanceDoesNotRequireGymMembership'
    'PTCanPageThroughOnlyTheirEndedContracts'
    'DashboardsAggregateMultiRoleAccountsAndKeepPersonalDataPrivate'
    'PtUsesTheCompletedSessionAndPtOrderWhileTrainerRatingRemainsSeparate'
    'FreezeRequestHoldsTheChosenSlotsAndWithdrawalWaitsForManagerDecisionBeforeReleasingThem'
    'EarlyResumeRechecksThePreviewAndPreservesOtherBookings'
    'PTCheckoutUsesTransferTimeAndRechecksReleasedSlots'
    'CompletedContractIsReviewedOnceWithScreenedTextAndListedUnderCompletedContracts'
)
$ptFilter = ($ptTests | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
dotnet test tests/VFMS.IntegrationTests/VFMS.IntegrationTests.csproj --no-restore -m:1 -nodeReuse:false --filter $ptFilter -v:q
```

## Migration inputs

- Each PT purchase must resolve to a real order with one purchased item. Component terms are a JSON array using the existing `Id`, numeric `Type`, `Days`, `Months` and `SessionCount` properties. PT is `Type: 2`. The projection accepts an array with one PT component, including other component types, and allows zero legacy credits. A multi-item order cannot safely provide a per-PT payment quote.
- PT freeze initial and change review remain independent. The enum names are `None`, `WithdrawPending`, `WithdrawApproved`, `WithdrawRejected`, `EarlyResumePending`, `EarlyResumeApproved` and `EarlyResumeRejected`. Early-resume payloads use the exact `ResumeDate` property emitted by the existing serializer.
- Historic session ownership and freeze requester must agree with the purchase member. If present, the old freeze limit copies and approved start date must agree with their canonical values. The old completion flag can disagree with status, so the migration needs to account for that difference. Legacy persisted double-session proposals require preflight rejection so they cannot become reschedule proposals.
- The root owns the EF configuration changes, Trainer and shared payment consumers, migration SQL and SQL verification. These code changes stayed in the current checkout for integration with that work.

## Isolated SQL Server verification

`FeatureSchemaMigrationTests.cs` starts from `20261126140850_TrainerInterviewTimestamp` in a unique test database and applies `20261127112843_SimplifyFeature3456Schema`. The valid case verifies the physical schema, shared identity backfills, preserved survey values, captured PT terms, cancellation and early-resume state. The other cases verify rollback on conflicting avatar URLs or active upload metadata (51101), orphan PT purchase (51110), approved freeze without an actual start (51124), and inconsistent locker terms (51131).

- The migration-only run passed **6 of 6** tests, exit code 0.
- The final run passed **9 of 9** tests, exit code 0. It included the six migration tests, both shared identity tests, and PT inactivity and rehiring.

```powershell
dotnet test tests/VFMS.IntegrationTests/VFMS.IntegrationTests.csproj --no-restore -m:1 -nodeReuse:false --output D:/SEP490_G144/Project/outputs/schema-sql-final --filter 'FullyQualifiedName~FeatureSchemaMigrationTests|FullyQualifiedName~TrainerSharedIdentityTests|FullyQualifiedName~PT_not_accepting' --logger 'console;verbosity=normal' -v:q
```

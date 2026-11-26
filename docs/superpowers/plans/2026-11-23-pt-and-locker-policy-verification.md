# PT and locker policy verification

Date: 2026-11-23. No application database migration was applied.

## Commands and results

- Final unit suite: 677 passed, 0 failed.
- Final client suite: 414 passed, 0 failed, including the final Customer sidebar adjustment.
- Full integration suite: 498 passed, 108 failed, 42 skipped. The skipped SQL Server tests require VFMS_TEST_SQL.
- Failure groups: 106 duplicate ApplicationUsers.PhoneNumber fixture failures, 1 duplicate ApplicationUserRoles assignment, 1 invoice decimal precision failure.
- The three failure categories were reproduced using the existing Debug assembly dated 2026-11-23 16:25, before this task changed source.
- New-rule RED: 14 API regression cases failed against the original pricing/booking eligibility; brief readiness toggle failed before implementation.
- Review regression RED: Customer freeze history returned 403 in 2 cases; Member paths passed. Fixed to use purchaser authorization with owner filtering.
- EF has-pending-model-changes: no pending changes.

## Focused verification

- PT/locker group: 55 passed, 0 failed, 2 SQL tests skipped.
- Readiness/validation targeted run: 36 passed, 0 failed.
- Final 14 new-rule API cases: 14 passed, 0 failed. Includes Customer freeze-history access after both payment methods.
- All tests use -c Policy --no-restore --disable-build-servers -m:1 to avoid locks in the application's Debug build.

## Full integration failure names

- VFMS.IntegrationTests.CompetitionApiTests.DisqualifiedParticipantDoesNotCountTowardsTheCapacity
- VFMS.IntegrationTests.CompetitionApiTests.ManagerCannotConfirmAResultWhileTheCompetitionIsStillRunning
- VFMS.IntegrationTests.CompetitionApiTests.MemberCanRegisterOnlyOnceAndOnlyInsideTheRegistrationWindow
- VFMS.IntegrationTests.CompetitionApiTests.RegistrationIsRejectedWhenTheCompetitionIsFull
- VFMS.IntegrationTests.CompetitionApiTests.StaffRecordsResultAndManagerConfirmsItAndGrantsThePrizeVoucher
- VFMS.IntegrationTests.ConsultationPersistenceTests.ManagerCanReplyAndCustomerNameRemainsAvailableForSoftDeletedAccount
- VFMS.IntegrationTests.MembershipPlanApiTests.ActiveSubscriptionPreventsDeletionAndRetainsAccessToInactivePlan
- VFMS.IntegrationTests.MembershipPlanApiTests.AdminCanFilterAndReadMembershipPlanAuditActions
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "durationInDays", value: -5)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "durationInDays", value: 0)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "durationInDays", value: 3,5)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "durationInDays", value: null)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "name", value: "   ")
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "name", value: null)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "price", value: -100)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "price", value: null)
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "serviceType", value: "PILATES")
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "serviceType", value: "PT")
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "status", value: "1")
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "status", value: "ARCHIVED")
- VFMS.IntegrationTests.MembershipPlanApiTests.CreateRejectsInvalidInput(field: "status", value: null)
- VFMS.IntegrationTests.MembershipPlanApiTests.EditingLegacyComboKeepsOriginalCalendarMonthsAndOtherEntitlements
- VFMS.IntegrationTests.MembershipPlanApiTests.FailedAuditWriteRollsBackNewPackageAndGymComponent
- VFMS.IntegrationTests.MembershipPlanApiTests.InvalidUpdateIsRejectedAndUnknownPlanReturnsNotFound
- VFMS.IntegrationTests.MembershipPlanApiTests.ListFiltersPaginatesAndCountsActiveSubscribersInTwoQueries
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagementEndpointsRequireManagerRole(role: "Admin", expected: Forbidden)
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagementEndpointsRequireManagerRole(role: "Customer", expected: Forbidden)
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagementEndpointsRequireManagerRole(role: "Member", expected: Forbidden)
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagementEndpointsRequireManagerRole(role: null, expected: Unauthorized)
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagerCanDeactivateAndReactivateAndBothTransitionsAreAudited
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagerCreatesReadsAndUpdatesPlanWithAuditHistory
- VFMS.IntegrationTests.MembershipPlanApiTests.ManagerCreatesYogaPlanThatCatalogFiltersByServiceType
- VFMS.IntegrationTests.MembershipPlanApiTests.PaidPlanWithoutSubscriptionsIsDeactivatedAndKeepsFinancialHistory
- VFMS.IntegrationTests.MembershipPlanApiTests.PurchaseCatalogReturnsOnlyActivePlansAndRejectsDirectAccessToInactivePlans(role: "Customer")
- VFMS.IntegrationTests.MembershipPlanApiTests.PurchaseCatalogReturnsOnlyActivePlansAndRejectsDirectAccessToInactivePlans(role: "Member")
- VFMS.IntegrationTests.MembershipPlanApiTests.PurchaseCatalogReturnsOnlyActivePlansAndRejectsDirectAccessToInactivePlans(role: null)
- VFMS.IntegrationTests.MembershipPlanApiTests.SoftDeletedOrderItemStillPreventsHardDeletion
- VFMS.IntegrationTests.MembershipPlanApiTests.UnsoldPlanAndItsComponentsArePermanentlyDeletedAndAudited
- VFMS.IntegrationTests.MembershipPlanApiTests.UpdateChangingStatusWritesUpdateAndStatusAudits
- VFMS.IntegrationTests.MembershipPlanApiTests.YogaPlanIsHiddenFromTheCatalogWhileNoYogaClassCanBeBooked(status: Cancelled, deadlineDays: 7, instructorTeaches: True)
- VFMS.IntegrationTests.MembershipPlanApiTests.YogaPlanIsHiddenFromTheCatalogWhileNoYogaClassCanBeBooked(status: Draft, deadlineDays: 7, instructorTeaches: True)
- VFMS.IntegrationTests.MembershipPlanApiTests.YogaPlanIsHiddenFromTheCatalogWhileNoYogaClassCanBeBooked(status: null, deadlineDays: 7, instructorTeaches: True)
- VFMS.IntegrationTests.MembershipPlanApiTests.YogaPlanIsHiddenFromTheCatalogWhileNoYogaClassCanBeBooked(status: Open, deadlineDays: -1, instructorTeaches: True)
- VFMS.IntegrationTests.MembershipPlanApiTests.YogaPlanIsHiddenFromTheCatalogWhileNoYogaClassCanBeBooked(status: Open, deadlineDays: 7, instructorTeaches: False)
- VFMS.IntegrationTests.MembershipPurchaseApiTests.InvoicePdfPreservesMaximumPersistedDecimalPrecisionWithLongHistoricalItemName
- VFMS.IntegrationTests.MembershipPurchaseApiTests.TransactionHistoryRetainsPaidPurchasesForCustomerWhoseMembershipHasExpired
- VFMS.IntegrationTests.PromotionApiTests.ClientCannotForgeUsageDerivedStatusActorOrSystemTimestamps
- VFMS.IntegrationTests.PromotionApiTests.CountIncludesOnlyConfirmedHistoryEvenWhenSoftDeletedAndQuotaExhaustionExpires
- VFMS.IntegrationTests.PromotionApiTests.DatabaseUniqueCodeConstraintRejectsCaseEquivalentDirectWrite
- VFMS.IntegrationTests.PromotionApiTests.DisablingAndEnablingPreservesRedemptionsFinancialHistoryAndExpiredEligibility
- VFMS.IntegrationTests.PromotionApiTests.DuplicateNormalizedCodeReturnsConflictAndDoesNotAudit(duplicate: "  SaVe20  ")
- VFMS.IntegrationTests.PromotionApiTests.DuplicateNormalizedCodeReturnsConflictAndDoesNotAudit(duplicate: "SAVE20")
- VFMS.IntegrationTests.PromotionApiTests.DuplicateNormalizedCodeReturnsConflictAndDoesNotAudit(duplicate: "save20")
- VFMS.IntegrationTests.PromotionApiTests.EffectiveStatusUsesServerClockAndExclusiveEndBoundary(minutesSinceStart: -1, status: "Scheduled")
- VFMS.IntegrationTests.PromotionApiTests.EffectiveStatusUsesServerClockAndExclusiveEndBoundary(minutesSinceStart: 0, status: "Active")
- VFMS.IntegrationTests.PromotionApiTests.EffectiveStatusUsesServerClockAndExclusiveEndBoundary(minutesSinceStart: 120, status: "Expired")
- VFMS.IntegrationTests.PromotionApiTests.EffectiveStatusUsesServerClockAndExclusiveEndBoundary(minutesSinceStart: 121, status: "Expired")
- VFMS.IntegrationTests.PromotionApiTests.EffectiveStatusUsesServerClockAndExclusiveEndBoundary(minutesSinceStart: 60, status: "Active")
- VFMS.IntegrationTests.PromotionApiTests.EmptyListIsSuccessfulAndDoesNotAuditReads
- VFMS.IntegrationTests.PromotionApiTests.EnableRequiresExplicitBooleanAndCannotAcceptDerivedStatusInstead
- VFMS.IntegrationTests.PromotionApiTests.EndMustBeStrictlyAfterStart(minutes: -1)
- VFMS.IntegrationTests.PromotionApiTests.EndMustBeStrictlyAfterStart(minutes: 0)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: "Admin", expected: Forbidden)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: "Customer", expected: Forbidden)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: "Member", expected: Forbidden)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: "PT", expected: Forbidden)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: "Staff", expected: Forbidden)
- VFMS.IntegrationTests.PromotionApiTests.EveryManagementEndpointRequiresManager(role: null, expected: Unauthorized)
- VFMS.IntegrationTests.PromotionApiTests.ExplicitDisabledStateOverridesEveryDateLifecycle(startDays: -1, endDays: 1)
- VFMS.IntegrationTests.PromotionApiTests.ExplicitDisabledStateOverridesEveryDateLifecycle(startDays: -2, endDays: -1)
- VFMS.IntegrationTests.PromotionApiTests.ExplicitDisabledStateOverridesEveryDateLifecycle(startDays: 1, endDays: 2)
- VFMS.IntegrationTests.PromotionApiTests.FailedAuditRollsBackEveryPrivilegedMutation(operation: "create")
- VFMS.IntegrationTests.PromotionApiTests.FailedAuditRollsBackEveryPrivilegedMutation(operation: "toggle")
- VFMS.IntegrationTests.PromotionApiTests.FailedAuditRollsBackEveryPrivilegedMutation(operation: "update")
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "code", value: " ")
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "code", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountType", value: "Unknown")
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountType", value: 99)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountType", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountValue", value: -1)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountValue", value: 0)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountValue", value: 101)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "discountValue", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "endAt", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "isEnabled", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "maxDiscountAmount", value: -1)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "maxDiscountAmount", value: 0)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "maxDiscountAmount", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "maxRedemptions", value: -1)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "maxRedemptions", value: 0)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "minimumSpend", value: -1)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "name", value: "  ")
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "name", value: null)
- VFMS.IntegrationTests.PromotionApiTests.InvalidConfigurationIsRejectedWithoutPartialMutation(field: "startAt", value: null)
- VFMS.IntegrationTests.PromotionApiTests.ListProjectsCountsInBoundedQueriesWithoutLoadingRedemptionHistory
- VFMS.IntegrationTests.PromotionApiTests.ManagerCreatesAndReadsCanonicalPromotionWithCompleteConfiguration(type: "Fixed", value: 50000, cap: null)
- VFMS.IntegrationTests.PromotionApiTests.ManagerCreatesAndReadsCanonicalPromotionWithCompleteConfiguration(type: "Percentage", value: 20, cap: 100000)
- VFMS.IntegrationTests.PromotionApiTests.ManagerUpdatesConfigurationAndCannotReuseAnotherPromotionsCode
- VFMS.IntegrationTests.PromotionApiTests.NoOpUpdateOrToggleAndReadsDoNotCreateAuditNoise
- VFMS.IntegrationTests.PromotionApiTests.NullableGlobalQuotaRemainsUnlimitedAndDoesNotConfigurePerUserQuota
- VFMS.IntegrationTests.PromotionApiTests.SoftDeletedHistoricalVoucherStillReservesItsCode
- VFMS.IntegrationTests.PromotionApiTests.UnknownPromotionReturnsNotFoundAndPermanentDeleteIsNotExposed
- VFMS.IntegrationTests.PromotionApiTests.UnsafeQuotaReductionRejectsUpdateAndPreservesAllConfigurationAndAudit
- VFMS.IntegrationTests.PromotionApiTests.UnspecifiedOrDefaultTimestampsAreRejected(start: "0001-01-01T00:00:00Z")
- VFMS.IntegrationTests.PromotionApiTests.UnspecifiedOrDefaultTimestampsAreRejected(start: "2026-11-13T12:00:00")
- VFMS.IntegrationTests.YogaPersistenceTests.InstructorTakesAttendanceByQrAndRosterThenSessionCloses
- VFMS.IntegrationTests.YogaPersistenceTests.ManagerCanCreateAndPublishClassForInstructorWithoutTrainerProfile
- VFMS.IntegrationTests.YogaPersistenceTests.MembersBookMatsUntilFullThenJoinTheWaitlist

## Follow-up PT attendance removal

- Kiosk/check-in/face recognition explicitly excluded by the user. No source changes in those modules.
- Removed Gym prerequisites for trainer-recorded PT attendance, the unused Gym queries/helpers, and obsolete Gym renewal warnings in PT purchase/freeze screens and DTOs.
- RED: missing, suspended and expired Gym cases returned409; active Gym case passed. GREEN: all four cases record Present, consume one PT session, and store attendance metadata; three related PT dispute cases also passed.
- Final unit suite: 677 passed, 0 failed. Final client suite: 414 passed, 0 failed.
- Full API suite: 500 passed, 109 failed, 42 skipped. The earlier108 failures persist. Additional intermittent concurrency failure outside PT: WalkInCustomerOnboardingApiTests.ConcurrentRequestsWithSamePhoneAllowOnlyOneCreation, SQLite pending-local-transaction exception in UserAdministrationRepository.SaveSetupAuditAsync. That module was not changed.
- No new schema migration is required for this follow-up.

- Isolated rerun: four PT attendance cases, no-Gym freeze planning, and the unrelated concurrency test all passed (6/6). The concurrency case also passed against the pre-task Debug assembly, confirming intermittent suite behavior.

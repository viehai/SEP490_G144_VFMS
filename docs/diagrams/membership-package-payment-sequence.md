# Luồng 2 — Thanh toán gói tập

## 1. Scope and evidence

These are two standalone UML sequence diagrams derived from the **current VFMS working-tree implementation inspected on 14 November 2026**. They describe design-level calls, replies, processing ownership and material alternatives. Production code is unchanged.

| Artifact | Contents |
|---|---|
| [Draw.io](membership-package-payment-sequence.drawio) | Exactly two editable pages: `Sequence 2.1 - Initiate VietQR Payment` and `Sequence 2.2 - SePay Settlement`. |
| [PlantUML](membership-package-payment-sequence.puml) | Exactly two `@startuml` / `@enduml` diagrams with the same participants, ordered messages, notes and alternatives as Draw.io. |
| [Sequence 2.1 PNG](sequence-2.1-initiate-vietqr-payment.png) | Report image of purchase initialization and initial VietQR display. |
| [Sequence 2.2 PNG](sequence-2.2-sepay-settlement.png) | Report image of webhook verification, settlement and fulfillment. |

Source priority: current code, then the latest accessible [Report 3 — Software Requirement Specification](https://docs.google.com/document/d/1ikvvPj3Q224wwM_5xqCjP680q0noxfZP/edit) (Drive modification time: **12 November 2026, 13:08:37 UTC**), then current entities / `VfmsDbContext`. The old sequence layout and ERD were not used to infer behavior. The pasted task's UML rules were available; separate sequence-reference images or files were not attached to this request.

## 2. Sequence 2.1 — Khởi tạo thanh toán gói tập bằng VietQR

The authenticated Customer selects packages, beneficiaries, optional activation dates and **VietQR** in `MembershipPlanCatalog`. Its `CheckoutAsync` validates the request and calls `MembershipPlanApiClient.PurchaseAsync`, which posts to **`/api/membership-purchases`**. The controller calls `MembershipPurchaseService.CreateAsync`.

The service loads users / roles and authoritative packages / components, validates eligibility and package terms, and constructs price and component-term snapshots. `SavePendingOrderAsync` computes the subtotal, calls `SepayVietQrGateway.CreateVietQr`, constructs a SePay / VietQR `PaymentTransaction`, and persists the order graph. `OrderStatus` and `PaymentStatus` both default to **Pending**. A serializable transaction completes before the API returns **HTTP 201** with `MembershipPurchaseResponse`.

The catalog then calls `NavigationManager.NavigateTo("/membership-purchases/{orderNumber}")`. `MembershipCheckout.OnParametersSetAsync` creates `MembershipCheckoutState` and performs an **initial GET** through `RefreshAsync` → `GetPurchaseAsync` → controller `Get` → service `GetAsync`. That call reads the purchaser-owned order and regenerates the QR information for a still-Pending VietQR order with positive `AmountDue`. **HTTP 200** supplies the checkout state. The UI displays the QR image, financial summary, bank / account / account name, transfer content and remaining time derived from `ExpiresAt` and `ServerTime`.

This diagram deliberately shows the successful initialization scenario: a valid authenticated buyer, accepted purchase, configured payment adapter and initial GET while the order remains Pending. Creation / initial-read failures are documented below instead of adding another deep alternative tree. SePay settlement and periodic polling are outside its endpoint. The catalog and checkout are separate real components; `MembershipCheckout` does **not** create the order.

## 3. Sequence 2.2 — Xác nhận thanh toán SePay và kích hoạt Membership

The interaction starts with SePay's **`POST /api/v1/commerce/payment/webhook/sepay`** and ends with the HTTP response to that same request. The customer's bank transfer is outside VFMS and is not drawn as a software call. `SepayWebhookController.Receive` forwards the original body bytes and authentication headers to `ConfirmSepayAsync`; the service calls `SepayVietQrGateway.Verify`.

Three `alt` frames expose the material choices:

1. **Verification failed / succeeded.** Failures propagate the actual gateway status: 401, 400 or 503, as `ProblemDetails` with an error code. No order lookup or fulfillment follows a verification failure.
2. **No matching payment / Paid + same reference / other matching payment.** “Matching” means an order exists and its payment has `PaymentMethod.VietQR` and `GatewayName == "SePay"`. A missing or mismatched payment returns 404. An already-Paid order with the same gateway transaction reference returns **HTTP 200 `{"success":true}` immediately**, without calling `FulfillAsync` again. Other matching payments proceed to settlement validation; this operand does not assume that they are necessarily still Pending.
3. **Settlement valid / rejected.** The service owns settlement validation. Accepted transfers call `FulfillAsync`; persistence shown under its nested service activation occurs **inside that method**. The enclosing serializable transaction completes before success is returned. Rejection produces **HTTP 409**, `ProblemDetails`, code `MSG-24`.

For a normal new purchase, fulfillment creates one `MemberMembership` for each purchased component, with **Active** or **Scheduled** status according to its effective dates. It marks payment and order Paid, records the received amount, reference and transfer timestamp, creates an `Invoice`, promotes eligible beneficiaries to Member where an entitlement is Active, and confirms a reserved voucher if present. An accepted scheduled membership is not falsely shown as immediately Active. The shared endpoint's renewal-specific behavior and rare Expired membership outcome are documented below.

There are no `opt` or `loop` fragments: optional voucher effects are summarized in the fulfillment note, while polling and per-item iteration are intentionally outside the visual detail level. All service / repository / HTTP calls use synchronous UML arrows because their callers await their results. Dashed open-arrow replies carry meaningful results. C# `async` does not imply a UML asynchronous message.

## 4. Participant-to-code mapping

Class names are wrapped across lines in the diagrams only for layout; concatenating their lines gives the actual class name.

| Diagram | Participant | UML role | Actual source / responsibility |
|---|---|---|---|
| 2.1 | Customer | Actor | External human purchaser. The API also permits an authenticated Member through the same path. |
| 2.1 | MembershipPlanCatalog | Boundary | [MembershipPlanCatalog.razor](../../src/VFMS.Client/Components/Memberships/MembershipPlanCatalog.razor): cart, payment selection, submit and navigation. |
| 2.1 | MembershipCheckout | Boundary | [MembershipCheckout.razor](../../src/VFMS.Client/Pages/MembershipCheckout.razor), [code-behind](../../src/VFMS.Client/Pages/MembershipCheckout.razor.cs): checkout lifecycle and display; delegates state and transport. |
| 2.1 | MembershipPurchasesController | Control | [MembershipPurchasesController.cs](../../src/VFMS.API/Controllers/MembershipPurchasesController.cs): authenticated create / get API coordinator. |
| Both | MembershipPurchaseService | Control | [MembershipPurchaseService.cs](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [voucher partial](../../src/VFMS.Application/Services/MembershipPurchaseService.Voucher.cs), [renewal partial](../../src/VFMS.Application/Services/MembershipPurchaseService.Renewal.cs): business decisions and fulfillment. |
| Both | MembershipPurchaseRepository | `«repository»` participant | [MembershipPurchaseRepository.cs](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs): queries, locks, transactions and persistence. It is not a domain Entity. |
| Both | VFMS Database | Database cylinder | [VfmsDbContext.cs](../../src/VFMS.Infrastructure/Data/VfmsDbContext.cs): EF Core access to persisted users, packages, orders, payments, memberships, invoices and voucher redemptions. The DbContext is folded into this persistence boundary. |
| Both | SepayVietQrGateway | Boundary | [SepayVietQrGateway.cs](../../src/VFMS.Infrastructure/Payments/SepayVietQrGateway.cs): local VietQR URL construction and inbound SePay verification. It is a VFMS adapter, distinct from external SePay. |
| 2.2 | SePay | `«External System»` Actor | External sender of the inbound bank-transfer webhook. |
| 2.2 | SepayWebhookController | Boundary | [SepayWebhookController.cs](../../src/VFMS.API/Controllers/SepayWebhookController.cs): raw webhook request entry point. |

`MembershipPlanApiClient`, `MembershipCheckoutState`, `NavigationManager`, `TimeProvider` and generic MVC result mapping are folded into their callers at this abstraction level; they are not invented or renamed components. Dependency registration is confirmed by [MembershipPurchaseModule.cs](../../src/VFMS.API/Extensions/MembershipPurchaseModule.cs), which binds the service, repository and payment-gateway interfaces to these implementations.

## 5. Message-to-code traceability

Interaction IDs occur in PlantUML comments and in Draw.io message metadata, not as decorative visual elements. Each row includes the request and meaningful replies belonging to the interaction. Design labels such as “Load checkout data” and “Persist settlement changes” are abstractions, not claimed method names.

| Diagram interaction | Actual class | Actual method | Source file | Notes |
|---|---|---|---|---|
| I01 — Choose packages / VietQR | MembershipPlanCatalog | Razor event bindings; `CheckoutAsync` | [catalog](../../src/VFMS.Client/Components/Memberships/MembershipPlanCatalog.razor) | Cart lines carry `PlanId`, trimmed beneficiary email and activation date; payment select defaults to VietQR. |
| I02 — POST purchase | MembershipPlanCatalog; MembershipPlanApiClient; MembershipPurchasesController | `CheckoutAsync`; `PurchaseAsync`; `SendAsync`; `Create` | [catalog](../../src/VFMS.Client/Components/Memberships/MembershipPlanCatalog.razor), [API client](../../src/VFMS.Client/Services/MembershipPlanApiClient.cs), [controller](../../src/VFMS.API/Controllers/MembershipPurchasesController.cs) | `PurchaseAsync` posts JSON to `api/membership-purchases`; transport attaches the Bearer token. Controller route plus `[HttpPost]` defines the endpoint. |
| I03 — Create purchase | MembershipPurchaseService | `CreateAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Receives authenticated user ID, request and cancellation token; runs work via `ExecuteInSerializableTransactionAsync`. |
| I04 — Load checkout data / database query / replies | MembershipPurchaseRepository; VfmsDbContext | `LoadCheckoutUsersAsync`; `LoadPlansAsync` | [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs) | In code, users / roles load first, purchaser eligibility is checked, then packages / components load. The combined visual query returns both authoritative sets on the success path. |
| I05 — Validate purchase / build order | MembershipPurchaseService | `CreateAsync`; `CanPurchase`; `IsPurchasable`; `IsValidComponent`; `NewOrder`; `NewOrderItem` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Grouped business processing. Date and purchaser checks occur earlier than some grouped reads; all validations precede persistence. Package price / name and component terms are snapshotted. |
| I06 — Save pending order | MembershipPurchaseService | `SavePendingOrderAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Computes subtotal and checks the amount limit before requesting QR information. Nested activation encloses QR generation and persistence. |
| I07 — Create VietQR / QR reply | SepayVietQrGateway | `CreateVietQr` | [gateway](../../src/VFMS.Infrastructure/Payments/SepayVietQrGateway.cs) | Arguments: order number and subtotal. Returns `VietQrPaymentResponse` with image URL and bank / account / transfer content. No outbound HTTP call is made by this method. |
| I08 — Build pending payment | MembershipPurchaseService; Order; PaymentTransaction | `SavePendingOrderAsync`; entity initializers | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [Order](../../src/VFMS.Domain/Entities/Order.cs), [PaymentTransaction](../../src/VFMS.Domain/Entities/PaymentTransaction.cs) | Creates payment after QR generation: `GatewayName="SePay"`, `PaymentMethod=VietQR`, `AmountDue=subtotal`; order and payment defaults are Pending. |
| I09 — Persist pending purchase / replies | MembershipPurchaseRepository; VfmsDbContext | `Add(Order)`; `SaveChangesAsync` | [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs), [DbContext](../../src/VFMS.Infrastructure/Data/VfmsDbContext.cs) | EF inserts the Order / OrderItems / PaymentTransaction graph; context supplies creation timestamps. Transaction wrapper commits before control returns to the HTTP endpoint. |
| I10 — Purchase result / HTTP 201 | MembershipPurchaseService; ApiResultExtensions | `SavePendingOrderAsync`; `ToResponse`; `ToActionResult` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [HTTP mapping](../../src/VFMS.API/Extensions/ApiResultExtensions.cs) | Actual data: `MembershipPurchaseResponse`; actual service status: 201. Mapper returns the data directly, not the `Result<T>` wrapper. |
| I11 — Open checkout route | MembershipPlanCatalog; NavigationManager; MembershipCheckout | `CheckoutAsync`; `NavigateTo`; `OnParametersSetAsync` | [catalog](../../src/VFMS.Client/Components/Memberships/MembershipPlanCatalog.razor), [checkout lifecycle](../../src/VFMS.Client/Pages/MembershipCheckout.razor.cs) | Navigation target: `/membership-purchases/{Uri.EscapeDataString(purchase.OrderNumber)}`. The UI-to-UI arrow abstracts routing and component lifecycle; it is not a direct CLR invocation on the checkout class. |
| I12 — Initial GET / GetAsync | MembershipCheckout; MembershipCheckoutState; MembershipPlanApiClient; MembershipPurchasesController; MembershipPurchaseService | `OnParametersSetAsync`; `RefreshAsync`; `GetPurchaseAsync`; `Get`; `GetAsync` | [checkout lifecycle](../../src/VFMS.Client/Pages/MembershipCheckout.razor.cs), [state](../../src/VFMS.Client/Services/MembershipCheckoutState.cs), [API client](../../src/VFMS.Client/Services/MembershipPlanApiClient.cs), [controller](../../src/VFMS.API/Controllers/MembershipPurchasesController.cs), [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Visual “GET purchase (orderNumber)” is **`GET /api/membership-purchases/{orderNumber}`**. The POST response is not directly used as checkout display state. |
| I13 — Find owned order / state replies | MembershipPurchaseRepository | `FindOrderAsync` | [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs) | Arguments include purchaser ID and `forUpdate:false`. No-tracking read includes items, payment / invoice and a separate voucher-redemption snapshot query. |
| I14 — Regenerate checkout QR | MembershipPurchaseService; SepayVietQrGateway | `GetAsync`; `CreateVietQr` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [gateway](../../src/VFMS.Infrastructure/Payments/SepayVietQrGateway.cs) | Arguments: order number and persisted `AmountDue`. Executes only for a still-Pending VietQR payment with positive due amount. |
| I15 — Checkout response / HTTP 200 | MembershipPurchaseService; ApiResultExtensions | `ToResponse`; `ToActionResult` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [response contract](../../src/VFMS.Contracts/Memberships/MembershipPurchaseContracts.cs), [HTTP mapping](../../src/VFMS.API/Extensions/ApiResultExtensions.cs) | Includes `VietQr`, `Subtotal`, `AmountDue`, `ExpiresAt`, `ServerTime`, items, discount / voucher snapshot and state fields. |
| I16 — Display payment information | MembershipCheckout; MembershipCheckoutState | Razor rendering; `Remaining` | [checkout UI](../../src/VFMS.Client/Pages/MembershipCheckout.razor), [state](../../src/VFMS.Client/Services/MembershipCheckoutState.cs) | QR shown only while Pending, due amount positive, QR non-null and remaining time positive. UI also displays bank / account details and order summary. |
| W01 — Receive SePay POST | SepayWebhookController | `Receive` | [webhook controller](../../src/VFMS.API/Controllers/SepayWebhookController.cs) | Exact endpoint: `/api/v1/commerce/payment/webhook/sepay`; anonymous at MVC authorization level, with gateway authentication below. Request limit is 16,384 bytes. |
| W02 — Confirm webhook | MembershipPurchaseService | `ConfirmSepayAsync` | [webhook controller](../../src/VFMS.API/Controllers/SepayWebhookController.cs), [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Exact arguments: Authorization, `X-SePay-Timestamp`, `X-SePay-Signature`, original raw body bytes and cancellation token. |
| W03 — Verify / transfer result | SepayVietQrGateway | `Verify`; `HasValidSignature`; `HasValidApiKey` | [gateway](../../src/VFMS.Infrastructure/Payments/SepayVietQrGateway.cs) | Configured HMAC takes precedence; otherwise API-key authentication. JSON and transfer fields are verified before `Result<SepayTransfer>` succeeds. |
| W04 — Verification rejection / HTTP failure | MembershipPurchaseService; ApiResultExtensions | `ConfirmSepayAsync`; `ToActionResult` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [gateway](../../src/VFMS.Infrastructure/Payments/SepayVietQrGateway.cs), [HTTP mapping](../../src/VFMS.API/Extensions/ApiResultExtensions.cs) | Preserves gateway code / status: authentication 401, invalid transfer 400, unavailable configuration 503. |
| W05 — Load authoritative payment state / database replies | MembershipPurchaseService; MembershipPurchaseRepository | `FindRenewalPurchaserAsync`; `ExecuteInSerializableTransactionAsync`; conditional `LockUsersAsync`; `FindOrderAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs) | Grouping preserves the real order: renewal lookup before transaction; optional renewal-purchaser lock; then order read with `purchaserId:null`, `forUpdate:true`, including items, payment / invoice and voucher redemption. This interaction is persistence, not payment validation. |
| W06 — No matching payment / HTTP 404 | MembershipPurchaseService | `ConfirmSepayAsync`; `OrderNotFound<T>` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Missing order, missing payment, wrong method or wrong gateway all return `MEMBERSHIP_ORDER_NOT_FOUND`, 404. |
| W07 — Idempotent callback / HTTP 200 | MembershipPurchaseService | `ConfirmSepayAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Exact condition: `order.OrderStatus == Paid && payment.GatewayTransactionReference == transfer.TransactionId`, after the matching-payment check. Returns `Result<SepayWebhookResponse>.Success(new(true))`; skips settlement checks and fulfillment. |
| W08 — Validate settlement | MembershipPurchaseService; MembershipPurchaseRepository | `ConfirmSepayAsync`; `HasSafeVoucherSettlement`; conditional `SepayReferenceUsedAsync`; conditional `PrepareRenewalFulfillmentAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [voucher partial](../../src/VFMS.Application/Services/MembershipPurchaseService.Voucher.cs), [renewal partial](../../src/VFMS.Application/Services/MembershipPurchaseService.Renewal.cs), [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs) | Service owns all decisions. The reference-usage query is a repository operation nested within this abstraction and is short-circuited if earlier checks fail. Renewal preparation occurs only for a renewal order. |
| W09 — Fulfill membership | MembershipPurchaseService | `FulfillAsync`; `ReadComponentTerms`; `EndDate`; `PromoteAsync` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs) | Real private service self-call, not a participant. Internally locks beneficiaries, loads existing memberships, schedules entitlements, promotes active beneficiaries, updates order / payment, creates invoice and confirms a reserved voucher. |
| W10 — Persist successful settlement / replies | MembershipPurchaseRepository; VfmsDbContext | `SaveChangesAsync`; enclosing `ExecuteInSerializableTransactionAsync` | [repository](../../src/VFMS.Infrastructure/Repositories/MembershipPurchaseRepository.cs), [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [DbContext](../../src/VFMS.Infrastructure/Data/VfmsDbContext.cs) | Save is invoked **inside `FulfillAsync`**. Transaction commits on return from the work delegate; a second fulfillment or duplicate save is not implied. |
| W11 — Successful webhook / HTTP 200 | MembershipPurchaseService; ApiResultExtensions | `ConfirmSepayAsync`; `ToActionResult` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [response contract](../../src/VFMS.Contracts/Memberships/MembershipPurchaseContracts.cs), [HTTP mapping](../../src/VFMS.API/Extensions/ApiResultExtensions.cs) | Visual `Success(true)` means `Result<SepayWebhookResponse>.Success(new(true))`; controller serializes data as `{"success":true}` with HTTP 200. |
| W12 — Settlement rejected / HTTP 409 | MembershipPurchaseService; ApiResultExtensions | `PaymentRejected<T>`; `ToActionResult` | [service](../../src/VFMS.Application/Services/MembershipPurchaseService.cs), [HTTP mapping](../../src/VFMS.API/Extensions/ApiResultExtensions.cs) | Error code `MSG-24`; Vietnamese failure message from `MembershipPurchaseMessages.Failed`; no fulfillment on rejection. |

## 6. Implementation details intentionally omitted from the visual diagrams

### Purchase validation and errors

The client validates with `MembershipPurchaseRequestValidator`; MVC FluentValidation is registered in [Program.cs](../../src/VFMS.API/Program.cs). The request requires 1–50 non-null items, positive plan IDs, a valid optional beneficiary email of at most 256 characters and payment method VietQR or Cash. The diagram selects VietQR. MVC authentication / authorization, model-binding failures and request validation are outside its successful-path detail.

In `CreateAsync`, the activation date must be today through today + 30 days, using UTC+07 local dates. Purchaser and beneficiaries must be Active, not banned, and hold Customer or Member role. Emails are normalized; ambiguous or missing beneficiary matches fail eligibility. Each package must be Active with positive whole-VND price, contain a GYM component and have valid positive durations; PT components require a positive session count. Duration calculations must fit supported dates. Per-item validation and snapshot construction are interleaved in code; the diagram groups them rather than showing each loop iteration. Subtotal must not exceed `9_999_999_999_999_999m`.

| Failure | Actual HTTP / code |
|---|---|
| Activation date invalid | 400 / `MSG-25` |
| Purchaser not eligible | 403 / `PURCHASER_NOT_ELIGIBLE` |
| Beneficiary not eligible | 400 / `BENEFICIARY_NOT_ELIGIBLE` |
| Plan not purchasable | 400 / `PLAN_NOT_PURCHASABLE` |
| Amount exceeds supported maximum | 400 / `ORDER_AMOUNT_INVALID` |
| VietQR adapter unavailable at creation | 503 / `PAYMENT_UNAVAILABLE`; no pending graph is added / saved |
| Initial GET cannot find an owned order with payment | 404 / `MEMBERSHIP_ORDER_NOT_FOUND` |

`GetAsync` computes an effective Expired response when now reaches expiry, without persisting that transition. It returns QR information only while Pending and due amount is positive; a null QR from changed configuration is returned as missing QR information, not transformed into a GET 503. The diagram's initial successful GET assumes these conditions remain satisfied.

### QR generation and initial display

`CreateVietQr` checks bank / account configuration and the presence of HMAC secret or API key. It constructs an **`https://img.vietqr.io/image/{BankId}-{AccountNumber}-compact2.png?...`** URL locally with whole-VND `amount`, `addInfo` and escaped account name. It does not call SePay or VietQR over HTTP. The browser later loads the image through the checkout `<img src>`; that resource fetch is folded into UI rendering.

Order number format is `ORD-yyyyMMdd-{12 uppercase hexadecimal characters}`. Transfer content removes all hyphens, producing `ORDyyyyMMdd{12HEX}`. The online window is fixed at **CreatedAt + 15 minutes**. `MembershipCheckoutState.Remaining` accounts for elapsed time since the server response rather than depending only on the browser wall clock.

### Webhook verification

- HMAC mode is selected whenever `WebhookSecret` is nonblank. It validates an integer Unix timestamp within ±5 minutes, a `sha256=` prefix plus 64 hexadecimal characters and HMAC-SHA256 of **`timestamp + "." + rawBody`**, compared in fixed time. There is no API-key fallback when configured HMAC validation fails.
- Otherwise the `Authorization` scheme must be `Apikey` (case-insensitive) and the parameter must match the configured key in fixed time.
- JSON must deserialize to `SepayWebhookRequest`. The transfer needs positive `Id`, `TransferType == "in"`, positive amount, matching account number, and `TransactionDate` in `yyyy-MM-dd HH:mm:ss`, interpreted as UTC+07. It cannot be more than 30 seconds in the future.
- The `Code` and `Content` fields must yield exactly one distinct normalized order number matching the gateway's ORD pattern. Optional hyphens and case variations are accepted by the regex. `payload.Id` becomes the transaction ID string.

| Gateway rejection | Actual HTTP / code |
|---|---|
| Not configured | 503 / `PAYMENT_UNAVAILABLE` |
| Invalid HMAC signature / timestamp | 401 / `SEPAY_SIGNATURE_INVALID` |
| Invalid API key | 401 / `SEPAY_API_KEY_INVALID` |
| Malformed payload, invalid transfer or ambiguous / missing order code | 400 / `SEPAY_TRANSFER_INVALID` |

### Settlement, transactions, locks and reference safety

`ConfirmSepayAsync` verifies first, performs `FindRenewalPurchaserAsync`, then enters `ExecuteInSerializableTransactionAsync`. SQL Server reads use `UPDLOCK, HOLDLOCK` when locking orders / beneficiaries; alternate providers use their normal query path inside the serializable transaction. The diagram's load-state abstraction does not claim a separate explicit payment-row lock: `FindOrderAsync` locks the **order** and loads its payment and related state. For renewal callbacks the purchaser is locked before the order. `FindOrderAsync` loads the voucher redemption separately with `IgnoreQueryFilters`.

After the precise idempotency branch, all of these must hold:

- State is `(Order Pending, Payment Pending)` **or** `(Order Expired, Payment Cancelled)`.
- `HasSafeVoucherSettlement` succeeds: no redemption; a Reserved redemption whose discount matches subtotal minus due; or a Released redemption only when the order is Pending and due equals full subtotal. Other redemption states reject settlement.
- Transfer amount equals persisted `AmountDue` exactly. The accepted transfer window is **[CreatedAt truncated down to its Unix second, ExpiresAt)**; equality with expiry rejects settlement. A late-delivered callback can settle an expired order if the transfer itself was timely and all other rules still pass.
- `SepayReferenceUsedAsync` finds no SePay payment with this reference, including soft-deleted rows. This query executes only if earlier conditions pass; it is deliberately hidden inside the service's validation abstraction.
- If the order is a renewal, `PrepareRenewalFulfillmentAsync` also succeeds.

A Paid order with a **different** reference does not enter the idempotent branch and fails normal state validation with 409. An already-used reference on another order also rejects with 409. [VfmsDbContext](../../src/VFMS.Infrastructure/Data/VfmsDbContext.cs) additionally defines a unique filtered index on non-null SePay `GatewayTransactionReference`, a unique order-number index, a one-to-one Order–PaymentTransaction relationship and a one-to-one PaymentTransaction–Invoice relationship. These guards are persistence constraints, not extra participants.

### Fulfillment internals

`FulfillAsync` calls `LockUsersAsync` for distinct beneficiaries, then `LoadCurrentMembershipsAsync` for existing Active / Scheduled / Frozen / Suspended entitlements ending today or later. It reads purchased component snapshots (falling back to current package components for legacy snapshots), iterates items in ID order, and calculates each component's term. A future requested start is honored; otherwise new-purchase terms start today. Terms of the same component type queue after an existing overlapping end date. PT entitlements receive their remaining session count. `PromoteAsync` obtains the Member role with `GetMemberRoleAsync` only if eligible active beneficiaries still hold Customer role, and replaces those Customer role links.

Payment receives Paid status, amount received, gateway reference and actual transfer time. Order receives Paid status and completion time. A persisted `Invoice` is numbered `INV-yyyyMMdd-{order.Id:D4}`. A Reserved redemption becomes Confirmed with transfer time as `RedeemedAt`. All are saved inside `FulfillAsync`; the transaction wrapper commits afterward. Exceptions from saving are not converted here into a business 409 / 200 response; normal failure propagation and transaction disposal apply. The visual success reply represents a completed successful transaction.

The same webhook handles renewal orders. `PrepareRenewalFulfillmentAsync` loads related renewal orders for update, rejects an already-paid sibling renewal or overlapping current entitlements, and cancels replacement Pending renewals while releasing their reserved vouchers. `FulfillAsync` honors a renewal's preserved activation date; with a sufficiently delayed callback the computed term can already be **Expired**. Sequence 2.2 summarizes the ordinary new-purchase Active / Scheduled outcome; shared renewal helpers are mapped above without expanding another use case.

### Adjacent interactions omitted

`MembershipCheckout.PollAsync` uses a one-second timer, refreshes every third tick (about three seconds), and keeps running while no purchase has loaded or the purchase is Pending. It stops on a terminal loaded status or component disposal. This is a separate repeated status-read interaction after initial display, so Sequence 2.1 contains only its first read and no polling loop. SePay does not push directly to the Blazor UI.

Voucher apply / remove and full-discount `CompleteNoChargeAsync` are separate checkout operations. Their recalculation / reservation calls are not part of initial purchase creation; zero-charge completion does not manufacture a bank transfer. Cash confirmation is a separate Staff endpoint and is excluded from these VietQR diagrams. `MembershipCommerceWorker` runs order expiry and membership transitions on a 30-second timer; `ExpireOrdersAsync` marks unpaid online orders Expired, payments Cancelled and reserved vouchers Released, while `AdvanceMembershipsAsync` activates due Scheduled terms. Neither worker is a participant in these request-response sequences.

## 7. Implementation Differences — Report 3 vs current code

Compared against the latest accessible Report 3 linked in section 1. Its use-case tables contain repeated column text; section / rule identifiers below identify the relevant statements rather than relying on extracted paragraph order.

| Requirement / document | Current implementation | Diagram decision |
|---|---|---|
| II.2.2 Purchase Membership Packages lists SePay / VNPAY as secondary payment gateways; GBR-48 also mentions VNPAY. | This purchase path's online method is VietQR and adapter / persisted gateway is SePay. | Show `SepayVietQrGateway` and external SePay only; no invented redirect or VNPAY participant. |
| I.5.2 API-04 describes a unique `VFMS-xxx` payment code and an outbound VietQR service interaction. | `CreateVietQr` builds an image URL locally with an ORD-based transfer reference; the browser retrieves the image. | Show a service-to-adapter call and QR-information reply; no server-to-VietQR HTTP request. |
| II.2.2 normal flow places online method selection / “Pay Now” after entering checkout and seeing the summary. | The catalog submits the chosen payment method while creating the order; checkout subsequently performs a GET. | Preserve the real catalog → POST → navigation → checkout GET order. |
| II.2.2 normal flow describes clearing the cart and redirecting to a Payment Success screen after settlement. | `ConfirmSepayAsync` only responds to SePay. Checkout polls and renders the success message / invoice number on the existing page. The catalog cart is local component state, not a cart cleared by the webhook. | End Sequence 2.2 at the webhook response; do not invent a success-screen redirect, server-side cart-clear call or UI push. |
| I.2.1 BF-01 says the generated invoice is sent to the Member; I.5.2 API-05 includes payment receipts / invoices among email purposes. | `FulfillAsync` creates an Invoice record. This payment path does not call an email sender or dispatch an invoice delivery job. | Summarize invoice creation only; do not add an email / notification participant. |
| II.2.2 timeout alternative uses Cancelled; GBR-48 describes 15 minutes of inactivity and cancellation, while JOB-01 describes pending-order expiry. | Window is fixed at CreatedAt + 15 minutes. Job persistence is Order Expired / Payment Cancelled. A callback delivered later can still settle a timely transfer if its state, voucher, reference and other checks pass. | Document precise state / timing rules; do not draw unconditional rejection merely because callback delivery is late. |

Report 3 and code agree on the SePay webhook route, server-side payment confirmation, multi-package purchase, optional beneficiaries / dates, and the distinction between immediate Active and future Scheduled memberships. These agreements do not require invented discrepancies.

## 8. Assumptions and validation

- **Presentation assumption:** English design / method labels accompany the requested Vietnamese titles. Stereotyped rectangular Draw.io boundaries / controls are used as the task's permitted fallback; actors use UML actor shapes, repositories use `«repository»`, and persistence uses a database cylinder. PlantUML uses native boundary / control notation.
- **Scenario assumption for 2.1:** the selected method is VietQR, the initial submission succeeds, and the first checkout read happens before expiry. All omitted failure behavior is listed above. No deployed configuration or live payment execution was assumed or tested.
- **Evidence limitation:** separate reference-diagram materials were unavailable in this request. Standard UML and the supplied detailed notation / abstraction requirements govern presentation.
- **Validation:** two PlantUML blocks, exactly two named Draw.io pages, matching ordered participant / message / reply lists, matching `alt` operands and fulfillment note, balanced activations, dashed vertical lifelines, UML actors and database notation were checked. Both formats were rendered locally for visual review. PNGs are exports from the actual Draw.io models through the official diagrams.net viewer. There are no phase banners, fake B/C/E icons or domain-entity repository stereotypes.
- **Production preservation:** fingerprints of all non-build files under `src` and `tests` were compared before and after documentation work. Existing unrelated working-tree changes were preserved. No production code, configuration, schema or migrations were modified, and no application / payment tests were needed for this documentation-only change.

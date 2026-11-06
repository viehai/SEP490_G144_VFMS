# Password recovery API

## Endpoints

Both routes are anonymous and return JSON. Neither route signs the user in automatically.

### `POST /api/auth/forgot-password`

```json
{ "email": "member@example.com" }
```

- `202`: the email request was queued. The same message is returned for registered, unknown, locked, inactive, banned, and deleted accounts: `If an eligible account exists for this email, password reset instructions will be sent.`
- `400`: invalid email format.
- `503` / `AUTH_RECOVERY_UNAVAILABLE`: SMTP/reset URL is not configured or the email queue is full. This response also does not depend on account existence.
- `429` / `AUTH_RECOVERY_RATE_LIMITED`: request limit exceeded; respect `Retry-After`.

The HTTP request does not query accounts or wait for SMTP. A background worker checks eligibility and sends mail only to an active, non-banned, non-deleted account. Requests for the same account have a 60-second cooldown (configurable).

### `POST /api/auth/reset-password`

```json
{
  "token": "<exact 64-character uppercase hexadecimal token from the email>",
  "newPassword": "<new password>",
  "confirmPassword": "<same new password>"
}
```

- `200`: password changed; log in again with the new password.
- `400`: invalid input, weak/unconfirmed/unchanged password, expired token, consumed token, or ineligible account.
- `429`: request limit exceeded.

Passwords follow the existing GBR-26 rules: 8–32 characters including uppercase, lowercase, a digit and a special character. The token is case-sensitive and must be sent in the JSON body, not in an API URL.

## Token and session handling

- Generate a cryptographically random 256-bit token; store only its SHA-256 hash in `ApplicationUsers.ResetTokenHash` and its UTC expiry in `ApplicationUsers.ResetTokenExpiresAt`. There is no separate password-reset table. Only the latest issued link is valid for each account.
- Default token lifetime is 15 minutes, configurable between 1 and 60 minutes.
- Successful recovery atomically changes the BCrypt password hash, clears both reset fields and revokes the account's refresh tokens. Conditional database updates prevent two stale reset requests from both succeeding. Errors roll back all changes. Ordinary tracked password changes also clear both reset fields; profile edits leave them intact.
- Issuance enforces the cooldown atomically on the user row, using the stored expiry minus the configured token lifetime to infer the last issue time. Keep the token-lifetime setting stable while links remain outstanding. A resend after the cooldown replaces the previous link; failure to deliver an older email cannot clear a newer token.
- This uses the existing JWT model: already-issued access tokens remain valid until their normal expiry (up to 30 minutes). Recovery revokes refresh tokens; it does not introduce a new access-token blacklist/security-stamp scheme.
- Email links use the configured `PasswordReset:ResetUrl` plus `#token=...`. The future frontend reset screen must read the fragment and submit the token in the JSON body. No reset screen is implemented by this backend change. Do not log reset bodies, tokens or email contents; use `Referrer-Policy: no-referrer` on the future reset screen.
- The API applies a shared limit of 10 recovery requests per IP per 15 minutes across both endpoints. Behind a reverse proxy, configure trusted forwarded headers before rate limiting; do not trust arbitrary forwarded IPs.
- The bounded in-memory email queue holds 100 requests and has one consumer. It is best-effort: pending requests are lost on process restart, and delivery failures are logged by exception type without email/token data. Failed-delivery tokens are invalidated. Users can request another email after the cooldown. A distributed deployment should replace this queue and local rate limit with durable/distributed equivalents.

The approach follows the [OWASP Forgot Password guidance](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html) on generic responses, asynchronous processing, expiring single-use tokens and abuse controls. SMTP uses [MailKit](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm) with required STARTTLS or implicit TLS and normal certificate validation.

## Configuration and database

`src/VFMS.API/appsettings.json` contains empty SMTP placeholders. Until real configuration is supplied, recovery requests return `503` instead of pretending to send mail. Put credentials in environment variables, local user secrets or a deployment secret store; never commit them.

Example configuration keys:

```text
Smtp__Host=smtp.example.com
Smtp__Port=587
Smtp__Security=StartTls
Smtp__FromEmail=no-reply@your-domain.example
Smtp__FromName=Victor Fitness
Smtp__Username=<SMTP username>
Smtp__Password=<SMTP password or provider app password>
PasswordReset__ResetUrl=https://your-frontend.example/auth/reset-password
PasswordReset__TokenLifetimeMinutes=15
PasswordReset__RequestCooldownSeconds=60
```

For port 465 use `SslOnConnect`. A trusted TLS SMTP relay can omit both username and password. The reset URL must be an absolute HTTPS URL without query/fragment/user-info; HTTP is accepted only for loopback development URLs. It is never derived from the request's Host header.

The unapplied `AddPasswordResetTokens` migration was removed after checking the configured database's migration history. Its replacement, `AddPasswordResetFieldsToUsers`, adds only two nullable columns and a filtered unique index to `ApplicationUsers`; it creates no tables. The existing API `DbInitializer` applies migrations when the API starts; alternatively use:

```powershell
dotnet ef database update --project src/VFMS.Infrastructure --startup-project src/VFMS.API
```

For any other database where the discarded table migration was already applied, first revert that database to `FixPolicyAndBannedWordFK` using the old migration files, then remove the old migration and apply the replacement. Do not merely delete an applied migration from source; preserve any unrelated later migrations and use a corrective migration instead when those exist.

## Validation

```powershell
dotnet test tests/VFMS.UnitTests/VFMS.UnitTests.csproj
dotnet test tests/VFMS.IntegrationTests/VFMS.IntegrationTests.csproj -p:UseAppHost=false
```

Integration tests use isolated SQLite databases and an in-memory ASP.NET test server. They do not send real email or modify SQL Server. They cover token hashing, expiry, replay, account eligibility, stale competing resets, transaction rollback, HTTP validation, generic responses and rate limiting. Verify SMTP delivery separately after configuring the intended provider.

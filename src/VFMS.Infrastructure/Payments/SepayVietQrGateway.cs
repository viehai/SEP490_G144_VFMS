using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Memberships;

namespace VFMS.Infrastructure.Payments;

public sealed class SepayVietQrOptions
{
    public string BankId { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}

public sealed class SepayVietQrGateway(IOptions<SepayVietQrOptions> options) : IMembershipPaymentGateway
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly Regex TransferCode = new(
        @"(?<![A-Z0-9])ORD-?([0-9]{8})-?([A-F0-9]{12})(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly SepayVietQrOptions _settings = options.Value;

    private bool UsesHmac => !string.IsNullOrWhiteSpace(_settings.WebhookSecret);

    private bool IsConfigured =>
        (UsesHmac || !string.IsNullOrWhiteSpace(_settings.ApiKey)) &&
        Regex.IsMatch(_settings.BankId, @"\A[A-Za-z0-9]{1,20}\z") &&
        Regex.IsMatch(_settings.AccountNumber, @"\A[0-9]{1,19}\z") &&
        !string.IsNullOrWhiteSpace(_settings.AccountName);

    public VietQrPaymentResponse? CreateVietQr(string orderNumber, decimal amount)
    {
        if (!IsConfigured)
            return null;

        var content = orderNumber.Replace("-", "");
        var url = FormattableString.Invariant(
            $"https://img.vietqr.io/image/{_settings.BankId}-{_settings.AccountNumber}-compact2.png?amount={amount:0}&addInfo={content}&accountName={Uri.EscapeDataString(_settings.AccountName)}");
        return new(url, _settings.BankId, _settings.AccountNumber, _settings.AccountName, content);
    }

    public Result<SepayTransfer> Verify(string authorization, string timestamp, string signature, byte[] rawBody, DateTimeOffset now)
    {
        if (!IsConfigured)
            return Result<SepayTransfer>.Failure("SePay is unavailable.", "PAYMENT_UNAVAILABLE", 503);
        if (UsesHmac)
        {
            if (!HasValidSignature(timestamp, signature, rawBody, now))
                return Result<SepayTransfer>.Failure("Invalid SePay signature.", "SEPAY_SIGNATURE_INVALID", 401);
        }
        else if (!HasValidApiKey(authorization))
        {
            return Result<SepayTransfer>.Failure("Invalid SePay API key.", "SEPAY_API_KEY_INVALID", 401);
        }

        SepayWebhookRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SepayWebhookRequest>(rawBody, WebJson);
        }
        catch (JsonException)
        {
            return InvalidTransfer();
        }

        if (payload is not { Id: > 0, TransferType: "in", TransferAmount: > 0 } ||
            payload.AccountNumber != _settings.AccountNumber ||
            !DateTime.TryParseExact(payload.TransactionDate, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return InvalidTransfer();

        var transferredAt = new DateTimeOffset(date, TimeSpan.FromHours(7));
        if (transferredAt > now.AddSeconds(30))
            return InvalidTransfer();

        var orderNumbers = TransferCode.Matches($"{payload.Code} {payload.Content}")
            .Select(match => $"ORD-{match.Groups[1].Value}-{match.Groups[2].Value.ToUpperInvariant()}")
            .Distinct().Take(2).ToArray();
        return orderNumbers is [var orderNumber]
            ? Result<SepayTransfer>.Success(new(orderNumber, payload.Id.ToString(CultureInfo.InvariantCulture),
                payload.TransferAmount, transferredAt))
            : InvalidTransfer();
    }

    private bool HasValidApiKey(string authorization) =>
        AuthenticationHeaderValue.TryParse(authorization, out var header) &&
        header.Scheme.Equals("Apikey", StringComparison.OrdinalIgnoreCase) &&
        header.Parameter is { } key &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(_settings.ApiKey));

    private bool HasValidSignature(string timestamp, string signature, byte[] rawBody, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var signedAt) ||
            signedAt < now.AddMinutes(-5).ToUnixTimeSeconds() || signedAt > now.AddMinutes(5).ToUnixTimeSeconds() ||
            signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal))
            return false;

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromHexString(signature[7..]);
        }
        catch (FormatException)
        {
            return false;
        }
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(_settings.WebhookSecret));
        hmac.AppendData(Encoding.UTF8.GetBytes(timestamp + "."));
        hmac.AppendData(rawBody);
        return CryptographicOperations.FixedTimeEquals(signatureBytes, hmac.GetHashAndReset());
    }

    private static Result<SepayTransfer> InvalidTransfer() =>
        Result<SepayTransfer>.Failure("Invalid SePay transfer.", "SEPAY_TRANSFER_INVALID");
}

namespace VFMS.Contracts.Users;

public sealed record FindPendingCustomerRequest(string? Email = null, string? PhoneNumber = null);

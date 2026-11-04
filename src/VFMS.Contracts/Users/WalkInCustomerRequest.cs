namespace VFMS.Contracts.Users;

public sealed record WalkInCustomerRequest(
    string FullName,
    string PhoneNumber,
    string? Email = null
);

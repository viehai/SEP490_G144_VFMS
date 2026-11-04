namespace VFMS.Contracts.SystemAdministration;

/// <summary>Create or rename a banned word (UC-85).</summary>
public sealed record BannedWordRequest(string Word);

/// <summary>Query for the banned word list (UC-85).</summary>
public sealed record BannedWordListRequest(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record BannedWordResponse(
    int Id,
    string Word,
    bool IsActive,
    DateTime CreatedAt,
    string CreatedByUserId,
    string? CreatedByName
);

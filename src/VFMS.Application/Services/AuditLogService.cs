using MapsterMapper;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.SystemAdministration;
using VFMS.Domain.Enums;

namespace VFMS.Application.Services;

/// <summary>UC-87 View System Audit Logs. Logs are read-only (GBR-65).</summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly IMapper _mapper;

    public AuditLogService(IAuditLogRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<Result<PagedResponse<AuditLogResponse>>> GetLogsAsync(
        AuditLogListRequest request, CancellationToken cancellationToken = default)
    {
        if (request.From is not null && request.To is not null && request.From > request.To)
            return Result<PagedResponse<AuditLogResponse>>.Failure(
                "The start date cannot be after the end date.", "AUDIT_INVALID_DATE_RANGE");

        AuditActionType? actionType = null;
        if (!string.IsNullOrWhiteSpace(request.ActionType))
        {
            if (!Enum.TryParse(request.ActionType.Trim(), ignoreCase: true, out AuditActionType parsed) ||
                !Enum.IsDefined(parsed))
                return Result<PagedResponse<AuditLogResponse>>.Failure(
                    "Action type filter is invalid.", "AUDIT_INVALID_ACTION_TYPE");
            actionType = parsed;
        }

        // A date-only "To" means "through the end of that day".
        DateTime? toExclusive = request.To is not { } to
            ? null
            : to.TimeOfDay == TimeSpan.Zero ? to.AddDays(1) : to.AddTicks(1);

        var (logs, total) = await _repository.SearchAsync(
            request.From,
            toExclusive,
            string.IsNullOrWhiteSpace(request.ActorUserId) ? null : request.ActorUserId.Trim(),
            actionType,
            string.IsNullOrWhiteSpace(request.Entity) ? null : request.Entity.Trim(),
            request.Page,
            request.PageSize,
            cancellationToken);

        var items = logs.Select(_mapper.Map<AuditLogResponse>).ToArray();
        return Result<PagedResponse<AuditLogResponse>>.Success(
            new PagedResponse<AuditLogResponse>(request.Page, request.PageSize, total, items));
    }

    public async Task<Result<AuditLogResponse>> GetLogAsync(int id, CancellationToken cancellationToken = default)
    {
        var log = await _repository.GetByIdAsync(id, cancellationToken);
        return log is null
            ? Result<AuditLogResponse>.Failure("Audit log entry was not found.", "AUDIT_LOG_NOT_FOUND", 404)
            : Result<AuditLogResponse>.Success(_mapper.Map<AuditLogResponse>(log));
    }
}

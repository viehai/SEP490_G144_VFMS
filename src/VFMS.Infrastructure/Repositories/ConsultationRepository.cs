using Microsoft.EntityFrameworkCore;
using VFMS.Application.Interfaces;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.Infrastructure.Repositories;

public sealed class ConsultationRepository : IConsultationRepository
{
    private readonly VfmsDbContext _context;

    public ConsultationRepository(VfmsDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<ConsultationConversation> Items, int Total)> SearchAsync(
        string? search, string? status, string? customerUserId, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ConsultationConversations
            .AsNoTracking()
            .Include(conversation => conversation.CustomerUser)
            .Include(conversation => conversation.ManagerUser)
            .Include(conversation => conversation.Messages).ThenInclude(message => message.SenderUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(customerUserId))
            query = query.Where(conversation => conversation.CustomerUserId == customerUserId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(conversation => conversation.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(conversation =>
                conversation.Subject.Contains(search) ||
                conversation.CustomerUser.FullName.Contains(search));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(conversation => conversation.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ConsultationConversation?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _context.ConsultationConversations
            .Include(conversation => conversation.CustomerUser)
            .Include(conversation => conversation.ManagerUser)
            .Include(conversation => conversation.Messages).ThenInclude(message => message.SenderUser)
            .FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);

    public Task<ApplicationUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.ApplicationUsers.FirstOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    public async Task<ConsultationConversation> CreateAsync(
        ConsultationConversation conversation, ConsultationMessage message,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        _context.ConsultationConversations.Add(conversation);
        await _context.SaveChangesAsync(cancellationToken);
        message.ConversationId = conversation.Id;
        message.Conversation = conversation;
        conversation.Messages.Add(message);
        _context.ConsultationMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return conversation;
    }

    public async Task<ConsultationMessage> AddMessageAsync(
        ConsultationConversation conversation, ConsultationMessage message,
        string? managerUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (managerUserId is not null)
        {
            conversation.ManagerUserId = managerUserId;
            conversation.ManagerUser = await _context.ApplicationUsers.FirstAsync(user => user.UserId == managerUserId, cancellationToken);
        }
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UpdatedAt = DateTime.UtcNow;
        message.CreatedAt = DateTime.UtcNow;
        conversation.Messages.Add(message);
        _context.ConsultationMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message;
    }
}

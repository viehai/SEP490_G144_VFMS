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
            .Include(conversation => conversation.ManagerUser)
            .Include(conversation => conversation.Messages)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(customerUserId))
            query = query.Where(conversation => conversation.CustomerUserId == customerUserId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(conversation => conversation.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(conversation =>
                conversation.Subject.Contains(search) ||
                _context.ApplicationUsers.IgnoreQueryFilters().Any(user =>
                    user.UserId == conversation.CustomerUserId && user.FullName.Contains(search)));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(conversation => conversation.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        await LoadMissingCustomerUsersAsync(items, asNoTracking: true, cancellationToken);
        await LoadMissingMessageSendersAsync(items, asNoTracking: true, cancellationToken);
        return (items, total);
    }

    public async Task<ConsultationConversation?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var conversation = await _context.ConsultationConversations
            .Include(conversation => conversation.ManagerUser)
            .Include(conversation => conversation.Messages)
            .FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);
        if (conversation is not null)
        {
            await LoadMissingCustomerUsersAsync([conversation], asNoTracking: false, cancellationToken);
            await LoadMissingMessageSendersAsync([conversation], asNoTracking: false, cancellationToken);
        }
        return conversation;
    }

    private async Task LoadMissingCustomerUsersAsync(
        IReadOnlyCollection<ConsultationConversation> conversations,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var missingCustomers = conversations
            .Where(conversation => conversation.CustomerUser is null ||
                string.IsNullOrWhiteSpace(conversation.CustomerUser.FullName))
            .ToArray();
        if (missingCustomers.Length == 0)
            return;

        var customerIds = missingCustomers
            .Select(conversation => conversation.CustomerUserId)
            .Distinct()
            .ToArray();
        IQueryable<ApplicationUser> users = _context.ApplicationUsers
            .IgnoreQueryFilters()
            .Where(user => customerIds.Contains(user.UserId));
        if (asNoTracking)
            users = users.AsNoTracking();

        var customers = await users.ToDictionaryAsync(user => user.UserId, cancellationToken);
        foreach (var conversation in missingCustomers)
            if (customers.TryGetValue(conversation.CustomerUserId, out var customer))
                conversation.CustomerUser = customer;
    }

    private async Task LoadMissingMessageSendersAsync(
        IReadOnlyCollection<ConsultationConversation> conversations,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var missingSenders = conversations
            .SelectMany(conversation => conversation.Messages)
            .Where(message => message.SenderUser is null ||
                string.IsNullOrWhiteSpace(message.SenderUser.FullName))
            .ToArray();
        if (missingSenders.Length == 0)
            return;

        var senderIds = missingSenders
            .Select(message => message.SenderUserId)
            .Distinct()
            .ToArray();
        IQueryable<ApplicationUser> users = _context.ApplicationUsers
            .IgnoreQueryFilters()
            .Where(user => senderIds.Contains(user.UserId));
        if (asNoTracking)
            users = users.AsNoTracking();

        var senders = await users.ToDictionaryAsync(user => user.UserId, cancellationToken);
        foreach (var message in missingSenders)
            if (senders.TryGetValue(message.SenderUserId, out var sender))
                message.SenderUser = sender;
    }

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

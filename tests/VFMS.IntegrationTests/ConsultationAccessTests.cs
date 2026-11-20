using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VFMS.API.Controllers;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Common;
using VFMS.Contracts.Consultations;
using VFMS.Contracts.Feedback;

namespace VFMS.IntegrationTests;

public sealed class ConsultationAccessTests
{
    [Fact]
    public async Task StaffCanListOpenAndReplyToConsultations()
    {
        var service = new RecordingConsultationService();
        var controller = new ConsultationsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", "staff-user"),
                        new Claim(ClaimTypes.Role, "Staff")
                    ], "test"))
                }
            }
        };

        await controller.List(new ConsultationListRequest(), CancellationToken.None);
        await controller.Get(17, CancellationToken.None);
        await controller.Send(17, new ConsultationSendRequest("Hello"), CancellationToken.None);

        Assert.Null(service.CustomerUserId);
        Assert.True(service.IsSupportAgent);
        Assert.Equal("staff-user", service.SenderUserId);
    }

    [Fact]
    public void StaffCanUseFeedbackManagementAndCloseConsultations()
    {
        AssertStaffAllowed(typeof(FeedbackController), nameof(FeedbackController.List));
        AssertStaffAllowed(typeof(FeedbackController), nameof(FeedbackController.Approve));
        AssertStaffAllowed(typeof(FeedbackController), nameof(FeedbackController.Reject));
        AssertStaffAllowed(typeof(FeedbackController), nameof(FeedbackController.Reply));
        AssertStaffAllowed(typeof(FeedbackController), nameof(FeedbackController.Visibility));
        AssertStaffAllowed(typeof(ConsultationsController), nameof(ConsultationsController.Close));
    }

    private static void AssertStaffAllowed(Type controllerType, string actionName)
    {
        var authorize = controllerType.GetMethod(actionName)!.GetCustomAttributes(typeof(AuthorizeAttribute), false)
            .Cast<AuthorizeAttribute>()
            .Single();
        Assert.Contains("Staff", authorize.Roles!.Split(','));
    }

    private sealed class RecordingConsultationService : IConsultationService
    {
        public string? CustomerUserId { get; private set; }
        public bool IsSupportAgent { get; private set; }
        public string? SenderUserId { get; private set; }

        public Task<Result<PagedResponse<ConsultationConversationResponse>>> ListAsync(
            ConsultationListRequest request, string? customerUserId = null, CancellationToken cancellationToken = default)
        {
            CustomerUserId = customerUserId;
            return Task.FromResult(Result<PagedResponse<ConsultationConversationResponse>>.Failure("test"));
        }

        public Task<Result<ConsultationConversationResponse>> GetAsync(
            int id, string userId, bool isManager, CancellationToken cancellationToken = default)
        {
            IsSupportAgent = isManager;
            return Task.FromResult(Result<ConsultationConversationResponse>.Failure("test"));
        }

        public Task<Result<ConsultationConversationResponse>> CreateAsync(
            ConsultationCreateRequest request, string customerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ConsultationConversationResponse>.Failure("test"));

        public Task<Result<ConsultationConversationResponse>> SendAsync(
            int id, ConsultationSendRequest request, string senderUserId, bool isManager,
            CancellationToken cancellationToken = default)
        {
            IsSupportAgent = isManager;
            SenderUserId = senderUserId;
            return Task.FromResult(Result<ConsultationConversationResponse>.Failure("test"));
        }

        public Task<Result<ConsultationConversationResponse>> CloseAsync(
            int id, string managerUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ConsultationConversationResponse>.Failure("test"));
    }
}

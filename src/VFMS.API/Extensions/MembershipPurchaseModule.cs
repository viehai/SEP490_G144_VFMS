using Microsoft.Extensions.DependencyInjection.Extensions;
using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Infrastructure.Payments;
using VFMS.Infrastructure.Invoices;
using VFMS.Infrastructure.Repositories;
using VFMS.Infrastructure.Workers;

namespace VFMS.API.Extensions;

public static class MembershipPurchaseModule
{
    public static IServiceCollection AddMembershipPurchaseModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<SepayVietQrOptions>(configuration.GetSection("MembershipPayments"));
        services.AddScoped<IMembershipPaymentGateway, SepayVietQrGateway>();
        services.AddScoped<IMembershipPurchaseRepository, MembershipPurchaseRepository>();
        services.AddScoped<IMembershipPurchaseService, MembershipPurchaseService>();
        services.AddScoped<IMembershipTransactionRepository, MembershipTransactionRepository>();
        services.AddScoped<IMembershipTransactionService, MembershipTransactionService>();
        services.AddSingleton<IMembershipInvoicePdfRenderer, MembershipInvoicePdfRenderer>();
        services.AddScoped<IMembershipInvoiceExportService, MembershipInvoiceExportService>();
        services.AddHostedService<MembershipCommerceWorker>();
        return services;
    }
}

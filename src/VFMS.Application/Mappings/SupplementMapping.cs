using Mapster;
using VFMS.Contracts.Supplements;
using VFMS.Domain.Entities;
using VFMS.Domain.Enums;

namespace VFMS.Application.Mappings;

public sealed class SupplementMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SupplementProduct, SupplementProductResponse>()
            .Map(d => d.Name, s => s.ProductName)
            .Map(d => d.Category, s => s.CatalogCategory)
            .Map(d => d.Status, s => s.ProductStatus.ToString())
            .Map(d => d.CanOrder, s => s.ProductStatus == SupplementProductStatus.Active);

        config.NewConfig<SupplementOrderItem, SupplementOrderItemResponse>()
            .Map(d => d.ProductId, s => s.SupplementProductId)
            .Map(d => d.ProductName, s => s.SupplementProduct == null ? null : s.SupplementProduct.ProductName)
            .Map(d => d.ImageUrl, s => s.SupplementProduct == null ? null : s.SupplementProduct.ImageUrl)
            .Map(d => d.LineTotal, s => s.Quantity * s.UnitPriceAtPurchase);

        config.NewConfig<SupplementOrder, SupplementOrderResponse>()
            .Map(d => d.Status, s => s.OrderStatus.ToString())
            .Map(d => d.FulfillmentType, s => s.DeliveryType.ToString())
            .Map(d => d.PlacedAt, s => s.CreatedAt)
            .Map(d => d.CustomerName, s => s.Customer == null ? null : s.Customer.FullName)
            .Map(d => d.CanCancel, s => s.OrderStatus == SupplementOrderStatus.Pending)
            .Map(d => d.Items, s => s.Items.OrderBy(i => i.Id))
            .Map(d => d.Delivery, s => s.DeliveryType != SupplementDeliveryType.HomeDelivery
                ? null
                : new SupplementOrderDeliveryResponse(
                    s.RecipientName,
                    s.DeliveryAddress,
                    s.DeliveryNote,
                    s.Customer == null ? null : s.Customer.PhoneNumber,
                    s.Delivery == null ? null : s.Delivery.CourierName,
                    s.Delivery == null ? null : s.Delivery.TrackingCode,
                    s.Delivery == null ? null : s.Delivery.DeliveredAt));
    }
}

using Mapster;
using VFMS.Contracts.Equipment;
using VFMS.Domain.Entities;

namespace VFMS.Application.Mappings;

public sealed class EquipmentMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Equipment, EquipmentResponse>()
            .Map(d => d.Name, s => s.EquipmentName)
            .Map(d => d.Code, s => s.EquipmentCode)
            .Map(d => d.CategoryId, s => s.CatalogCategoryId)
            .Map(d => d.CategoryName, s => s.CatalogCategory == null ? null : s.CatalogCategory.CategoryName)
            .Map(d => d.Brand, s => s.EquipmentBrand)
            .Map(d => d.Model, s => s.EquipmentModel)
            .Map(d => d.Status, s => s.EquipmentStatus.ToString())
            .Map(d => d.DescriptionUrl, s => s.EquipmentDescriptionUrl);

        config.NewConfig<EquipmentSafetyConfirmation, SafetyConfirmationResponse>()
            .Map(d => d.ConfirmedByName, s => s.ConfirmedByUser == null ? null : s.ConfirmedByUser.FullName);

        config.NewConfig<EquipmentIssueReport, EquipmentIssueResponse>()
            .Map(d => d.EquipmentName, s => s.Equipment == null ? string.Empty : s.Equipment.EquipmentName)
            .Map(d => d.EquipmentCode, s => s.Equipment == null ? null : s.Equipment.EquipmentCode)
            .Map(d => d.EquipmentStatus, s => s.Equipment == null ? string.Empty : s.Equipment.EquipmentStatus.ToString())
            .Map(d => d.IncidentType, s => s.IncidentType.ToString())
            .Map(d => d.Status, s => s.IssueStatus.ToString())
            .Map(d => d.ReportedByName, s => s.ReportedByUser == null ? null : s.ReportedByUser.FullName)
            .Map(d => d.HandledByName, s => s.HandledByUser == null ? null : s.HandledByUser.FullName)
            .Map(d => d.SafetyConfirmations, s => s.SafetyConfirmations.OrderBy(c => c.ConfirmedAt));
    }
}

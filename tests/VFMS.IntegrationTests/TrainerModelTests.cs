using Microsoft.EntityFrameworkCore;
using VFMS.Domain.Entities;
using VFMS.Infrastructure.Data;

namespace VFMS.IntegrationTests;

public class TrainerModelTests
{
    [Fact]
    public void Applicant_can_submit_multiple_applications_and_tracking_code_is_unique()
    {
        using var db = new VfmsDbContext(new DbContextOptionsBuilder<VfmsDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ModelOnly;Integrated Security=true").Options);
        var model = db.Model.FindEntityType(typeof(TrainerApplication))!;
        var fk = model.GetForeignKeys().Single(x => x.Properties.Any(p => p.Name == "ApplicantUserId"));
        Assert.False(fk.IsUnique);
        Assert.Contains(model.GetIndexes(), i => i.IsUnique && i.Properties.Any(p => p.Name == "TrackingCode"));
    }
}

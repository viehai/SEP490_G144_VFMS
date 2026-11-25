using System.Text;
using VFMS.Client.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.SystemAdministration;
using VFMS.Contracts.Supplements;
using Xunit;

namespace VFMS.ClientTests;

/// <summary>CSV exports of the equipment list, supplement orders and audit logs.</summary>
public sealed class ListExportsTests
{
    private static string[] Lines(byte[] csv)
    {
        Assert.Equal(Encoding.UTF8.GetPreamble(), csv[..3]);
        return Encoding.UTF8.GetString(csv[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void EquipmentRowsUseVietnameseLabelsAndPlainNumbers()
    {
        var row = new EquipmentResponse(1, "Treadmill, T5", "TM-0001", 2, "Máy chạy bộ", "Life Fitness", "T5", "Operational",
            new DateOnly(2024, 5, 1), 85_000_000.5m, null, "Gần cửa sổ", DateTime.UtcNow, DateTime.UtcNow, RecentIssueCount: 3);

        var lines = Lines(ListExports.Equipment([row]));

        Assert.Equal("Mã,Tên thiết bị,Danh mục,Hãng,Model,Trạng thái,Ngày mua,Giá mua (VND),Sự cố 90 ngày,Ghi chú", lines[0]);
        Assert.Equal("TM-0001,\"Treadmill, T5\",Máy chạy bộ,Life Fitness,T5,Hoạt động,01/05/2024,85000000.5,3,Gần cửa sổ", lines[1]);
    }

    [Fact]
    public void OrderRowsListTheProductsAndDelivery()
    {
        var order = new SupplementOrderResponse(5, "SO-1", "OutForDelivery", "HomeDelivery", 2_500_000m,
            new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc), null, null, "u1", "Nguyễn Văn A", false,
            [new(1, 7, "Whey", null, 2, 1_000_000m, 2_000_000m), new(2, 8, "Bar", null, 1, 500_000m, 500_000m)],
            new("Nguyễn Văn A", "1 Lê Lợi", null, "0900000001", "GHN", "GHN123", null));

        var cells = Lines(ListExports.SupplementOrders([order]))[1];

        Assert.StartsWith("SO-1,", cells);
        Assert.Contains(",3,2500000,Whey × 2; Bar × 1,", cells);
        Assert.Contains("0900000001,1 Lê Lợi,GHN,GHN123", cells);
    }

    [Fact]
    public void AuditRowsEscapeQuotesAndFormulas()
    {
        var log = new AuditLogResponse(9, new DateTime(2026, 11, 19, 1, 0, 0, DateTimeKind.Utc), "u1", "Admin", "Update", "Gym", 1,
            "Name=\"Old\"", "=cmd()", "127.0.0.1");

        var cells = Lines(ListExports.AuditLogs([log], type => type == "Update" ? "Cập nhật" : type))[1];

        Assert.EndsWith(",Admin,u1,Cập nhật,Gym,1,\"Name=\"\"Old\"\"\",'=cmd()", cells);
        Assert.DoesNotContain("127.0.0.1", cells);
    }

    [Fact]
    public async Task CollectReadsEveryPageUntilTheEnd()
    {
        var asked = new List<int>();
        var (rows, truncated) = await CsvFile.CollectAsync((page, size) =>
        {
            asked.Add(page);
            var items = Enumerable.Range((page - 1) * size, page < 3 ? size : 30).ToArray();
            return Task.FromResult(new PagedResponse<int>(page, size, 230, items));
        });

        Assert.Equal([1, 2, 3], asked);
        Assert.Equal(230, rows.Count);
        Assert.False(truncated);
    }

    [Fact]
    public async Task CollectStopsAtTheLimit()
    {
        var (rows, truncated) = await CsvFile.CollectAsync((page, size) =>
            Task.FromResult(new PagedResponse<int>(page, size, 9000, Enumerable.Repeat(page, size).ToArray())));

        Assert.Equal(CsvFile.MaxRows, rows.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void FileNameHasTheDate() =>
        Assert.Equal("VFMS-thiet-bi-2026-11-19.csv", CsvFile.FileName("thiet-bi", new DateTime(2026, 11, 19)));
}

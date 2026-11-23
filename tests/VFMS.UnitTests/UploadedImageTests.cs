using VFMS.Application.Interfaces;
using VFMS.Application.Services;
using VFMS.Contracts.Common;
using VFMS.Contracts.Equipment;
using VFMS.Contracts.Equipment.Validators;
using VFMS.Contracts.Supplements;
using VFMS.Contracts.Supplements.Validators;
using VFMS.Domain.Entities;
using Xunit;

namespace VFMS.UnitTests;

/// <summary>Equipment and supplement photos: upload checks, storage and the image field rules.</summary>
public sealed class UploadedImageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private static readonly byte[] Webp = [.."RIFF"u8, 0, 0, 0, 0, .."WEBP"u8, 1, 2];

    private sealed class Repo : IUploadedImageRepository
    {
        public List<UploadedImage> Images { get; } = [];
        public void Add(UploadedImage image) => Images.Add(image);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UploadedImage?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Images.FirstOrDefault(i => i.ImageKey == key));
    }

    private static Task<Result<UploadedImageResponse>> Upload(Repo repo, byte[] bytes, string name = "photo.png") =>
        new UploadedImageService(repo).UploadAsync("manager-1", name, bytes.Length, new MemoryStream(bytes));

    [Theory]
    [MemberData(nameof(Images))]
    public async Task StoresAnImageAndReturnsItsPath(byte[] bytes, string contentType)
    {
        var repo = new Repo();

        var result = await Upload(repo, bytes);

        Assert.Equal(201, result.StatusCode);
        var stored = Assert.Single(repo.Images);
        Assert.Equal(contentType, stored.ContentType);
        Assert.Equal(UploadedImages.PathFor(stored.ImageKey), result.Data!.Url);
        Assert.True(UploadedImages.IsUploadedPath(result.Data.Url));
        Assert.Equal("manager-1", stored.UploadedByUserId);
        Assert.True((await new UploadedImageService(repo).GetAsync(stored.ImageKey)).IsSuccess);
    }

    public static TheoryData<byte[], string> Images() => new()
    {
        { Png, "image/png" }, { Jpeg, "image/jpeg" }, { Webp, "image/webp" },
    };

    [Fact]
    public async Task RefusesAFileThatIsNotAnImageWhateverItsName()
    {
        var repo = new Repo();

        var result = await Upload(repo, "<script>alert(1)</script>"u8.ToArray(), "photo.png");

        Assert.Equal("IMAGE_INVALID_TYPE", result.ErrorCode);
        Assert.Empty(repo.Images);
    }

    [Fact]
    public async Task RefusesEmptyAndTooLargeFiles()
    {
        var repo = new Repo();
        var service = new UploadedImageService(repo);

        Assert.Equal("IMAGE_EMPTY", (await service.UploadAsync("m", "a.png", 0, new MemoryStream())).ErrorCode);
        Assert.Equal("IMAGE_TOO_LARGE", (await service.UploadAsync("m", "a.png", UploadedImages.MaximumBytes + 1, new MemoryStream(Png))).ErrorCode);
        // A stream longer than the length it claimed is still stopped at the limit.
        var huge = new byte[UploadedImages.MaximumBytes + 10];
        Png.CopyTo(huge, 0);
        Assert.Equal("IMAGE_TOO_LARGE", (await service.UploadAsync("m", "a.png", 100, new MemoryStream(huge))).ErrorCode);
        Assert.Empty(repo.Images);
    }

    [Fact]
    public async Task UnknownImageIsNotFound() =>
        Assert.Equal(404, (await new UploadedImageService(new Repo()).GetAsync(Guid.NewGuid())).StatusCode);

    [Theory]
    [InlineData("Máy chạy bộ (1).PNG", "image/png", "Máy-chạy-bộ--1.png")]
    [InlineData(@"C:\fakepath\..\whey.jpeg", "image/jpeg", "whey.jpg")]
    [InlineData("", "image/webp", "anh.webp")]
    public void DownloadNamesAreSafeAndUseTheRealType(string name, string type, string expected) =>
        Assert.Equal(expected, UploadedImageService.CleanFileName(name, type));

    [Theory]
    [InlineData("/api/images/0f8fad5bd9cb469fa165708f3a5e1c22", true)]
    [InlineData("https://cdn.example.com/whey.png", true)]
    [InlineData("/api/images/not-a-key", false)]
    [InlineData("/etc/passwd", false)]
    public void ProductImageAcceptsUploadedImagesAndImageLinks(string url, bool valid) =>
        Assert.Equal(valid, new SupplementProductRequestValidator()
            .Validate(new SupplementProductRequest("Whey", "Mô tả", url, 1_000_000)).IsValid);

    [Theory]
    [InlineData(null, true)]
    [InlineData("/api/images/0f8fad5bd9cb469fa165708f3a5e1c22", true)]
    [InlineData("https://example.com/treadmill.jpg", true)]
    [InlineData("ftp://example.com/x.jpg", false)]
    public void EquipmentImageIsOptional(string? url, bool valid) =>
        Assert.Equal(valid, new EquipmentRequestValidator()
            .Validate(new EquipmentRequest("Treadmill", 1, ImageUrl: url)).IsValid);
}

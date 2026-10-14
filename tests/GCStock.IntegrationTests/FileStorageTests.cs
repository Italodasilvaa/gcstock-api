using GCStock.Application.Storage;
using GCStock.Infra.Storage;
using Microsoft.Extensions.Options;

namespace GCStock.IntegrationTests;

public class FileStorageTests
{
    private static readonly Guid CompanyId = Guid.Parse("00000000-0000-0000-0000-00000000c001");

    [Theory]
    [InlineData("foto.JPG", "image/jpeg", 1024, ".jpg")]
    [InlineData("video.mp4", "video/mp4", 200L * 1024 * 1024, ".mp4")]
    [InlineData("nota.pdf", "application/pdf; charset=binary", 5000, ".pdf")]
    public void Accepts_Allowed_Files(string name, string type, long size, string expectedExt)
    {
        Assert.True(FileRules.TryValidate(name, type, size, out var ext, out _));
        Assert.Equal(expectedExt, ext);
    }

    [Theory]
    [InlineData("a.exe", "application/x-msdownload", 100)]
    [InlineData("a.png", "image/jpeg", 100)]
    [InlineData("a.jpg", "image/jpeg", 0)]
    [InlineData("a.jpg", "image/jpeg", 11L * 1024 * 1024)]
    [InlineData("a.mp4", "video/mp4", 501L * 1024 * 1024)]
    [InlineData("", "image/jpeg", 100)]
    public void Rejects_Invalid_Files(string name, string type, long size)
    {
        Assert.False(FileRules.TryValidate(name, type, size, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Key_Is_Scoped_By_Company_And_Sanitizes_Folder()
    {
        var key = FileRules.BuildKey(CompanyId, "produtos", ".jpg");
        Assert.StartsWith($"{CompanyId}/produtos/", key);
        Assert.EndsWith(".jpg", key);
        Assert.StartsWith($"{CompanyId}/geral/", FileRules.BuildKey(CompanyId, "../../etc", ".jpg"));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-00000000c001/produtos/a.jpg", true)]
    [InlineData("00000000-0000-0000-0000-00000000c002/produtos/a.jpg", false)]
    [InlineData("00000000-0000-0000-0000-00000000c001/../00000000-0000-0000-0000-00000000c002/a.jpg", false)]
    [InlineData("", false)]
    public void Delete_Only_Allowed_For_Own_Company(string key, bool expected)
    {
        Assert.Equal(expected, FileRules.BelongsToCompany(key, CompanyId));
    }

    [Fact]
    public void Unconfigured_Storage_Reports_Not_Configured()
    {
        var storage = new R2FileStorage(Options.Create(new R2Settings()));
        Assert.False(storage.IsConfigured);
        Assert.Throws<InvalidOperationException>(() =>
            storage.CreatePresignedUpload("k.jpg", "image/jpeg", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Presigned_Url_Targets_R2_Bucket_And_Public_Url()
    {
        var storage = new R2FileStorage(Options.Create(new R2Settings
        {
            AccountId = "abc123",
            AccessKeyId = "AKID",
            SecretAccessKey = "secret",
            Bucket = "gcstock-nprd",
            PublicBaseUrl = "https://cdn.exemplo.com/",
        }));

        var result = storage.CreatePresignedUpload("emp/produtos/x.jpg", "image/jpeg", TimeSpan.FromMinutes(15));

        Assert.StartsWith("https://abc123.r2.cloudflarestorage.com/gcstock-nprd/emp/produtos/x.jpg?", result.UploadUrl);
        Assert.Contains("X-Amz-Signature=", result.UploadUrl);
        Assert.Equal("https://cdn.exemplo.com/emp/produtos/x.jpg", result.Url);
    }
}

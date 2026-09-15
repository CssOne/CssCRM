namespace CssVision.Web.Services.Storage;

public sealed class S3StorageOptions
{
    public const string SectionName = "Storage:S3";

    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";

    /// <summary>Opcional — domínio do CloudFront na frente do bucket, se houver. Sem isso, usa a URL direta do S3.</summary>
    public string? PublicBaseUrl { get; set; }
}

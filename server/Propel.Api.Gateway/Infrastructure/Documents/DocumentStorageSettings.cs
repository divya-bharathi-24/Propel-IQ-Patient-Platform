namespace Propel.Api.Gateway.Infrastructure.Documents;

/// <summary>
/// Configuration settings for the local document storage service.
/// Bound from <c>DocumentStorage</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class DocumentStorageSettings
{
    /// <summary>
    /// Absolute or relative path to the directory where encrypted documents are stored.
    /// Defaults to <c>./document-storage</c> relative to the application root.
    /// </summary>
    public string StoragePath { get; set; } = "./document-storage";
}

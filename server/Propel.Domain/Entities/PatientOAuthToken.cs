namespace Propel.Domain.Entities;

/// <summary>
/// Encrypted OAuth 2.0 token storage for a patient's external calendar provider connection
/// (EP-007, us_035, AC-2, NFR-004).
/// Access and refresh tokens are stored encrypted via ASP.NET Core Data Protection (AES-256).
/// One record per (patientId, provider) pair — upserted on each successful OAuth callback.
/// </summary>
public sealed class PatientOAuthToken
{
    public Guid Id { get; set; }

    /// <summary>The patient who authorised the OAuth connection.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Calendar provider name (e.g., "Google").</summary>
    public string Provider { get; set; } = "";

    /// <summary>Data Protection–encrypted access token ciphertext.</summary>
    public string EncryptedAccessToken { get; set; } = "";

    /// <summary>Data Protection–encrypted refresh token ciphertext.</summary>
    public string EncryptedRefreshToken { get; set; } = "";

    /// <summary>UTC expiry of the access token so the service can refresh proactively.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public Patient Patient { get; set; } = null!;
}

namespace Lucid.Services.Trust.EndpointValidation;

/// <summary>
/// Result of a local-endpoint validation check.
/// Immutable — produced by <see cref="LocalEndpointValidator.Validate"/>.
/// </summary>
public sealed record EndpointValidationResult(
    bool                        IsAllowed,
    string                      OriginalUrl,
    EndpointTrustClassification Classification,
    string                      Reason)
{
    /// <summary>Creates an allowed result for a verified local endpoint.</summary>
    public static EndpointValidationResult Allow(
        string url, EndpointTrustClassification classification) =>
        new(IsAllowed: true, OriginalUrl: url, Classification: classification,
            Reason: "Endpoint is local — allowed.");

    /// <summary>Creates a denied result with an explanation the user can read.</summary>
    public static EndpointValidationResult Deny(
        string url, EndpointTrustClassification classification, string reason) =>
        new(IsAllowed: false, OriginalUrl: url, Classification: classification, Reason: reason);
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CandidatePortal.Api.Contracts;

public sealed class EmailTestRequest
{
    [Required, EmailAddress, MaxLength(255)]
    [JsonPropertyName("recipientAddress")]
    public string RecipientAddress { get; init; } = "";

    [MaxLength(200)]
    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [MaxLength(2000)]
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed record EmailTestResponse(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("recipientAddress")] string RecipientAddress,
    [property: JsonPropertyName("submittedAt")] DateTime SubmittedAt);

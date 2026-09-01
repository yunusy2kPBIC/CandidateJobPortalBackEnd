namespace CandidatePortal.Api.Infrastructure;

public class ApiException(int statusCode, string detail, Exception? innerException = null)
    : Exception(detail, innerException)
{
    public int StatusCode { get; } = statusCode;
    public string Detail { get; } = detail;
}

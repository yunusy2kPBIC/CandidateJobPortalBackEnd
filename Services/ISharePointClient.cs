using CandidatePortal.Api.Contracts;

namespace CandidatePortal.Api.Services;

public interface ISharePointClient
{
    object ConfigurationStatus();
    Task<object> DiagnosticsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharePointListResponse>> ListListsAsync(CancellationToken cancellationToken = default);
    Task<SharePointSetupResponse> ProvisionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharePointItemResponse>> ListItemsAsync(string listName, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse> GetItemAsync(string listName, int itemId, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse?> FindItemByFieldAsync(string listName, string fieldName, string value, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse> CreateItemAsync(string listName, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse> UpdateItemAsync(string listName, int itemId, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default);
    Task DeleteItemAsync(string listName, int itemId, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse> UploadResumeAsync(int candidateItemId, string candidateEmail, string filename, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<SharePointItemResponse> UploadCooperativeTrainingDocumentAsync(int requestItemId, string applicantEmail, string documentType, string filename, byte[] content, string contentType, CancellationToken cancellationToken = default);
    Task<int> DeleteCooperativeTrainingDocumentsAsync(int requestItemId, CancellationToken cancellationToken = default);
}

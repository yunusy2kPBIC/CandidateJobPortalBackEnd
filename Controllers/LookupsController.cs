using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CandidatePortal.Api.Controllers;

[Route("api/lookups")]
public sealed class LookupsController(MasterDataService masterData) : PortalControllerBase
{
    [AllowAnonymous, HttpGet]
    public Task<LookupOptionsResponse> Get(CancellationToken cancellationToken) =>
        masterData.GetOptionsAsync(cancellationToken);
}

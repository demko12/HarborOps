using HarborOps.Modules.Voyages.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Voyages.Presentation;

[ApiController]
[Route("api/voyages/master-data-summary")]
public sealed class MasterDataSummaryController(IGetMasterDataSummaryQuery getMasterDataSummaryQuery)
    : ControllerBase
{
    private const string ReadVoyagesPolicy = "voyages.read";

    [HttpGet]
    [Authorize(ReadVoyagesPolicy)]
    [ProducesResponseType<MasterDataSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MasterDataSummaryResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await getMasterDataSummaryQuery.GetAsync(cancellationToken));
    }
}

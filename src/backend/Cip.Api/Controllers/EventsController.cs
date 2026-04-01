using Cip.Application.Features.CipMvp;
using Cip.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

namespace Cip.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController(ICipMvpService cipMvpService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] IngestEventRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.IngestEventAsync(request, cancellationToken);
            if (response.Duplicate)
            {
                return Ok(response);
            }

            return Created($"/api/change-sets/{response.ChangeSetId}?tenantId={response.TenantId}", response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }
}

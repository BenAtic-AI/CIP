using Cip.Application.Features.CipMvp;
using Cip.Contracts.Triggers;
using Microsoft.AspNetCore.Mvc;

namespace Cip.Api.Controllers;

[ApiController]
[Route("api/triggers")]
public sealed class TriggersController(ICipMvpService cipMvpService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.ListTriggersAsync(tenantId, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TriggerDefinitionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.CreateTriggerAsync(request, cancellationToken);
            return Created($"/api/triggers/{response.TriggerId}?tenantId={response.TenantId}", response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpPost("{triggerId}/run")]
    public async Task<IActionResult> Run(string triggerId, [FromBody] RunTriggerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.RunTriggerAsync(request.TenantId, triggerId, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }
}

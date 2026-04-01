using Cip.Application.Features.CipMvp;
using Cip.Contracts.ChangeSets;
using Microsoft.AspNetCore.Mvc;

namespace Cip.Api.Controllers;

[ApiController]
[Route("api/change-sets")]
public sealed class ChangeSetsController(ICipMvpService cipMvpService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string tenantId, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.ListChangeSetsAsync(tenantId, status, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpGet("{changeSetId}")]
    public async Task<IActionResult> GetById(string changeSetId, [FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.GetChangeSetAsync(tenantId, changeSetId, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpPost("{changeSetId}/approve")]
    public async Task<IActionResult> Approve(string changeSetId, [FromBody] ReviewChangeSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.ApproveChangeSetAsync(request.TenantId, changeSetId, request.ReviewedBy, request.Comment, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpPost("{changeSetId}/reject")]
    public async Task<IActionResult> Reject(string changeSetId, [FromBody] ReviewChangeSetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.RejectChangeSetAsync(request.TenantId, changeSetId, request.ReviewedBy, request.Comment, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message });
        }
    }
}

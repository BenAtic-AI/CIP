using Cip.Application.Features.CipMvp;
using Microsoft.AspNetCore.Mvc;

namespace Cip.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public sealed class ProfilesController(ICipMvpService cipMvpService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.ListProfilesAsync(tenantId, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }

    [HttpGet("{profileId}")]
    public async Task<IActionResult> GetById(string profileId, [FromQuery] string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await cipMvpService.GetProfileAsync(tenantId, profileId, cancellationToken);
            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = exception.Message });
        }
    }
}

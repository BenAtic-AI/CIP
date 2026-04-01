using Cip.Application.Features.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cip.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public sealed class HealthController(IApplicationHealthService healthService, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
        => Ok(healthService.GetStatus(environment.EnvironmentName));
}

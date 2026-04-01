using Cip.Api.Authentication;
using Cip.Api.Configuration;
using Cip.Application.Features.Health;
using Cip.Contracts.Constants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCipApplication();
builder.Services.AddCipInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEntraAuthentication(builder.Configuration);

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [Constants.Cors.DefaultFrontendOrigin];

builder.Services.AddCors(options =>
{
    options.AddPolicy(Constants.Cors.LocalDevelopmentPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
var authEnabled = app.Configuration.GetValue<bool>($"{Constants.Entra.SectionName}:Enabled");

if (authEnabled)
{
    var entraOptions = app.Configuration.GetSection(Constants.Entra.SectionName).Get<EntraOptions>() ?? new EntraOptions();
    if (string.IsNullOrWhiteSpace(entraOptions.Authority) || string.IsNullOrWhiteSpace(entraOptions.Audience))
    {
        throw new InvalidOperationException($"{Constants.Entra.SectionName} must include Authority and Audience when authentication is enabled.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(Constants.Cors.LocalDevelopmentPolicy);

if (authEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var controllers = app.MapControllers();

if (authEnabled)
{
    controllers.RequireAuthorization();
}

app.MapGet("/", () => Results.Redirect("/api/health"));

app.Run();

public partial class Program;

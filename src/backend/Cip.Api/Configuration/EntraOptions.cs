namespace Cip.Api.Configuration;

public sealed class EntraOptions
{
    public bool Enabled { get; set; }

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;
}

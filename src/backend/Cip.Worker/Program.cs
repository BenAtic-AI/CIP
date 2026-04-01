using Cip.Worker.Services;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddCipApplication();
builder.Services.AddCipInfrastructure(builder.Configuration);
builder.Services.AddHostedService<BootstrapWorker>();

var host = builder.Build();
host.Run();

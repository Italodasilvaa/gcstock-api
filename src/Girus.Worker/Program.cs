using Girus.Infra.DependencyInjection;
using Girus.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfra(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

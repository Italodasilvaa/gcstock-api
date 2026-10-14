using GCStock.Infra.DependencyInjection;
using GCStock.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Docker/Swarm secrets montados em /run/secrets (um arquivo por chave, ex.: ConnectionStrings__DefaultConnection).
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddInfra(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

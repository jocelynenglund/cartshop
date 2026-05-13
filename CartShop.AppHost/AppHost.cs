var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("cartshop-pgdata")
    .WithPgAdmin();

var cartdb = postgres.AddDatabase("cartdb");

var api = builder.AddProject<Projects.CartShop_ApiService>("api")
    .WithReference(cartdb)
    .WaitFor(cartdb);

builder.AddNpmApp("web", "../cart-shop-web", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("BROWSER", "none")
    .WithHttpEndpoint(env: "PORT", port: 4200)
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();

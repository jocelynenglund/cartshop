var builder = DistributedApplication.CreateBuilder(args);

var pgUser = builder.AddParameter("pg-user", "postgres");
var pgPassword = builder.AddParameter("pg-password", "cartshop-dev", secret: true);

// The password is baked into the data volume the first time Postgres
// initializes it; POSTGRES_PASSWORD is ignored once the data dir exists.
// So pg-password MUST stay a stable, explicit value (above) — never a
// generated/rotating one — while the volume persists. If you ever change
// pg-password, recreate the volume or auth will fail on the next run:
//   podman volume rm cartshop-pgdata   (stop the AppHost first)
var postgres = builder.AddPostgres("postgres", userName: pgUser, password: pgPassword, port: 5432)
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

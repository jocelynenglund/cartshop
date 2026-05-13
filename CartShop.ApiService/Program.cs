using CartShop.Core;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("CartShopCors", p => p
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// Marten + Wolverine + slice discovery. "cartdb" is the Aspire-resolved connection.
builder.AddCartShopCore("cartdb");

builder.Services.AddWolverineHttp();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("CartShopCors");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

// Wolverine.Http discovers [WolverinePost/Get/Delete] across the loaded assemblies
// and registers them as minimal-API endpoints.
app.MapWolverineEndpoints();

app.MapGet("/", () => Results.Redirect("/openapi/v1.json"));

app.Run();

public partial class Program;

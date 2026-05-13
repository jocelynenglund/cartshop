using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Marten;

namespace CartShop.Core;

public static class Initialization
{
    public static IHostApplicationBuilder AddCartShopCore(this IHostApplicationBuilder builder, string postgresConnectionName)
    {
        // Marten configuration sourced from the named Aspire Postgres connection.
        builder.Services
            .AddMarten(sp =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                var connStr = cfg.GetConnectionString(postgresConnectionName)
                    ?? throw new InvalidOperationException($"Missing connection string '{postgresConnectionName}'");

                var opts = new StoreOptions();
                opts.Connection(connStr);
                opts.DatabaseSchemaName = "cartshop";

                // Quick append mode uses the mt_quick_append_events SQL function
                // which writes events AND their inferred tags in a single call.
                // The default Rich mode emits separate INSERT statements that
                // get reordered relative to tag inserts, leaving tags empty.
                opts.Events.AppendMode = EventAppendMode.Quick;

                // Self-aggregating snapshot of CartAggregate kept up-to-date inline.
                opts.Projections.Snapshot<CartAggregate>(SnapshotLifecycle.Inline);

                // Register Sku as a Marten DCB tag type. Any event with a Sku
                // property is then automatically tagged and participates in
                // cross-stream consistency queries (see AddItem slice).
                opts.Events.RegisterTagType<Sku>();

                return opts;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine()
            .ApplyAllDatabaseChangesOnStartup();

        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(Initialization).Assembly);
        });

        return builder;
    }
}

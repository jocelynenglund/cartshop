using CartShop.Core.Domain;
using CartShop.Events;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
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

                // Inline: snapshot of CartAggregate updated in the same
                // transaction as the events. Read-after-write consistency for
                // GetCart / ListSubmittedCarts.
                opts.Projections.Snapshot<CartAggregate>(SnapshotLifecycle.Inline);

                // Inline (custom): one-open-cart-per-customer lookup. Must be
                // consistent with the previous write so CreateCart can reject
                // a duplicate customer in the same request that follows.
                opts.Projections.Add<OpenCartByCustomerProjection>(ProjectionLifecycle.Inline);

                // Index the customer-name lookup column so the uniqueness query
                // is fast (CreateCart hits it on the write path).
                opts.Schema.For<OpenCartByCustomer>()
                    .Index(x => x.CustomerNameNormalized);

                // Async: SalesByDay rollup runs in the background daemon.
                // Tolerates lag; aggregates across every cart stream.
                opts.Projections.Add<SalesByDayProjection>(ProjectionLifecycle.Async);

                // (CartTimeline is a *live* projection — built on demand in
                // the query handler from FetchStreamAsync; nothing to register.)

                // Register Sku and CouponCode as Marten DCB tag types. Any event
                // with one of these properties is then automatically tagged and
                // participates in cross-stream consistency queries (see AddItem
                // and ApplyCoupon slices).
                opts.Events.RegisterTagType<Sku>();
                opts.Events.RegisterTagType<CouponCode>();

                return opts;
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine()
            .AddAsyncDaemon(DaemonMode.Solo)
            .ApplyAllDatabaseChangesOnStartup();

        builder.UseWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(Initialization).Assembly);
        });

        return builder;
    }
}

using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Application.Bookings.Validation;
using Booking.Service.Application.Contracts.Persistence;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Application.Pricing;
using Booking.Service.Infrastructure.Concurrency;
using Booking.Service.Infrastructure.Configuration;
using Booking.Service.Infrastructure.Expiry;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Pricing;
using Booking.Service.Infrastructure.Saga;
using Booking.Service.Infrastructure.Validation;
using BuildingBlocks.Abstractions.Messaging;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace Booking.Service.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BookingDb")
            ?? throw new InvalidOperationException("Connection string 'BookingDb' was not found.");
        var messagingOptions = configuration.GetSection(BookingMessagingOptions.SectionName).Get<BookingMessagingOptions>()
            ?? new BookingMessagingOptions();

        services.Configure<BookingMessagingOptions>(configuration.GetSection(BookingMessagingOptions.SectionName));
        services.Configure<BookingSagaPaymentOptions>(configuration.GetSection(BookingSagaPaymentOptions.SectionName));
        services.Configure<BookingAnalyticsKafkaOptions>(configuration.GetSection("BookingAnalyticsKafka"));
        services.Configure<BookingPricingOptions>(configuration.GetSection(BookingPricingOptions.SectionName));
        services.Configure<BookingExpiryOptions>(configuration.GetSection(BookingExpiryOptions.SectionName));
        services.Configure<SeatReservationLockOptions>(configuration.GetSection(SeatReservationLockOptions.SectionName));
        services.Configure<BookingSagaTimeoutOptions>(configuration.GetSection(BookingSagaTimeoutOptions.SectionName));
        services.Configure<BookingHealthEmitterOptions>(configuration.GetSection(BookingHealthEmitterOptions.SectionName));

        // Strategy pattern: each pricing rule is its own implementation; the resolver applies them in priority order.
        services.AddSingleton<IPricingStrategy, EarlyBirdPricingStrategy>();
        services.AddSingleton<IPricingStrategy, GroupDiscountPricingStrategy>();
        services.AddSingleton<IPricingStrategy, VipPricingStrategy>();
        services.AddSingleton<IPricingStrategyResolver, PricingStrategyResolver>();

        // Chain of Responsibility for booking validation.
        services.Configure<BookingCustomerBlocklistOptions>(configuration.GetSection(BookingCustomerBlocklistOptions.SectionName));
        services.AddSingleton<ICustomerBlocklist, InMemoryCustomerBlocklist>();
        services.AddScoped<EventPublishedHandler>();
        services.AddScoped<SeatExistsHandler>();
        services.AddScoped<SeatAvailableHandler>();
        services.AddScoped<CustomerNotBlockedHandler>();
        services.AddScoped<IBookingValidationPipeline, BookingValidationPipeline>();

        services.AddDbContext<Persistence.BookingDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        services.AddScoped<DomainEventCollector>();
        services.AddScoped<IBookingRepository, Persistence.EfBookingRepository>();
        services.AddScoped<IEventRepository, Persistence.EfEventRepository>();
        services.AddScoped<IBookingUnitOfWork, Persistence.EfUnitOfWork>();
        services.AddScoped<ITransactionExecutor, Persistence.EfTransactionExecutor>();
        services.AddScoped<IBookingSagaStateReader, Persistence.EfBookingSagaStateReader>();
        services.AddScoped<IInboxMessageStore, BookingInboxMessageStore>();
        services.AddSingleton<IdempotentConsumerExecutor>();

        if (configuration.GetSection("BookingAnalyticsKafka").Exists())
        {
            services.AddSingleton<IBookingAnalyticsEventProducer, KafkaBookingAnalyticsEventProducer>();
        }
        else
        {
            services.AddSingleton<IBookingAnalyticsEventProducer, NoOpBookingAnalyticsEventProducer>();
        }

        if (configuration.GetSection(BookingMessagingOptions.SectionName).Exists())
        {
            services.AddMassTransit(configurator =>
            {
                configurator.AddConsumer<ConfirmBookingRequestedConsumer>();
                configurator.AddConsumer<CancelBookingRequestedConsumer>();

                configurator.AddSagaStateMachine<BookingPaymentSagaStateMachine, Persistence.Models.BookingPaymentSagaState>()
                    .EntityFrameworkRepository(repository =>
                    {
                        repository.ExistingDbContext<Persistence.BookingDbContext>();
                        repository.LockStatementProvider = new PostgresLockStatementProvider();
                    });

                configurator.UsingRabbitMq((context, cfg) =>
                {
                    var options = context.GetRequiredService<Microsoft.Extensions.Options.IOptions<BookingMessagingOptions>>().Value;
                    cfg.Host(new Uri($"rabbitmq://{options.HostName}:{options.Port}"), host =>
                    {
                        host.Username(options.UserName);
                        host.Password(options.Password);
                    });

                    // F5 — bounded retry + DLQ for poison saga messages. A malformed
                    // RefundCompletedV1 (e.g. shape drift from a misaligned Payment.Service
                    // deploy) would otherwise spin forever on the saga endpoint and block all
                    // newer messages behind it. After UseMessageRetry exhausts, MassTransit
                    // moves the message to the <queue>_error exchange — that IS the DLQ here,
                    // operationally inspectable through the RabbitMQ management UI.
                    cfg.UseMessageRetry(retry => retry.Exponential(
                        retryLimit: 3,
                        minInterval: TimeSpan.FromSeconds(1),
                        maxInterval: TimeSpan.FromSeconds(10),
                        intervalDelta: TimeSpan.FromSeconds(2)));

                    cfg.ConfigureEndpoints(context);
                });
            });

            services.AddHostedService<BookingOutboxPublisher>();
        }

        AddBookingExpiry(services, configuration);
        AddSeatReservationLock(services, configuration);
        AddBookingSagaTimeout(services, configuration);
        AddBookingHealthEmitter(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the F7 observability emitters — the periodic background services that publish
    /// structured saga-progress / outbox-lag log events for the Kibana dashboards. Gated at
    /// runtime by the option's <c>Enabled</c> flag so test fixtures can switch them off.
    /// </summary>
    private static void AddBookingHealthEmitter(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Observability.BookingHealthEmitterService>();
        services.AddHostedService<Observability.BookingHealthEmitterWorker>();
    }

    /// <summary>
    /// Wires the F5 saga-timeout pipeline: per-tick scoped service + background worker that drives
    /// it. Both registrations are gated on the option's <c>Enabled</c> flag at runtime, so test
    /// fixtures can flip the worker off without disturbing this composition root.
    /// </summary>
    private static void AddBookingSagaTimeout(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BookingSagaTimeoutService>();
        services.AddHostedService<BookingSagaTimeoutWorker>();
    }

    private static void AddBookingExpiry(IServiceCollection services, IConfiguration configuration)
    {
        var expiryOptions = configuration.GetSection(BookingExpiryOptions.SectionName).Get<BookingExpiryOptions>()
            ?? new BookingExpiryOptions();

        services.AddScoped<BookingExpiryService>();

        var redisConnection = expiryOptions.Lock.RedisConnection;
        if (IsRedisConfigured(redisConnection))
        {
            RegisterSharedRedisMultiplexer(services, redisConnection!);
            services.AddSingleton<IBookingExpiryLock, RedisBookingExpiryLock>();
        }
        else
        {
            // Single-instance dev fallback. Documented in BookingExpiryLockOptions.
            services.AddSingleton<IBookingExpiryLock, InProcessBookingExpiryLock>();
        }

        if (expiryOptions.Enabled)
        {
            services.AddHostedService<BookingExpiryWorker>();
        }
    }

    private static void AddSeatReservationLock(IServiceCollection services, IConfiguration configuration)
    {
        var lockOptions = configuration.GetSection(SeatReservationLockOptions.SectionName).Get<SeatReservationLockOptions>()
            ?? new SeatReservationLockOptions();

        if (IsRedisConfigured(lockOptions.RedisConnection))
        {
            RegisterSharedRedisMultiplexer(services, lockOptions.RedisConnection!);

            // RedLockNet's IDistributedLockFactory is the entry-point used by RedLockSeatReservationLock.
            // The factory takes its own list of multiplexers, so we adapt our single shared
            // IConnectionMultiplexer at registration time.
            services.AddSingleton<IDistributedLockFactory>(serviceProvider =>
            {
                var connection = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var multiplexers = new List<RedLockMultiplexer> { new(connection) };
                return RedLockFactory.Create(multiplexers, loggerFactory);
            });

            services.AddSingleton<ISeatReservationLock, RedLockSeatReservationLock>();
        }
        else
        {
            // Single-instance dev fallback. Documented in SeatReservationLockOptions.
            services.AddSingleton<ISeatReservationLock, InProcessSeatReservationLock>();
        }
    }

    private static bool IsRedisConfigured(string? redisConnection) =>
        !string.IsNullOrWhiteSpace(redisConnection)
        && !redisConnection.Equals("none", StringComparison.OrdinalIgnoreCase)
        && !redisConnection.Equals("in-process", StringComparison.OrdinalIgnoreCase);

    private static void RegisterSharedRedisMultiplexer(IServiceCollection services, string connectionString)
    {
        // TryAdd so the second consumer (seat lock or expiry lock) shares whichever multiplexer was
        // registered first instead of opening a second connection to the same Redis instance.
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
    }
}

using Booking.Service.Application.Bookings.Concurrency;
using Booking.Service.Application.Contracts.IntegrationEvents;
using Booking.Service.Infrastructure.Concurrency;
using Booking.Service.Infrastructure.Expiry;
using Booking.Service.Infrastructure.Messaging;
using Booking.Service.Infrastructure.Persistence;
using DotNet.Testcontainers.Builders;
using RedLockNet;
using StackExchange.Redis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Booking.Service.IntegrationTests;

public sealed class BookingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string GatewaySharedSecret = "INSERT_DEVELOPMENT_SECRET_HERE_CHANGE_ME";
    private PostgreSqlContainer? _postgresContainer;
    private RabbitMqContainer? _rabbitMqContainer;
    private string? _previousGatewaySharedSecret;

    public bool IsDockerAvailable { get; private set; } = true;

    public string RabbitMqHostName => _rabbitMqContainer?.Hostname ?? "localhost";

    public int RabbitMqPort => _rabbitMqContainer?.GetMappedPublicPort(5672) ?? 5672;

    public async Task InitializeAsync()
    {
        _previousGatewaySharedSecret = Environment.GetEnvironmentVariable("GatewayAuth__SharedSecret");
        Environment.SetEnvironmentVariable("GatewayAuth__SharedSecret", GatewaySharedSecret);

        try
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithDatabase("booking_service_tests")
                .WithUsername("booking_user")
                .WithPassword("booking_pass")
                .Build();

            _rabbitMqContainer = new RabbitMqBuilder()
                .WithImage("rabbitmq:3-management")
                .WithUsername("guest")
                .WithPassword("guest")
                .Build();

            await _postgresContainer.StartAsync();
            await _rabbitMqContainer.StartAsync();

            Environment.SetEnvironmentVariable("BookingMessaging__HostName", RabbitMqHostName);
            Environment.SetEnvironmentVariable("BookingMessaging__Port", RabbitMqPort.ToString());
            Environment.SetEnvironmentVariable("ConnectionStrings__BookingDb", _postgresContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("SeatReservationLock__RedisConnection", "none");
            Environment.SetEnvironmentVariable("BookingExpiry__Lock__RedisConnection", "none");
        }
        catch
        {
            IsDockerAvailable = false;
        }
    }

    public new async Task DisposeAsync()
    {
        try
        {
            if (_postgresContainer is not null)
            {
                await _postgresContainer.StopAsync();
                await _postgresContainer.DisposeAsync();
            }

            if (_rabbitMqContainer is not null)
            {
                await _rabbitMqContainer.StopAsync();
                await _rabbitMqContainer.DisposeAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GatewayAuth__SharedSecret", _previousGatewaySharedSecret);
            Environment.SetEnvironmentVariable("BookingMessaging__HostName", null);
            Environment.SetEnvironmentVariable("BookingMessaging__Port", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__BookingDb", null);
            Environment.SetEnvironmentVariable("SeatReservationLock__RedisConnection", null);
            Environment.SetEnvironmentVariable("BookingExpiry__Lock__RedisConnection", null);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GatewayAuth:SharedSecret"] = GatewaySharedSecret,
                ["BookingMessaging:HostName"] = RabbitMqHostName,
                ["BookingMessaging:Port"] = RabbitMqPort.ToString(),
                ["BookingMessaging:UserName"] = "guest",
                ["BookingMessaging:Password"] = "guest",
                ["BookingMessaging:PublisherEnabled"] = "true",
                ["BookingSagaPayment:DefaultAmount"] = "100",
                ["BookingSagaPayment:DefaultCurrency"] = "USD",
                ["BookingSagaPayment:DefaultProvider"] = "stripe",
                ["BookingSagaPayment:DefaultMetadata:cardNumber"] = "4242 4242 4242 4242",
                // The expiry worker is driven explicitly by tests; the hosted-loop must not race them.
                ["BookingExpiry:Enabled"] = "false",
                ["BookingExpiry:ExpiryAfterMinutes"] = "10",
                ["BookingExpiry:BatchSize"] = "200",
                // F5 — saga timeout worker disabled by default in tests so the polling loop does
                // not race deterministic assertions. Tests that want to exercise the timeout path
                // resolve BookingSagaTimeoutService from DI and drive RunOnceAsync directly.
                ["BookingSagaTimeout:Enabled"] = "false",
                ["BookingSagaTimeout:RefundTimeoutSeconds"] = "1",
                ["BookingSagaTimeout:ScanIntervalSeconds"] = "1",
                // F7 — health emitter is just observability scaffolding. Disabled in tests so the
                // periodic DB queries do not race deterministic assertions; tests that want to
                // exercise the emitter resolve the scoped service and call EmitOnceAsync directly.
                ["BookingHealthEmitter:Enabled"] = "false",
                // In-process lock fallbacks so integration tests do not depend on external Redis
                ["SeatReservationLock:RedisConnection"] = "",
                ["BookingExpiry:Lock:RedisConnection"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            if (!IsDockerAvailable || _postgresContainer is null)
            {
                return;
            }

            services.RemoveAll<BookingDbContext>();
            services.RemoveAll<DbContextOptions<BookingDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IBookingAnalyticsEventProducer>();
            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<IDistributedLockFactory>();
            services.RemoveAll<ISeatReservationLock>();
            services.RemoveAll<IBookingExpiryLock>();

            services.AddSingleton<ISeatReservationLock, InProcessSeatReservationLock>();
            services.AddSingleton<IBookingExpiryLock, InProcessBookingExpiryLock>();

            var bookingConnectionString = _postgresContainer.GetConnectionString();

            services.AddDbContext<BookingDbContext>(options =>
            {
                options.UseNpgsql(bookingConnectionString);
            });
            services.AddSingleton<IBookingAnalyticsEventProducer, NoOpBookingAnalyticsEventProducer>();
            services.RemoveAll<Microsoft.Extensions.Options.IOptions<Booking.Service.Infrastructure.Configuration.BookingMessagingOptions>>();
            services.AddSingleton<Microsoft.Extensions.Options.IOptions<Booking.Service.Infrastructure.Configuration.BookingMessagingOptions>>(
                Microsoft.Extensions.Options.Options.Create(new Booking.Service.Infrastructure.Configuration.BookingMessagingOptions
                {
                    HostName = RabbitMqHostName,
                    Port = RabbitMqPort,
                    UserName = "guest",
                    Password = "guest",
                    PublisherEnabled = true
                }));

            using var scope = services.BuildServiceProvider().CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            dbContext.Database.Migrate();
            DevelopmentDataSeeder.SeedAsync(dbContext).GetAwaiter().GetResult();
        });
    }
}

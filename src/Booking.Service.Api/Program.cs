using Booking.Service.Api.Filters;
using Booking.Service.Api.Middleware;
using Booking.Service.Api.Services;
using Booking.Service.Application;
using Booking.Service.Application.Contracts.Persistence;
using BuildingBlocks.Grpc.Catalog;
using Booking.Service.Infrastructure;
using Booking.Service.Infrastructure.Persistence;
using BuildingBlocks.Abstractions;
using BuildingBlocks.Observability;
using BuildingBlocks.Security;
using BuildingBlocks.Time;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddEventHubSerilog();
builder.AddEventHubTracing();
builder.Services.AddCorrelationContext();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEventHubClock();
builder.Services.AddHttpContextAccessor();

builder.Services.AddGatewayForwardedAuthentication(builder.Configuration);
builder.Services.AddEventHubAuthorization();
// Plug in the in-process EF resolver for the BookingOwnerOrAdmin policy. Lets the auth handler
// load the booking via the same IBookingRepository the rest of the service uses, so a stale
// auth decision and a stale handler decision can never diverge.
builder.Services.AddBookingOwnerAuthorization<Booking.Service.Infrastructure.Security.BookingOwnershipResolver>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditActionFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var catalogGrpcAddress = builder.Configuration["CatalogGrpc:BaseAddress"] ?? "http://localhost:5002";
builder.Services
    .AddGrpcClient<CatalogGrpc.CatalogGrpcClient>(options =>
    {
        options.Address = new Uri(catalogGrpcAddress);
    });
builder.Services.AddScoped<ICatalogEventDetailsClient, CatalogEventDetailsClient>();

builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<IBookingUnitOfWork>());

var bookingMessagingSection = builder.Configuration.GetSection("BookingMessaging");
var bookingMessagingHost = bookingMessagingSection["HostName"] ?? "localhost";
var bookingMessagingPort = bookingMessagingSection["Port"] ?? "5672";
var bookingMessagingUser = bookingMessagingSection["UserName"] ?? "guest";
var bookingMessagingPass = bookingMessagingSection["Password"] ?? "guest";

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("BookingDb")
        ?? throw new InvalidOperationException("Connection string 'BookingDb' is required."),
        tags: new[] { "ready" })
    .AddRabbitMQ(new Uri($"amqp://{bookingMessagingUser}:{bookingMessagingPass}@{bookingMessagingHost}:{bookingMessagingPort}"),
        name: "rabbitmq", tags: new[] { "ready" })
    .AddKafka(config =>
    {
        config.BootstrapServers = builder.Configuration["BookingAnalyticsKafka:BootstrapServers"] ?? "localhost:9092";
    }, name: "kafka", tags: new[] { "ready" });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

    await dbContext.Database.MigrateAsync();
    await DevelopmentDataSeeder.SeedAsync(dbContext);

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCorrelationContext();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();

public partial class Program;

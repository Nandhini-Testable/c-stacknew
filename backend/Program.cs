using Microsoft.EntityFrameworkCore;
using NineBlock.Api.Data;
using NineBlock.Api.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("NineBlock.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://127.0.0.1:4317"))
        .AddJaegerExporter(o =>
        {
            o.AgentHost = builder.Configuration["OpenTelemetry:JaegerHost"] ?? "127.0.0.1";
            o.AgentPort = int.Parse(builder.Configuration["OpenTelemetry:JaegerPort"] ?? "6831");
        })
        .AddZipkinExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:ZipkinEndpoint"] ?? "http://127.0.0.1:9411/api/v2/spans")));

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// SQL Server Database Provider with InMemory fallback for isolated testing
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<NineBlockDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString) && !builder.Environment.IsEnvironment("Testing"))
    {
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        });
    }
    else
    {
        options.UseInMemoryDatabase("NineBlockDb");
    }
});

// Register Services
builder.Services.AddScoped<NineBoxMatrixService>();
builder.Services.AddScoped<EmployeeEvaluationService>();

// CORS for React frontend (dev server on port 3000)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Ensure Database schema exists & seed initial reference data
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NineBlockDbContext>();
    context.Database.EnsureCreated();
}

app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapControllers();

app.Run();

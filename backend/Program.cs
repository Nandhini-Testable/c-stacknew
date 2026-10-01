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

// Database Configuration: Enterprise SQL Server provider with InMemory fallback for local/test runs
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString) && !builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<NineBlockDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}
else
{
    builder.Services.AddDbContext<NineBlockDbContext>(options =>
        options.UseInMemoryDatabase("NineBlockDb"));
}

// Register Domain & Evaluation Services (Clean Single-Responsibility, 0% Duplication)
builder.Services.AddScoped<NineBoxMatrixService>();
builder.Services.AddScoped<EmployeeEvaluationService>();

// CORS configuration for frontend clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Seed initial in-memory database if applicable
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NineBlockDbContext>();
    context.Database.EnsureCreated();
}

app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapControllers();

app.Run();

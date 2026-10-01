using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ScholarshipCMGroups.Data;
using ScholarshipCMGroups.Helpers;
using ScholarshipCMGroups.Middleware;
using ScholarshipCMGroups.Repositories;
using ScholarshipCMGroups.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ScholarshipCMGroups"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://127.0.0.1:4317"))
        .AddJaegerExporter(o =>
        {
            o.AgentHost = builder.Configuration["OpenTelemetry:JaegerHost"] ?? "127.0.0.1";
            o.AgentPort = int.Parse(builder.Configuration["OpenTelemetry:JaegerPort"] ?? "6831");
        })
        .AddZipkinExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:ZipkinEndpoint"] ?? "http://127.0.0.1:9411/api/v2/spans")));

builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<StudentRepository>();
builder.Services.AddScoped<ScholarshipRepository>();
builder.Services.AddScoped<ApplicationRepository>();
builder.Services.AddScoped<DocumentRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<ScholarshipService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<SecurityTestSamples>();
builder.Services.AddSingleton<SessionStore>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("Frontend");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();
app.Run();

public partial class Program
{
}

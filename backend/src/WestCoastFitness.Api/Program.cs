using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using WestCoastFitness.Api;
using WestCoastFitness.Application;
using WestCoastFitness.Infrastructure;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("WestCoastFitness.Api"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://127.0.0.1:4317"))
        .AddJaegerExporter(o =>
        {
            o.AgentHost = builder.Configuration["OpenTelemetry:JaegerHost"] ?? "127.0.0.1";
            o.AgentPort = int.Parse(builder.Configuration["OpenTelemetry:JaegerPort"] ?? "6831");
        })
        .AddZipkinExporter(o => o.Endpoint = new Uri(builder.Configuration["OpenTelemetry:ZipkinEndpoint"] ?? "http://127.0.0.1:9411/api/v2/spans")));
builder.Services.AddScoped<MemberAccountService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<ClassBookingService>();
builder.Services.AddScoped<AssessmentService>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
builder.Services.AddInfrastructure(builder.Configuration);

var signingKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;
if (signingKey.Length < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ClubExceptionMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

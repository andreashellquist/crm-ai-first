using System.Text;
using CrmApi.Data;
using CrmApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<JobQueueService>();
builder.Services.AddScoped<DealScoringService>();
builder.Services.AddSingleton<IAnthropicMessagesClient, AnthropicMessagesClient>();
builder.Services.AddHostedService<JobWorker>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret not configured");

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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (args.Contains("seed"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await Seed.RunAsync(db);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in CrmApi.Tests.
public partial class Program { }

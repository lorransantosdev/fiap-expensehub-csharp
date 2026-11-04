using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api.Auth;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Errors;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Reports;
using ExpenseHub.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseHub.Api;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");

        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services
            .AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);
        builder.Services.AddAuthorization();
        builder.Services
            .AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection(AdminSeedOptions.SectionName));

        builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ExpenseHubExceptionHandler>();
        builder.Services.AddOpenApi();

        builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
        builder.Services.AddScoped<IExpenseService, ExpenseService>();
        builder.Services.AddScoped<IUserService, UserService>();

        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.AddSingleton<IReportPublisher, RabbitMqReportPublisher>();
        builder.Services.AddScoped<IReportRepository, ReportRepository>();
        builder.Services.AddScoped<IReportService, ReportService>();

        string? keysPath = builder.Configuration["DataProtection:KeysPath"];

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            builder.Services.AddDataProtection()
                .SetApplicationName("ExpenseHub")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        WebApplication app = builder.Build();

        await DatabaseInitializer.InitializeAsync(app.Services, CancellationToken.None);
        await IdentitySeeder.SeedAsync(app.Services, CancellationToken.None);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseAuthentication();
        app.UseMiddleware<SecurityStampMiddleware>();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithName("GetHealth");
        app.MapControllers();

        await app.RunAsync();
    }
}

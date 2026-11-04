using System;
using System.Threading;
using System.Threading.Tasks;
using ExpenseHub.Api;
using ExpenseHub.Api.Data;
using ExpenseHub.Api.Messaging;
using ExpenseHub.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExpenseHub.Worker;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        string connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' não configurada.");

        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.AddScoped<IReportRepository, ReportRepository>();
        builder.Services.AddScoped<ReportProcessor>();
        builder.Services.AddHostedService<ReportConsumerService>();

        IHost host = builder.Build();

        string version = builder.Configuration["EXPENSEHUB_GIT_SHA"] ?? "local";
        ILogger startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ExpenseHub.Worker");
        StartupLog.Started(startupLogger, "worker", version);

        // A web também migra; o EF Core usa trava de migração, então as duas inicializações são seguras.
        await DatabaseInitializer.InitializeAsync(host.Services, CancellationToken.None);

        await host.RunAsync();
    }
}

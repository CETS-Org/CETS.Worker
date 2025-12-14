using Application.Implementations;
using Application.Implementations.COM;
using Application.Interfaces;
using Application.Interfaces.COM;
using Application.Interfaces.Common.Email;
using CETS.Worker.Services.Implementations;
using CETS.Worker.Services.Interfaces;
using CETS.Worker.Workers;
using Domain.Data;
using Domain.Interfaces;
using Domain.Interfaces.ACAD;
using Domain.Interfaces.COM;
using Domain.Interfaces.CORE;
using Domain.Interfaces.IDN;
using Infrastructure.Implementations.Repositories;
using Infrastructure.Implementations.Repositories.ACAD;
using Infrastructure.Implementations.Repositories.CORE;
using Infrastructure.Implementations.Repositories.IDN;
using Infrastructure.Implementations.Common.Mongo;
using Infrastructure.Implementations.Repositories.COM;
using Infrastructure.Implementations.Common.Notifications;
using Infrastructure.Implementations.Common.Email;
using Infrastructure.Implementations.Common.Email.EmailTemplates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using StackExchange.Redis; // Required for Redis Connection
using System;

namespace CETS.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 1. Immediate Output: Proves the process actually started in Azure Docker Logs
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"[SYSTEM BOOT] Application starting at {DateTime.UtcNow} UTC");
            Console.WriteLine("--------------------------------------------------");

            try
            {
                var builder = Host.CreateApplicationBuilder(args);

                // 2. Configure Logging for Azure Linux (File System / Console)
                builder.Logging.ClearProviders();
                builder.Logging.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    options.SingleLine = true;
                });
                builder.Logging.AddDebug();
                // Force Information level to ensure logs are not hidden
                builder.Logging.SetMinimumLevel(LogLevel.Information);

                // --- BACKGROUND WORKERS ---
                builder.Services.AddHostedService<Worker>(); // Default test worker
                builder.Services.AddHostedService<AcademicRequestExpiryWorker>();
                builder.Services.AddHostedService<PaymentReminderWorker>();
                builder.Services.AddHostedService<DropoutProcessingWorker>();

                // Suspension & Status Workers
                builder.Services.AddHostedService<ApplySuspensionWorker>();
                builder.Services.AddHostedService<EndSuspensionWorker>();
                builder.Services.AddHostedService<ReturnReminderWorker>();
                builder.Services.AddHostedService<AutoDropoutWorker>();
                builder.Services.AddHostedService<CourseStartReminderWorker>();
                builder.Services.AddHostedService<AttendanceWarningWorker>();
                // builder.Services.AddHostedService<RoomStatusUpdaterWorker>(); // Uncomment if needed

                // --- CORE SERVICES (Scoped) ---
                builder.Services.AddMemoryCache();

                // Note: Workers are Singletons. They must create a Scope to use these services.
                builder.Services.AddScoped<IMessageService, MessageService>();
                builder.Services.AddScoped<IPaymentReminderService, PaymentReminderService>();
                builder.Services.AddScoped<IDropoutProcessingService, DropoutProcessingService>();
                builder.Services.AddScoped<ISuspensionProcessingService, SuspensionProcessingService>();
                builder.Services.AddScoped<ICurrentUserService, WorkerCurrentUserService>();
                builder.Services.AddScoped<IAttendanceWarningService, AttendanceWarningService>();
                builder.Services.AddScoped<ICourseProcessingService, CourseProcessingService>();

                // Email Services
                builder.Services.AddScoped<IMailService, MailService>();
                builder.Services.AddScoped<IEmailTemplateBuilder, EmailTemplateBuilder>();

                // --- MONGODB CONFIGURATION ---
                builder.Services.Configure<MongoNotificationOptions>(
                    builder.Configuration.GetSection(MongoNotificationOptions.SectionName));

                builder.Services.PostConfigure<MongoNotificationOptions>(options =>
                {
                    if (string.IsNullOrWhiteSpace(options.Database))
                    {
                        var databaseName = builder.Configuration["Mongo:Database"];
                        if (string.IsNullOrWhiteSpace(databaseName))
                        {
                            throw new InvalidOperationException("Mongo:Database must be configured.");
                        }
                        options.Database = databaseName;
                    }
                });

                builder.Services.AddSingleton<IMongoClient>(sp =>
                {
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    var options = sp.GetRequiredService<IOptions<MongoNotificationOptions>>().Value;
                    var connectionString = options.ConnectionString ?? configuration.GetConnectionString("MongoDb");

                    if (string.IsNullOrWhiteSpace(connectionString))
                    {
                        throw new InvalidOperationException("Mongo connection string is missing.");
                    }

                    return new MongoClient(MongoClientSettings.FromConnectionString(connectionString));
                });

                builder.Services.AddScoped<ICOM_NotificationService, COM_NotificationService>();
                builder.Services.AddScoped<ICOM_NotificationRepository, COM_NotificationRepository>();

                // --- REDIS CONFIGURATION (CRITICAL FIX) ---
                // We must register the ConnectionMultiplexer so the EventPublisher can use it.
                var redisConnString = builder.Configuration.GetConnectionString("Redis")
                                   ?? builder.Configuration["Redis:ConnectionString"];

                if (!string.IsNullOrWhiteSpace(redisConnString))
                {
                    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
                        ConnectionMultiplexer.Connect(redisConnString));
                }
                else
                {
                    Console.WriteLine("[WARNING] Redis Connection String is missing. Redis Publisher might fail.");
                }

                builder.Services.AddSingleton<INotificationEventPublisher, RedisNotificationEventPublisher>();

                // --- REPOSITORIES ---
                builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
                builder.Services.AddScoped<IACAD_AcademicRequestRepository, ACAD_AcademicRequestRepository>();
                builder.Services.AddScoped<IACAD_AcademicRequestHistoryRepository, ACAD_AcademicRequestHistoryRepository>();
                builder.Services.AddScoped<IACAD_EnrollmentRepository, ACAD_EnrollmentRepository>();
                builder.Services.AddScoped<ICORE_LookUpRepository, CORE_LookUpRepository>();
                builder.Services.AddScoped<IIDN_AccountRepository, IDN_AccountRepository>();
                builder.Services.AddScoped<IIDN_StudentRepository, IDN_StudentRepository>();

                // --- AUTOMAPPER ---
                builder.Services.AddAutoMapper(typeof(Application.Mappers.CORE.CORE_LookUpProfile));

                // --- DB CONTEXT ---
                builder.Services.AddDbContext<AppDbContext>(opts =>
                {
                    var sqlConn = builder.Configuration.GetConnectionString("SqlServerDb");
                    if (string.IsNullOrEmpty(sqlConn)) throw new InvalidOperationException("SQL Connection String is missing!");

                    opts.UseSqlServer(sqlConn);

                    // Enable detailed logs only if explicitly requested or in Dev
                    if (builder.Environment.IsDevelopment())
                    {
                        opts.EnableSensitiveDataLogging();
                        opts.LogTo(Console.WriteLine, LogLevel.Information);
                    }
                });

                Console.WriteLine("[SYSTEM BOOT] Services registered. Building Host...");
                var host = builder.Build();

                Console.WriteLine("[SYSTEM BOOT] Host built successfully. Starting Run...");
                host.Run();
            }
            catch (Exception ex)
            {
                // 3. Crash Catcher: This prints why the app failed to start in Azure Logs
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("❌ FATAL CRASH ON STARTUP ❌");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("--------------------------------------------------");
                throw; // Re-throw to ensure the process exits with error code
            }
        }
    }
}
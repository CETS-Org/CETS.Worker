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
using Application.Interfaces.Common.Email;
using Infrastructure.Implementations.Common.Email;
using Infrastructure.Implementations.Common.Email.EmailTemplates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;

namespace CETS.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Write to stdout/stderr which Azure Log Stream captures
            Console.Out.WriteLine("--------------------------------------------------");
            Console.Out.WriteLine($"[SYSTEM START] Application is booting at {DateTime.Now}");
            Console.Out.WriteLine("--------------------------------------------------");
            Console.Out.Flush();
            
            var builder = Host.CreateApplicationBuilder(args);

            // Configure logging for Azure Log Stream - use SimpleConsoleFormatter for better visibility
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                options.SingleLine = false;
            });
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
            
            // Register Background Services / Workers
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddHostedService<AcademicRequestExpiryWorker>();
            builder.Services.AddHostedService<PaymentReminderWorker>();
            builder.Services.AddHostedService<DropoutProcessingWorker>();
            
            // Suspension Workers
            builder.Services.AddHostedService<ApplySuspensionWorker>();
            builder.Services.AddHostedService<EndSuspensionWorker>();
            builder.Services.AddHostedService<ReturnReminderWorker>();
            builder.Services.AddHostedService<AutoDropoutWorker>();
            builder.Services.AddHostedService<CourseStartReminderWorker>();
          //  builder.Services.AddHostedService<RoomStatusUpdaterWorker>();
            builder.Services.AddMemoryCache();

            // Register Application Services
            builder.Services.AddScoped<Application.Interfaces.IMessageService, Application.Implementations.MessageService>();
            builder.Services.AddScoped<IPaymentReminderService, PaymentReminderService>();
            builder.Services.AddScoped<IDropoutProcessingService, DropoutProcessingService>();
            builder.Services.AddScoped<ISuspensionProcessingService, SuspensionProcessingService>();
            builder.Services.AddScoped<IMailService, MailService>();
            builder.Services.AddSingleton<IEmailTemplateBuilder, EmailTemplateBuilder>();
            builder.Services.AddScoped<ICurrentUserService, WorkerCurrentUserService>();
            builder.Services.AddScoped<IAttendanceWarningService, AttendanceWarningService>();
            builder.Services.AddHostedService<AttendanceWarningWorker>();
            builder.Services.AddScoped<ICourseProcessingService, CourseProcessingService>();


            // Register Email Services
            builder.Services.AddScoped<IMailService, MailService>();
            builder.Services.AddScoped<IEmailTemplateBuilder, EmailTemplateBuilder>();
            
            // Register MongoDB and Notification Service
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
                    throw new InvalidOperationException(
                        "Mongo connection string is not configured. Set Mongo:Notification:ConnectionString or ConnectionStrings:MongoDb.");
                }

                var settings = MongoClientSettings.FromConnectionString(connectionString);
                return new MongoClient(settings);
            });

            builder.Services.AddScoped<ICOM_NotificationService, COM_NotificationService>();
            builder.Services.AddScoped<ICOM_NotificationRepository, COM_NotificationRepository>();
            builder.Services.AddSingleton<INotificationEventPublisher, RedisNotificationEventPublisher>();

            // Register Repositories
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<IACAD_AcademicRequestRepository, ACAD_AcademicRequestRepository>();
            builder.Services.AddScoped<IACAD_AcademicRequestHistoryRepository, ACAD_AcademicRequestHistoryRepository>();
            builder.Services.AddScoped<IACAD_EnrollmentRepository, ACAD_EnrollmentRepository>();
            builder.Services.AddScoped<ICORE_LookUpRepository, CORE_LookUpRepository>();
            builder.Services.AddScoped<IIDN_AccountRepository, IDN_AccountRepository>();
            builder.Services.AddScoped<IIDN_StudentRepository, IDN_StudentRepository>();
            

            // Register AutoMapper
            builder.Services.AddAutoMapper(typeof(Application.Mappers.CORE.CORE_LookUpProfile));

            // Register DbContext

            builder.Services.AddDbContext<AppDbContext>(opts =>
                    opts.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerDb"))
                     .EnableSensitiveDataLogging()
                .LogTo(Console.WriteLine, LogLevel.Information));

            var host = builder.Build();
            
            // Get logger from built host
            var finalLogger = host.Services.GetRequiredService<ILogger<Program>>();
            
            finalLogger.LogInformation("--------------------------------------------------");
            finalLogger.LogInformation("[SYSTEM READY] All services registered. Starting host at {Time}", DateTime.Now);
            finalLogger.LogInformation("📆 Academic Request Expiry Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("📅 Payment Reminder Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("🎓 Dropout Processing Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("🔄 Apply Suspension Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("⏸️ End Suspension Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("🔔 Return Reminder Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("⚠️ Auto Dropout Worker scheduled at 00:00 AM (midnight) daily");
            finalLogger.LogInformation("📢 Course Start Reminder Worker scheduled");
            finalLogger.LogInformation("🎓 Dropout Processing Worker scheduled at 9:00 AM daily");
            finalLogger.LogInformation("--------------------------------------------------");
            
            host.Run();
        }
    }
}
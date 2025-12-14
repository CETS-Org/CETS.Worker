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
using MongoDB.Driver;
using System;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;

namespace CETS.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"[SYSTEM START] Application is booting at {DateTime.Now}");
            Console.WriteLine("--------------------------------------------------");
            var builder = Host.CreateApplicationBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(); // Allows "File System" logs in Azure
            builder.Logging.AddDebug();
            // Register Background Services / Workers
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddHostedService<AcademicRequestExpiryWorker>();
            Console.WriteLine("📆 Academic Request Expiry Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<PaymentReminderWorker>();
            Console.WriteLine("📅 Payment Reminder Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<DropoutProcessingWorker>();
            Console.WriteLine("🎓 Dropout Processing Worker scheduled at 00:00 AM (midnight) daily");
            
            // Suspension Workers
            builder.Services.AddHostedService<ApplySuspensionWorker>();
            Console.WriteLine("🔄 Apply Suspension Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<EndSuspensionWorker>();
            Console.WriteLine("⏸️ End Suspension Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<ReturnReminderWorker>();
            Console.WriteLine("🔔 Return Reminder Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<AutoDropoutWorker>();
            Console.WriteLine("⚠️ Auto Dropout Worker scheduled at 00:00 AM (midnight) daily");
            builder.Services.AddHostedService<CourseStartReminderWorker>();
            Console.WriteLine("📢 Course Start Reminder Worker scheduled ");
          //  builder.Services.AddHostedService<RoomStatusUpdaterWorker>();
         //   Console.WriteLine("🏢 Room Status & IsStudy Updater Worker scheduled (runs every 2 mins)");

            Console.WriteLine("🎓 Dropout Processing Worker scheduled at 9:00 AM daily");
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
            
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"[SYSTEM READY] All services registered. Starting host at {DateTime.Now}");
            Console.WriteLine("--------------------------------------------------");
            
            host.Run();
        }
    }
}
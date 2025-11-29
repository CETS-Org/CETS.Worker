using Application.Implementations;
using CETS.Worker.Consumers.Message;
using Application.Implementations.COM;
using Application.Interfaces;
using Application.Interfaces.COM;
using CETS.Worker.Services.Implementations;
using CETS.Worker.Services.Interfaces;
using CETS.Worker.Workers;
using Domain.Data;
using Domain.Interfaces.COM;
using Infrastructure.Implementations.Common.Mongo;
using Infrastructure.Implementations.Repositories.COM;
using Infrastructure.Implementations.Common.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System;

namespace CETS.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            // Register Background Services / Workers
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddHostedService<PaymentReminderWorker>();
            Console.WriteLine("📅 Payment Reminder Worker scheduled at 8:00 AM daily");
            
            // Register Application Services
            builder.Services.AddScoped<Application.Interfaces.IMessageService, Application.Implementations.MessageService>();
            builder.Services.AddScoped<IPaymentReminderService, PaymentReminderService>();
            builder.Services.AddScoped<ICurrentUserService, WorkerCurrentUserService>();
            
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

            // Register AutoMapper
            builder.Services.AddAutoMapper(typeof(Application.Mappers.CORE.CORE_LookUpProfile));

            // Register DbContext
            builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            {
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        "Connection string 'DefaultConnection' is not configured. " +
                        "Please check appsettings.json or appsettings.Development.json");
                }
                
                Console.WriteLine($"🔌 Database Connection String: {connectionString.Replace("pwd=123", "pwd=***")}");
                options.UseSqlServer(connectionString);
            });


            var host = builder.Build();
            host.Run();
        }
    }
}
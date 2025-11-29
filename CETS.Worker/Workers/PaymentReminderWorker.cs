using Application.Interfaces.COM;
using CETS.Worker.Services.Interfaces;
using DTOs.COM.COM_Notification.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CETS.Worker.Workers
{
    /// <summary>
    /// Worker để kiểm tra và gửi nhắc nhở thanh toán cho các ClassReservation 
    /// có status "2ndPaid" và invoice item thứ 2 sắp đến hạn (14 ngày)
    /// </summary>
    public class PaymentReminderWorker : BackgroundService
    {
        private readonly ILogger<PaymentReminderWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        // Thời gian chạy: mỗi ngày lúc 8h sáng (có thể config trong appsettings.json)
        private readonly TimeSpan _runTime = new TimeSpan(8, 0, 0); // 8:00 AM

        public PaymentReminderWorker(
            ILogger<PaymentReminderWorker> logger, 
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Payment Reminder Worker is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Tính toán thời gian scheduled tiếp theo (8h sáng)
                    var now = DateTime.Now;
                    var today = now.Date;
                    var scheduledTime = today + _runTime;
                    
                    // Nếu đã qua giờ chạy hôm nay (8h sáng), schedule cho ngày mai
                    if (now >= scheduledTime)
                    {
                        scheduledTime = scheduledTime.AddDays(1);
                    }

                    var delay = scheduledTime - now;
                    
                    _logger.LogInformation(
                        $"⏰ Next payment reminder check scheduled at {scheduledTime:yyyy-MM-dd HH:mm:ss}. " +
                        $"Waiting {delay.TotalHours:F1} hours until next run.");

                    // Đợi đến khi tới giờ chạy
                    await Task.Delay(delay, stoppingToken);

                    // Chạy khi đã đến giờ scheduled
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndSendRemindersAsync();
                        
                        // Sau khi chạy xong, loop sẽ tiếp tục và tính toán lại scheduledTime cho lần tiếp theo
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Payment Reminder Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Payment Reminder Worker.");
                    
                    // Nếu có lỗi, đợi 5 phút trước khi thử lại
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        private async Task CheckAndSendRemindersAsync()
        {
            _logger.LogInformation("Starting payment reminder check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var paymentReminderService = scope.ServiceProvider
                    .GetRequiredService<IPaymentReminderService>();
                
                var notificationService = scope.ServiceProvider
                    .GetRequiredService<ICOM_NotificationService>();

                try
                {
                    var reminders = await paymentReminderService.GetPendingPaymentRemindersAsync();

                    if (reminders.Count == 0)
                    {
                        _logger.LogInformation("No payment reminders to send.");
                        return;
                    }

                    _logger.LogInformation($"Found {reminders.Count} payment reminders to process.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var reminder in reminders)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"Processing Payment Reminder - Student: {reminder.StudentName} ({reminder.StudentEmail}), " +
                                $"Invoice: {reminder.InvoiceNumber}, " +
                                $"Due Date: {reminder.DueDate}, " +
                                $"Amount: {reminder.Amount:C}, " +
                                $"Package: {reminder.CoursePackageName}");

                            // Tạo notification request để gọi trực tiếp notification service
                            var (title, message) = GetNotificationContent(reminder.DaysUntilDue, reminder.InvoiceNumber, reminder.CoursePackageName, reminder.Amount, reminder.DueDate);
                            
                            var notificationRequest = new CreateNotificationRequest
                            {
                                UserId = reminder.StudentId.ToString().ToUpperInvariant(),
                                Title = title,
                                Message = message,
                                Type = "warning",
                                IsRead = false
                            };

                            // Gọi trực tiếp notification service để tạo notification
                            var notificationResponse = await notificationService.CreateAsync(notificationRequest);

                            _logger.LogInformation(
                                $"✅ Successfully created payment reminder notification for student {reminder.StudentName} " +
                                $"(Invoice: {reminder.InvoiceNumber}, Notification ID: {notificationResponse.Id})");
                            
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, 
                                $"❌ Failed to create payment reminder notification for student {reminder.StudentName} (Invoice: {reminder.InvoiceNumber})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"Payment reminder processing completed: {successCount} succeeded, {failureCount} failed out of {reminders.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing payment reminders.");
                    throw;
                }
            }
        }

        private (string Title, string Message) GetNotificationContent(int daysUntilDue, string invoiceNumber, string coursePackageName, decimal amount, DateOnly dueDate)
        {
            string title;
            string message;

            switch (daysUntilDue)
            {
                case 14:
                    title = "Payment Reminder - 14 Days Remaining";
                    message = $"Your invoice {invoiceNumber} for the course package {coursePackageName} " +
                             $"with an amount of {amount:N0} VND is due in 14 days (due date: {dueDate:dd/MM/yyyy}). " +
                             $"Please make payment before the due date.";
                    break;
                
                case 7:
                    title = "Payment Reminder - 7 Days Remaining";
                    message = $"Your invoice {invoiceNumber} for the course package {coursePackageName} " +
                             $"with an amount of {amount:N0} VND is due in 7 days (due date: {dueDate:dd/MM/yyyy}). " +
                             $"Please make payment before the due date.";
                    break;
                
                case 1:
                    title = "Payment Reminder - 1 Day Remaining";
                    message = $"Your invoice {invoiceNumber} for the course package {coursePackageName} " +
                             $"with an amount of {amount:N0} VND is due tomorrow ({dueDate:dd/MM/yyyy}). " +
                             $"Please make payment as soon as possible.";
                    break;
                
                default:
                    title = "Payment Reminder";
                    message = $"Your invoice {invoiceNumber} for the course package {coursePackageName} " +
                             $"with an amount of {amount:N0} VND is due in {daysUntilDue} days (due date: {dueDate:dd/MM/yyyy}). " +
                             $"Please make payment before the due date.";
                    break;
            }

            return (title, message);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Payment Reminder Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}


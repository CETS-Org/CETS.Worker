using Application.Interfaces.COM;
using Application.Interfaces.Common.Email;
using CETS.Worker.Helpers;
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
    /// Worker to remind students when their suspension return date is near.
    /// Runs daily at 8:00 AM to send reminders 3 days before return.
    /// </summary>
    public class ReturnReminderWorker : BackgroundService
    {
        private readonly ILogger<ReturnReminderWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private const int ReminderDaysBeforeReturn = 3;

        public ReturnReminderWorker(
            ILogger<ReturnReminderWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔔 Return Reminder Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = WorkerTimeHelper.CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        $"⏰ Next return reminder check at {DateTime.Now.Add(delay):yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Wait until midnight
                    await Task.Delay(delay, stoppingToken);

                    // Run the check
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndSendReturnRemindersAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🔔 Return Reminder Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Return Reminder Worker.");

                    // If error occurs, wait 30 seconds before retrying
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task CheckAndSendReturnRemindersAsync()
        {
            _logger.LogInformation("🔍 Starting return reminder check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var suspensionService = scope.ServiceProvider
                    .GetRequiredService<ISuspensionProcessingService>();

                var notificationService = scope.ServiceProvider
                    .GetRequiredService<ICOM_NotificationService>();

                var mailService = scope.ServiceProvider
                    .GetRequiredService<IMailService>();

                var emailTemplateBuilder = scope.ServiceProvider
                    .GetRequiredService<IEmailTemplateBuilder>();

                try
                {
                    var suspensions = await suspensionService.GetSuspensionsNearingReturnAsync(ReminderDaysBeforeReturn);

                    if (suspensions.Count == 0)
                    {
                        _logger.LogInformation("✅ No return reminders to send today.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {suspensions.Count} student(s) to remind about return.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var suspension in suspensions)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"📝 Sending Return Reminder - Student: {suspension.StudentName} ({suspension.StudentEmail}), " +
                                $"Request ID: {suspension.RequestId}, " +
                                $"End Date: {suspension.EndDate:yyyy-MM-dd}, " +
                                $"Expected Return Date: {suspension.ExpectedReturnDate:yyyy-MM-dd}");

                            // Send reminder notification to student
                            var notificationRequest = new CreateNotificationRequest
                            {
                                UserId = suspension.StudentId.ToString().ToUpperInvariant(),
                                Title = "🔔 Reminder: Your Suspension Ends Soon",
                                Message = $"This is a friendly reminder that your suspension will end in {suspension.DaysUntilReturn} days on {suspension.EndDate:MMMM dd, yyyy}. " +
                                         $"You are expected to return by {suspension.ExpectedReturnDate:MMMM dd, yyyy}. " +
                                         $"Please prepare to resume your studies and contact our support team if you have any questions. " +
                                         $"To ensure a smooth transition back, please confirm your return date with our team. " +
                                         $"We're looking forward to seeing you back!",
                                Type = "info",
                                IsRead = false
                            };

                            await notificationService.CreateAsync(notificationRequest);

                            // Send email notification
                            try
                            {
                                var emailBody = emailTemplateBuilder.BuildSuspensionReturnReminderEmail(
                                    suspension.StudentName,
                                    suspension.EndDate.ToString("MMMM dd, yyyy"),
                                    suspension.ExpectedReturnDate.ToString("MMMM dd, yyyy"),
                                    suspension.DaysUntilReturn
                                );

                                await mailService.SendEmailAsync(
                                    suspension.StudentEmail,
                                    "🔔 Reminder: Your Suspension Ends Soon - CETS",
                                    emailBody
                                );

                                _logger.LogInformation($"📧 Email sent to {suspension.StudentEmail}");
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx, $"Failed to send email to {suspension.StudentEmail}");
                                // Don't fail the entire process if email fails
                            }

                            _logger.LogInformation(
                                $"✅ Successfully sent return reminder to {suspension.StudentName} (Request ID: {suspension.RequestId})");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"❌ Failed to send return reminder to {suspension.StudentName} (Request ID: {suspension.RequestId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Return reminder processing completed: {successCount} succeeded, {failureCount} failed out of {suspensions.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while sending return reminders.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔔 Return Reminder Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}



using Application.Interfaces.COM;
using Application.Interfaces.Common.Email;
using CETS.Worker.Helpers;
using CETS.Worker.Services.Interfaces;
using DTOs.COM.COM_Notification.Requests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CETS.Worker.Workers
{
    /// <summary>
    /// Worker to automatically drop students who fail to return after grace period.
    /// Runs daily at 8:00 AM to check for overdue returns (default: 14 days grace period).
    /// </summary>
    public class AutoDropoutWorker : BackgroundService
    {
        private readonly ILogger<AutoDropoutWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private const int DefaultGracePeriodDays = 14;

        public AutoDropoutWorker(
            ILogger<AutoDropoutWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("⚠️ Auto Dropout Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = WorkerTimeHelper.CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        $"⏰ Next auto dropout check at {DateTime.Now.Add(delay):yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Wait until midnight
                    await Task.Delay(delay, stoppingToken);

                    // Run the check
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndProcessAutoDropoutsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("⚠️ Auto Dropout Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Auto Dropout Worker.");

                    // If error occurs, wait 30 seconds before retrying
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task CheckAndProcessAutoDropoutsAsync()
        {
            _logger.LogInformation("🔍 Starting auto dropout check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var suspensionService = scope.ServiceProvider
                    .GetRequiredService<ISuspensionProcessingService>();

                var notificationService = scope.ServiceProvider
                    .GetRequiredService<ICOM_NotificationService>();

                var configuration = scope.ServiceProvider
                    .GetRequiredService<IConfiguration>();

                var mailService = scope.ServiceProvider
                    .GetRequiredService<IMailService>();

                var emailTemplateBuilder = scope.ServiceProvider
                    .GetRequiredService<IEmailTemplateBuilder>();

                try
                {
                    // Get grace period from configuration or use default
                    var gracePeriodDays = configuration.GetValue<int?>("SuspensionPolicy:AwaitingReturnGraceDays") 
                                          ?? DefaultGracePeriodDays;

                    var suspensions = await suspensionService.GetOverdueReturnSuspensionsAsync(gracePeriodDays);

                    if (suspensions.Count == 0)
                    {
                        _logger.LogInformation("✅ No overdue returns to process today.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {suspensions.Count} overdue return(s) to process as auto-dropout.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var suspension in suspensions)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"📝 Processing Auto Dropout - Student: {suspension.StudentName} ({suspension.StudentEmail}), " +
                                $"Request ID: {suspension.RequestId}, " +
                                $"End Date: {suspension.EndDate:yyyy-MM-dd}, " +
                                $"Expected Return Date: {suspension.ExpectedReturnDate:yyyy-MM-dd}, " +
                                $"Days Overdue: {suspension.DaysOverdue}");

                            // Process auto dropout (change status to AutoDroppedOut)
                            await suspensionService.ProcessAutoDropoutAsync(suspension.RequestId);

                            // Send notification to student
                            var studentNotification = new CreateNotificationRequest
                            {
                                UserId = suspension.StudentId.ToString().ToUpperInvariant(),
                                Title = "⚠️ Auto Dropout - Account Suspended",
                                Message = $"Your account has been automatically dropped out due to failure to return after your suspension period. " +
                                         $"Your suspension ended on {suspension.EndDate:MMMM dd, yyyy} and you were expected to return by {suspension.ExpectedReturnDate:MMMM dd, yyyy}. " +
                                         $"After {gracePeriodDays} days of grace period, your enrollment has been terminated. " +
                                         $"If you believe this is an error or wish to re-enroll, please contact our admissions office immediately. " +
                                         $"We hope to hear from you soon.",
                                Type = "error",
                                IsRead = false
                            };

                            await notificationService.CreateAsync(studentNotification);

                            // Send email notification
                            try
                            {
                                var emailBody = emailTemplateBuilder.BuildAutoDropoutEmail(
                                    suspension.StudentName,
                                    suspension.EndDate.ToString("MMMM dd, yyyy"),
                                    suspension.ExpectedReturnDate.ToString("MMMM dd, yyyy"),
                                    suspension.DaysOverdue,
                                    gracePeriodDays
                                );

                                await mailService.SendEmailAsync(
                                    suspension.StudentEmail,
                                    "⚠️ Auto Dropout - Account Suspended - CETS",
                                    emailBody
                                );

                                _logger.LogInformation($"📧 Email sent to {suspension.StudentEmail}");
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx, $"Failed to send email to {suspension.StudentEmail}");
                                // Don't fail the entire process if email fails
                            }

                            // TODO: Optionally send notification to staff
                            // For now, staff can monitor through reports

                            _logger.LogInformation(
                                $"✅ Successfully processed auto-dropout for student {suspension.StudentName} (Request ID: {suspension.RequestId})");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"❌ Failed to process auto-dropout for student {suspension.StudentName} (Request ID: {suspension.RequestId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Auto dropout processing completed: {successCount} succeeded, {failureCount} failed out of {suspensions.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while processing auto-dropouts.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("⚠️ Auto Dropout Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}



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
    /// Worker to activate approved suspension requests when their start date arrives.
    /// Runs daily at 8:00 AM to automatically activate suspensions.
    /// </summary>
    public class ApplySuspensionWorker : BackgroundService
    {
        private readonly ILogger<ApplySuspensionWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ApplySuspensionWorker(
            ILogger<ApplySuspensionWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🔄 Apply Suspension Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = WorkerTimeHelper.CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        $"⏰ Next suspension activation check at {DateTime.Now.Add(delay):yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Wait until midnight
                    await Task.Delay(delay, stoppingToken);

                    // Run the check
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndApplySuspensionsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🔄 Apply Suspension Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Apply Suspension Worker.");

                    // If error occurs, wait 30 seconds before retrying
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task CheckAndApplySuspensionsAsync()
        {
            _logger.LogInformation("🔍 Starting suspension activation check at: {time}", DateTime.Now);

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
                    var suspensions = await suspensionService.GetApprovedSuspensionsToActivateAsync();

                    if (suspensions.Count == 0)
                    {
                        _logger.LogInformation("✅ No suspension requests to activate today.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {suspensions.Count} suspension request(s) to activate.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var suspension in suspensions)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"📝 Activating Suspension Request - Student: {suspension.StudentName} ({suspension.StudentEmail}), " +
                                $"Request ID: {suspension.RequestId}, " +
                                $"Start Date: {suspension.StartDate:yyyy-MM-dd}, " +
                                $"End Date: {suspension.EndDate:yyyy-MM-dd}, " +
                                $"Reason: {suspension.ReasonCategory}");

                            // Apply the suspension (change status to Suspended)
                            await suspensionService.ApplySuspensionAsync(suspension.RequestId);

                            // Send notification to student
                            var notificationRequest = new CreateNotificationRequest
                            {
                                UserId = suspension.StudentId.ToString().ToUpperInvariant(),
                                Title = "🔄 Suspension Activated",
                                Message = $"Your suspension has been activated as of {suspension.StartDate:MMMM dd, yyyy}. " +
                                         $"Your suspension will end on {suspension.EndDate:MMMM dd, yyyy}. " +
                                         $"Please ensure you return on or before the expected return date. " +
                                         $"You will receive a reminder 3 days before your return date. " +
                                         $"If you have any questions, please contact our support team.",
                                Type = "info",
                                IsRead = false
                            };

                            await notificationService.CreateAsync(notificationRequest);

                            // Send email notification
                            try
                            {
                                var emailBody = emailTemplateBuilder.BuildSuspensionActivatedEmail(
                                    suspension.StudentName,
                                    suspension.StartDate.ToString("MMMM dd, yyyy"),
                                    suspension.EndDate.ToString("MMMM dd, yyyy"),
                                    suspension.EndDate.AddDays(1).ToString("MMMM dd, yyyy"),
                                    suspension.ReasonCategory ?? "Not specified"
                                );

                                await mailService.SendEmailAsync(
                                    suspension.StudentEmail,
                                    "🔄 Suspension Activated - CETS",
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
                                $"✅ Successfully activated suspension for student {suspension.StudentName} (Request ID: {suspension.RequestId})");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"❌ Failed to activate suspension for student {suspension.StudentName} (Request ID: {suspension.RequestId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Suspension activation completed: {successCount} succeeded, {failureCount} failed out of {suspensions.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while activating suspension requests.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔄 Apply Suspension Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}



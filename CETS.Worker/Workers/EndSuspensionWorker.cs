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
    /// Worker to move active suspension requests to "AwaitingReturn" when their end date arrives.
    /// Runs daily at 8:00 AM to automatically end suspensions.
    /// </summary>
    public class EndSuspensionWorker : BackgroundService
    {
        private readonly ILogger<EndSuspensionWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public EndSuspensionWorker(
            ILogger<EndSuspensionWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("⏸️ End Suspension Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        $"⏰ Next suspension end check at {DateTime.Now.Add(delay):yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Wait until midnight
                    await Task.Delay(delay, stoppingToken);

                    // Run the check
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndEndSuspensionsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("⏸️ End Suspension Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in End Suspension Worker.");

                    // If error occurs, wait 30 seconds before retrying
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private TimeSpan CalculateDelayUntilMidnight()
        {
            var now = DateTime.Now;
            var nextMidnight = now.Date.AddDays(1); // Next midnight (00:00)
            var delay = nextMidnight - now;
            return delay;
        }

        private async Task CheckAndEndSuspensionsAsync()
        {
            _logger.LogInformation("🔍 Starting suspension end check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var suspensionService = scope.ServiceProvider
                    .GetRequiredService<ISuspensionProcessingService>();

                var notificationService = scope.ServiceProvider
                    .GetRequiredService<ICOM_NotificationService>();

                try
                {
                    var suspensions = await suspensionService.GetActiveSuspensionsToEndAsync();

                    if (suspensions.Count == 0)
                    {
                        _logger.LogInformation("✅ No suspension requests to end today.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {suspensions.Count} suspension request(s) to end.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var suspension in suspensions)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"📝 Ending Suspension Request - Student: {suspension.StudentName} ({suspension.StudentEmail}), " +
                                $"Request ID: {suspension.RequestId}, " +
                                $"End Date: {suspension.EndDate:yyyy-MM-dd}, " +
                                $"Expected Return Date: {suspension.ExpectedReturnDate:yyyy-MM-dd}");

                            // End the suspension (change status to AwaitingReturn)
                            await suspensionService.EndSuspensionAsync(suspension.RequestId);

                            // Send notification to student
                            var notificationRequest = new CreateNotificationRequest
                            {
                                UserId = suspension.StudentId.ToString().ToUpperInvariant(),
                                Title = "⏸️ Suspension Ended - Please Return",
                                Message = $"Your suspension period has ended as of {suspension.EndDate:MMMM dd, yyyy}. " +
                                         $"You are expected to return by {suspension.ExpectedReturnDate:MMMM dd, yyyy}. " +
                                         $"Please contact our support team to confirm your return. " +
                                         $"⚠️ Important: If you do not return within 14 days, you may be automatically dropped out. " +
                                         $"We look forward to having you back!",
                                Type = "warning",
                                IsRead = false
                            };

                            await notificationService.CreateAsync(notificationRequest);

                            _logger.LogInformation(
                                $"✅ Successfully ended suspension for student {suspension.StudentName} (Request ID: {suspension.RequestId})");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"❌ Failed to end suspension for student {suspension.StudentName} (Request ID: {suspension.RequestId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Suspension end processing completed: {successCount} succeeded, {failureCount} failed out of {suspensions.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while ending suspension requests.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("⏸️ End Suspension Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}



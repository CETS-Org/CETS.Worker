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
    /// Worker to process approved dropout requests when their effective date arrives.
    /// Runs daily at 9:00 AM to check and process dropouts.
    /// </summary>
    public class DropoutProcessingWorker : BackgroundService
    {
        private readonly ILogger<DropoutProcessingWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public DropoutProcessingWorker(
            ILogger<DropoutProcessingWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🎓 Dropout Processing Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        $"⏰ Next dropout processing check at {DateTime.Now.Add(delay):yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Wait until midnight
                    await Task.Delay(delay, stoppingToken);

                    // Run the check
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndProcessDropoutsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🎓 Dropout Processing Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Dropout Processing Worker.");

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

        private async Task CheckAndProcessDropoutsAsync()
        {
            _logger.LogInformation("🔍 Starting dropout processing check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dropoutService = scope.ServiceProvider
                    .GetRequiredService<IDropoutProcessingService>();

                var notificationService = scope.ServiceProvider
                    .GetRequiredService<ICOM_NotificationService>();

                try
                {
                    var dropouts = await dropoutService.GetApprovedDropoutRequestsAsync();

                    if (dropouts.Count == 0)
                    {
                        _logger.LogInformation("✅ No dropout requests to process today.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {dropouts.Count} dropout request(s) to process.");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var dropout in dropouts)
                    {
                        try
                        {
                            _logger.LogInformation(
                                $"📝 Processing Dropout Request - Student: {dropout.StudentName} ({dropout.StudentEmail}), " +
                                $"Request ID: {dropout.RequestId}, " +
                                $"Effective Date: {dropout.EffectiveDate:yyyy-MM-dd}, " +
                                $"Reason: {dropout.ReasonCategory}");

                            // Process the dropout (change status to Completed)
                            await dropoutService.ProcessDropoutAsync(dropout.RequestId);

                            // Send notification to student
                            var notificationRequest = new CreateNotificationRequest
                            {
                                UserId = dropout.StudentId.ToString().ToUpperInvariant(),
                                Title = "🎓 Dropout Request Completed",
                                Message = $"Your dropout request has been completed as of {dropout.EffectiveDate:MMMM dd, yyyy}. " +
                                         $"Your enrollment has been terminated. " +
                                         $"If you wish to re-enroll in the future, please contact admissions. " +
                                         $"Thank you for being part of CETS.",
                                Type = "info",
                                IsRead = false
                            };

                            await notificationService.CreateAsync(notificationRequest);

                            _logger.LogInformation(
                                $"✅ Successfully processed dropout for student {dropout.StudentName} (Request ID: {dropout.RequestId})");

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"❌ Failed to process dropout for student {dropout.StudentName} (Request ID: {dropout.RequestId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Dropout processing completed: {successCount} succeeded, {failureCount} failed out of {dropouts.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while processing dropout requests.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🎓 Dropout Processing Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}


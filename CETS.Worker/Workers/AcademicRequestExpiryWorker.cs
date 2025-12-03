using CETS.Worker.Helpers;
using Domain.Constants;
using Domain.Entities;
using Domain.Interfaces;
using Domain.Interfaces.ACAD;
using Domain.Interfaces.CORE;
using Domain.Interfaces.ACAD;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CETS.Worker.Workers
{
    /// <summary>
    /// Worker to automatically expire pending academic requests whose EffectiveDate has passed.
    /// Runs once per day at midnight.
    /// </summary>
    public class AcademicRequestExpiryWorker : BackgroundService
    {
        private readonly ILogger<AcademicRequestExpiryWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public AcademicRequestExpiryWorker(
            ILogger<AcademicRequestExpiryWorker> logger,
            IServiceScopeFactory serviceScopeFactory)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("📆 Academic Request Expiry Worker is starting. Scheduled to run daily at 00:00 AM.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = WorkerTimeHelper.CalculateDelayUntilMidnight();
                    _logger.LogInformation(
                        "⏰ Next academic request expiry check at {nextTime} (in {hours:F1} hours)",
                        DateTime.Now.Add(delay),
                        delay.TotalHours);

                    await Task.Delay(delay, stoppingToken);

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await ExpireAcademicRequestsAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("📆 Academic Request Expiry Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Academic Request Expiry Worker.");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task ExpireAcademicRequestsAsync()
        {
            _logger.LogInformation("🔍 Starting academic request expiry check at: {time}", DateTime.Now);

            using var scope = _serviceScopeFactory.CreateScope();

            var requestRepo = scope.ServiceProvider.GetRequiredService<IACAD_AcademicRequestRepository>();
            var historyRepo = scope.ServiceProvider.GetRequiredService<IACAD_AcademicRequestHistoryRepository>();
            var lookUpRepository = scope.ServiceProvider.GetRequiredService<ICORE_LookUpRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            try
            {
                var expiredStatus = await lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, "Expired");
                if (expiredStatus == null)
                {
                    _logger.LogWarning("⚠️ 'Expired' status not found in AcademicRequestStatus lookup. Skipping expiry run.");
                    return;
                }

                var pendingStatus = await lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, "Pending");
                if (pendingStatus == null)
                {
                    _logger.LogWarning("⚠️ 'Pending' status not found in AcademicRequestStatus lookup. Skipping expiry run.");
                    return;
                }

                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                var allRequests = await requestRepo.GetAllAsync();

                var toExpire = allRequests
                    .Where(r =>
                        r.AcademicRequestStatusID == pendingStatus.Id &&
                        r.EffectiveDate.HasValue &&
                        r.EffectiveDate.Value < today)
                    .ToList();

                if (!toExpire.Any())
                {
                    _logger.LogInformation("✅ No academic requests to expire today.");
                    return;
                }

                _logger.LogInformation("📋 Found {count} academic request(s) to expire.", toExpire.Count);

                foreach (var request in toExpire)
                {
                    request.AcademicRequestStatusID = expiredStatus.Id;
                    requestRepo.Update(request);

                    // Create history entry for expiry
                    var history = new ACAD_AcademicRequestHistory
                    {
                        RequestID = request.Id,
                        StatusID = expiredStatus.Id,
                        AttachmentUrl = request.AttachmentUrl
                    };
                    historyRepo.Add(history);
                }

                await unitOfWork.SaveChangesAsync();

                _logger.LogInformation("📊 Academic request expiry completed. {count} request(s) marked as Expired and logged to history.", toExpire.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while expiring academic requests.");
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("📆 Academic Request Expiry Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}



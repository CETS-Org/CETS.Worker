using CETS.Worker.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Workers
{
    public class AttendanceWarningWorker : BackgroundService
    {
        private readonly ILogger<AttendanceWarningWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public AttendanceWarningWorker(
            ILogger<AttendanceWarningWorker> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AttendanceWarningWorker started.");
            _logger.LogInformation($"AttendanceWarningWorker RUNNING at {DateTime.Now}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var warningService = scope.ServiceProvider
                            .GetRequiredService<IAttendanceWarningService>();

                        await warningService.ProcessAttendanceWarningsAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AttendanceWarningWorker");
                }

                //await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}

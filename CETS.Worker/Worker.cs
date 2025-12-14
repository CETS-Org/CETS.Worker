namespace CETS.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ Main Worker service started successfully at {time}", DateTimeOffset.Now);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("💓 Worker heartbeat - Service is running at: {time}", DateTimeOffset.Now);
                }
                // Log every 1 minute for better trace visibility in monitoring tools
                await Task.Delay(1000 * 60, stoppingToken);
            }
            
            _logger.LogInformation("⚠️ Main Worker service is stopping at {time}", DateTimeOffset.Now);
        }
    }
}

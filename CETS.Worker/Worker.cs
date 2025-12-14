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
            // Force immediate output to stdout for Azure Log Stream
            Console.Out.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ✅ Main Worker service started successfully");
            Console.Out.Flush();
            _logger.LogInformation("✅ Main Worker service started successfully at {time}", DateTimeOffset.Now);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                var timestamp = DateTimeOffset.Now;
                // Write to both Console.Out and ILogger for maximum visibility
                Console.Out.WriteLine($"[{timestamp:yyyy-MM-dd HH:mm:ss}] 💓 Worker heartbeat - Service is running");
                Console.Out.Flush();
                _logger.LogInformation("💓 Worker heartbeat - Service is running at: {time}", timestamp);
                
                // Log every 30 seconds for better trace visibility in Azure Log Stream
                await Task.Delay(1000 * 30, stoppingToken);
            }
            
            Console.Out.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ⚠️ Main Worker service is stopping");
            Console.Out.Flush();
            _logger.LogInformation("⚠️ Main Worker service is stopping at {time}", DateTimeOffset.Now);
        }
    }
}

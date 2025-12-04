using System;

namespace CETS.Worker.Helpers
{
    /// <summary>
    /// Helper class for time-related calculations used by workers.
    /// </summary>
    public static class WorkerTimeHelper
    {
        /// <summary>
        /// Calculates the delay until the next midnight (00:00).
        /// </summary>
        /// <returns>The TimeSpan representing the delay until the next midnight.</returns>
        public static TimeSpan CalculateDelayUntilMidnight()
        {
            // TODO: TESTING ONLY - Remove this and uncomment production code below
            return TimeSpan.FromSeconds(15);
            
            // PRODUCTION CODE:
            // var now = DateTime.Now;
            // var nextMidnight = now.Date.AddDays(1); // Next midnight (00:00)
            // return nextMidnight - now;
        }
    }
}


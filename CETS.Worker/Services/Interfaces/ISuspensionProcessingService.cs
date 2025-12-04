using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Interfaces
{
    public interface ISuspensionProcessingService
    {
        // Worker 1: Apply Suspension
        Task<List<SuspensionToActivateInfo>> GetApprovedSuspensionsToActivateAsync();
        Task ApplySuspensionAsync(Guid requestId);

        // Worker 2: End Suspension
        Task<List<SuspensionToEndInfo>> GetActiveSuspensionsToEndAsync();
        Task EndSuspensionAsync(Guid requestId);

        // Worker 3: Return Reminder
        Task<List<SuspensionReturnReminderInfo>> GetSuspensionsNearingReturnAsync(int daysBeforeReturn = 3);

        // Worker 4: Auto Dropout
        Task<List<SuspensionOverdueReturnInfo>> GetOverdueReturnSuspensionsAsync(int gracePeriodDays = 14);
        Task ProcessAutoDropoutAsync(Guid requestId);
    }

    /// <summary>
    /// Suspension request ready to be activated (status change from Approved → Suspended)
    /// </summary>
    public class SuspensionToActivateInfo
    {
        public Guid RequestId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public string? ReasonCategory { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Active suspension that has reached its end date (status change from Suspended → AwaitingReturn)
    /// </summary>
    public class SuspensionToEndInfo
    {
        public Guid RequestId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public DateOnly EndDate { get; set; }
        public DateOnly ExpectedReturnDate { get; set; }
        public int DaysOverdue { get; set; }
    }

    /// <summary>
    /// Suspension nearing return date (for reminder notifications)
    /// </summary>
    public class SuspensionReturnReminderInfo
    {
        public Guid RequestId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public DateOnly EndDate { get; set; }
        public DateOnly ExpectedReturnDate { get; set; }
        public int DaysUntilReturn { get; set; }
    }

    /// <summary>
    /// Suspension in AwaitingReturn status that has exceeded grace period (auto dropout)
    /// </summary>
    public class SuspensionOverdueReturnInfo
    {
        public Guid RequestId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public DateOnly EndDate { get; set; }
        public DateOnly ExpectedReturnDate { get; set; }
        public int DaysOverdue { get; set; }
    }
}



using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Interfaces
{
    public interface IDropoutProcessingService
    {
        Task<List<DropoutRequestInfo>> GetApprovedDropoutRequestsAsync();
        Task ProcessDropoutAsync(Guid requestId);
    }

    public class DropoutRequestInfo
    {
        public Guid RequestId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public DateOnly EffectiveDate { get; set; }
        public int DaysUntilEffective { get; set; }
        public string? ReasonCategory { get; set; }
        public string? Reason { get; set; }
    }
}


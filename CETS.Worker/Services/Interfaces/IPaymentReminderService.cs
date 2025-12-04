using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Interfaces
{
    public interface IPaymentReminderService
    {
        Task<List<PaymentReminderInfo>> GetPendingPaymentRemindersAsync();
    }

    public class PaymentReminderInfo
    {
        public Guid ClassReservationId { get; set; }
        public Guid StudentId { get; set; }
        public string StudentName { get; set; } = null!;
        public string StudentEmail { get; set; } = null!;
        public Guid InvoiceId { get; set; }
        public string InvoiceNumber { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public int DaysUntilDue { get; set; }
        public decimal Amount { get; set; }
        public string CoursePackageName { get; set; } = null!;
    }
}







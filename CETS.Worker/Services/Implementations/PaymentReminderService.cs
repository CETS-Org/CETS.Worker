using CETS.Worker.Services.Interfaces;
using Domain.Constants;
using Domain.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Implementations
{
    public class PaymentReminderService : IPaymentReminderService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentReminderService> _logger;

        public PaymentReminderService(AppDbContext context, ILogger<PaymentReminderService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<PaymentReminderInfo>> GetPendingPaymentRemindersAsync()
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var targetDate = today.AddDays(14); // Check invoices due in 14 days

                _logger.LogInformation($"Checking for payment reminders on {today}, target due date: {targetDate}");

                // Lấy status "2ndPaid" từ lookup
                var secondPaidStatus = await _context.CORE_LookUps
                    .Include(l => l.LookUpType)
                    .FirstOrDefaultAsync(l => l.Code == "2ndPaid" 
                                           && l.LookUpType.Code == LookUpTypes.ReservationStatus 
                                           && l.IsActive);

                if (secondPaidStatus == null)
                {
                    _logger.LogWarning("Could not find '2ndPaid' status in lookup table");
                    return new List<PaymentReminderInfo>();
                }

                // Lấy các ClassReservation có status "2ndPaid"
                var reservations = await _context.ACAD_ClassReservations
                    .Include(cr => cr.ReservationStatus)
                    .Include(cr => cr.Student)
                        .ThenInclude(s => s.Account)
                    .Include(cr => cr.CoursePackage)
                    .Include(cr => cr.ACAD_ReservationItems.OrderBy(ri => ri.PaymentSequence))
                        .ThenInclude(ri => ri.Invoice)
                            .ThenInclude(i => i.FIN_InvoiceItems)
                    .Where(cr => cr.ReservationStatusID == secondPaidStatus.Id)
                    .ToListAsync();

                _logger.LogInformation($"Found {reservations.Count} reservations with '2ndPaid' status");

                var reminderInfos = new List<PaymentReminderInfo>();

                foreach (var reservation in reservations)
                {
                    // Lấy tất cả invoices từ reservation items
                    var invoices = reservation.ACAD_ReservationItems
                        .Where(ri => ri.InvoiceID.HasValue && ri.Invoice != null)
                        .Select(ri => ri.Invoice)
                        .Distinct()
                        .ToList();

                    if (!invoices.Any())
                    {
                        _logger.LogWarning($"Reservation {reservation.Id} has no invoices");
                        continue;
                    }

                    _logger.LogInformation($"Reservation {reservation.Id} has {invoices.Count} invoice(s)");

                    foreach (var invoice in invoices)
                    {
                        if (invoice.FIN_InvoiceItems == null || !invoice.FIN_InvoiceItems.Any())
                        {
                            _logger.LogWarning($"Invoice {invoice.Id} has no invoice items");
                            continue;
                        }

                        _logger.LogInformation($"Invoice {invoice.InvoiceNumber} has {invoice.FIN_InvoiceItems.Count} invoice items");

                        // Tìm InvoiceItem có PaymentSequence = 2 (hoặc item thứ 2 nếu không có PaymentSequence)
                        FIN_InvoiceItem? secondInvoiceItem = null;

                        // Thử tìm item có PaymentSequence = 2
                        var itemsWithSequence = invoice.FIN_InvoiceItems
                            .Where(ii => ii.PaymentSequence.HasValue)
                            .ToList();

                        if (itemsWithSequence.Any())
                        {
                            secondInvoiceItem = itemsWithSequence
                                .FirstOrDefault(ii => ii.PaymentSequence == 2);
                            
                            if (secondInvoiceItem == null)
                            {
                                _logger.LogWarning($"Invoice {invoice.InvoiceNumber} doesn't have an InvoiceItem with PaymentSequence = 2");
                            }
                        }
                        
                        // Nếu không có item với PaymentSequence = 2, lấy item thứ 2 theo order
                        if (secondInvoiceItem == null)
                        {
                            var allItems = invoice.FIN_InvoiceItems
                                .OrderBy(ii => ii.PaymentSequence ?? int.MaxValue)
                                .ThenBy(ii => ii.Id)
                                .ToList();

                            if (allItems.Count >= 2)
                            {
                                secondInvoiceItem = allItems[1];
                                _logger.LogInformation($"Using 2nd invoice item (by order) for Invoice {invoice.InvoiceNumber}");
                            }
                            else
                            {
                                _logger.LogWarning($"Invoice {invoice.InvoiceNumber} doesn't have at least 2 invoice items (has {allItems.Count})");
                                continue;
                            }
                        }

                        // Check due date của invoice item thứ 2
                        var dueDate = secondInvoiceItem.DueDate;

                        if (!dueDate.HasValue)
                        {
                            _logger.LogWarning($"Invoice item {secondInvoiceItem.Id} (PaymentSequence: {secondInvoiceItem.PaymentSequence}) has no due date");
                            continue;
                        }

                        // Check số ngày còn lại trước khi đến hạn
                        var daysUntilDue = dueDate.Value.DayNumber - today.DayNumber;

                        _logger.LogInformation(
                            $"Invoice {invoice.InvoiceNumber}, InvoiceItem PaymentSequence {secondInvoiceItem.PaymentSequence ?? 0}: " +
                            $"DueDate = {dueDate.Value}, Days until due = {daysUntilDue}");

                        // Gửi notification khi còn đúng 14 ngày, 7 ngày, hoặc 1 ngày
                        if (daysUntilDue == 14 || daysUntilDue == 7 || daysUntilDue == 1)
                        {
                            var reminderInfo = new PaymentReminderInfo
                            {
                                ClassReservationId = reservation.Id,
                                StudentId = reservation.StudentID,
                                StudentName = reservation.Student?.Account?.FullName ?? "Unknown",
                                StudentEmail = reservation.Student?.Account?.Email ?? "Unknown",
                                InvoiceId = invoice.Id,
                                InvoiceNumber = invoice.InvoiceNumber,
                                DueDate = dueDate.Value,
                                DaysUntilDue = daysUntilDue,
                                Amount = invoice.TotalAmount,
                                CoursePackageName = reservation.CoursePackage?.Name ?? "Unknown Package"
                            };

                            reminderInfos.Add(reminderInfo);

                            _logger.LogInformation(
                                $"✅ Payment reminder ({daysUntilDue} days): Student {reminderInfo.StudentName} ({reminderInfo.StudentEmail}) " +
                                $"has invoice {reminderInfo.InvoiceNumber} due in {daysUntilDue} days " +
                                $"(Due date: {dueDate.Value}, Amount: {reminderInfo.Amount:C})");
                        }
                        else if (daysUntilDue < 0)
                        {
                            _logger.LogInformation(
                                $"⏭️  Skipping invoice {invoice.InvoiceNumber}: Due date ({dueDate.Value}) has already passed " +
                                $"({Math.Abs(daysUntilDue)} days ago)");
                        }
                        else if (daysUntilDue > 14)
                        {
                            _logger.LogInformation(
                                $"⏭️  Skipping invoice {invoice.InvoiceNumber}: Days until due ({daysUntilDue}) is more than 14 days. " +
                                $"(Will notify at 14, 7, and 1 day(s) before due date)");
                        }
                        else
                        {
                            _logger.LogInformation(
                                $"⏭️  Skipping invoice {invoice.InvoiceNumber}: Days until due ({daysUntilDue}) is not a reminder day. " +
                                $"(Reminder days: 14, 7, 1 day(s) before due date)");
                        }
                    }
                }

                _logger.LogInformation($"Total payment reminders to send: {reminderInfos.Count}");

                return reminderInfos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking payment reminders");
                throw;
            }
        }
    }
}


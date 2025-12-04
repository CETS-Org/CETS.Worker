using CETS.Worker.Services.Interfaces;
using Domain.Constants;
using Domain.Data;
using Domain.Interfaces.ACAD;
using Domain.Interfaces.CORE;
using Domain.Interfaces.IDN;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Implementations
{
    public class SuspensionProcessingService : ISuspensionProcessingService
    {
        private readonly AppDbContext _context;
        private readonly IACAD_AcademicRequestRepository _requestRepo;
        private readonly IIDN_StudentRepository _studentRepo;
        private readonly IACAD_EnrollmentRepository _enrollmentRepo;
        private readonly ICORE_LookUpRepository _lookUpRepository;
        private readonly ILogger<SuspensionProcessingService> _logger;
        private readonly IConfiguration _configuration;

        public SuspensionProcessingService(
            AppDbContext context,
            IACAD_AcademicRequestRepository requestRepo,
            IIDN_StudentRepository studentRepo,
            IACAD_EnrollmentRepository enrollmentRepo,
            ICORE_LookUpRepository lookUpRepository,
            ILogger<SuspensionProcessingService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _requestRepo = requestRepo;
            _studentRepo = studentRepo;
            _enrollmentRepo = enrollmentRepo;
            _lookUpRepository = lookUpRepository;
            _logger = logger;
            _configuration = configuration;
        }

        #region Worker 1: Apply Suspension

        public async Task<List<SuspensionToActivateInfo>> GetApprovedSuspensionsToActivateAsync()
        {
            try
            {
                // Get "Approved" status
                var approvedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.Approved);
                if (approvedStatus == null)
                {
                    _logger.LogWarning("Approved status not found in lookup data");
                    return new List<SuspensionToActivateInfo>();
                }

                // Get suspension request type
                var allRequestTypes = await _lookUpRepository.GetByTypeAsync("AcademicRequestType");
                var suspensionRequestTypes = allRequestTypes
                    .Where(rt => (rt.Name ?? "").ToLower().Contains("suspension"))
                    .Select(rt => rt.Id)
                    .ToList();

                if (!suspensionRequestTypes.Any())
                {
                    _logger.LogWarning("No suspension request types found in lookup data");
                    return new List<SuspensionToActivateInfo>();
                }

                var today = DateOnly.FromDateTime(DateTime.Today);

                // Get approved suspension requests where start date is today
                var requests = await _context.Set<Domain.Entities.ACAD_AcademicRequest>()
                    .Where(r => suspensionRequestTypes.Contains(r.RequestTypeID) &&
                               r.AcademicRequestStatusID == approvedStatus.Id &&
                               r.SuspensionStartDate.HasValue &&
                               r.SuspensionStartDate.Value == today)
                    .Include(r => r.Student)
                        .ThenInclude(s => s.Account)
                    .ToListAsync();

                var result = requests.Select(r => new SuspensionToActivateInfo
                {
                    RequestId = r.Id,
                    StudentId = r.StudentID,
                    StudentName = r.Student?.Account?.FullName ?? "Unknown",
                    StudentEmail = r.Student?.Account?.Email ?? "Unknown",
                    StartDate = r.SuspensionStartDate!.Value,
                    EndDate = r.SuspensionEndDate ?? r.SuspensionStartDate.Value.AddDays(7),
                    ReasonCategory = r.ReasonCategory,
                    Reason = r.Reason
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching approved suspensions to activate");
                throw;
            }
        }

        public async Task ApplySuspensionAsync(Guid requestId)
        {
            try
            {
                var request = await _requestRepo.GetByIdAsync(requestId);
                if (request == null)
                {
                    _logger.LogWarning($"Request {requestId} not found");
                    return;
                }

                // Get "Suspended" status for academic request
                var suspendedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.Suspended);
                if (suspendedStatus == null)
                {
                    _logger.LogError("Suspended status not found in lookup data");
                    return;
                }

                // Get "Suspended" enrollment status
                var suspendedEnrollmentStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.EnrollmentStatus, "Suspended");
                if (suspendedEnrollmentStatus == null)
                {
                    _logger.LogError("Suspended enrollment status not found in lookup data");
                    return;
                }

                // Update request status to Suspended
                request.AcademicRequestStatusID = suspendedStatus.Id;
                request.ProcessedAt = DateTime.Now;

                // Update enrollment status to Suspended
                if (request.EnrollmentID.HasValue)
                {
                    var enrollment = await _enrollmentRepo.GetByIdAsync(request.EnrollmentID.Value);
                    if (enrollment != null)
                    {
                        enrollment.EnrollmentStatusID = suspendedEnrollmentStatus.Id;
                        enrollment.UpdatedAt = DateTime.Now;
                        _enrollmentRepo.Update(enrollment);
                        _logger.LogInformation($"Updated enrollment {enrollment.Id} status to Suspended");
                    }
                    else
                    {
                        _logger.LogWarning($"Enrollment {request.EnrollmentID.Value} not found for request {requestId}");
                    }
                }
                else
                {
                    _logger.LogWarning($"Request {requestId} has no associated enrollment");
                }

                _requestRepo.Update(request);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully activated suspension request {requestId} - Status changed to Suspended");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while applying suspension request {requestId}");
                throw;
            }
        }

        #endregion

        #region Worker 2: End Suspension

        public async Task<List<SuspensionToEndInfo>> GetActiveSuspensionsToEndAsync()
        {
            try
            {
                // Get "Suspended" status
                var suspendedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.Suspended);
                if (suspendedStatus == null)
                {
                    _logger.LogWarning("Suspended status not found in lookup data");
                    return new List<SuspensionToEndInfo>();
                }

                // Get suspension request type
                var allRequestTypes = await _lookUpRepository.GetByTypeAsync("AcademicRequestType");
                var suspensionRequestTypes = allRequestTypes
                    .Where(rt => (rt.Name ?? "").ToLower().Contains("suspension"))
                    .Select(rt => rt.Id)
                    .ToList();

                if (!suspensionRequestTypes.Any())
                {
                    _logger.LogWarning("No suspension request types found in lookup data");
                    return new List<SuspensionToEndInfo>();
                }

                var today = DateOnly.FromDateTime(DateTime.Today);

                // Get suspended requests where end date has passed
                var requests = await _context.Set<Domain.Entities.ACAD_AcademicRequest>()
                    .Where(r => suspensionRequestTypes.Contains(r.RequestTypeID) &&
                               r.AcademicRequestStatusID == suspendedStatus.Id &&
                               r.SuspensionEndDate.HasValue &&
                               r.SuspensionEndDate.Value < today)
                    .Include(r => r.Student)
                        .ThenInclude(s => s.Account)
                    .ToListAsync();

                var result = requests.Select(r => new SuspensionToEndInfo
                {
                    RequestId = r.Id,
                    StudentId = r.StudentID,
                    StudentName = r.Student?.Account?.FullName ?? "Unknown",
                    StudentEmail = r.Student?.Account?.Email ?? "Unknown",
                    EndDate = r.SuspensionEndDate!.Value,
                    ExpectedReturnDate = r.ExpectedReturnDate ?? r.SuspensionEndDate.Value.AddDays(1),
                    DaysOverdue = today.DayNumber - r.SuspensionEndDate.Value.DayNumber
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching active suspensions to end");
                throw;
            }
        }

        public async Task EndSuspensionAsync(Guid requestId)
        {
            try
            {
                var request = await _requestRepo.GetByIdAsync(requestId);
                if (request == null)
                {
                    _logger.LogWarning($"Request {requestId} not found");
                    return;
                }

                // Get "AwaitingReturn" status
                var awaitingReturnStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.AwaitingReturn);
                if (awaitingReturnStatus == null)
                {
                    _logger.LogError("AwaitingReturn status not found in lookup data");
                    return;
                }

                // Update request status to AwaitingReturn
                request.AcademicRequestStatusID = awaitingReturnStatus.Id;

                _requestRepo.Update(request);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully ended suspension request {requestId} - Status changed to AwaitingReturn");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while ending suspension request {requestId}");
                throw;
            }
        }

        #endregion

        #region Worker 3: Return Reminder

        public async Task<List<SuspensionReturnReminderInfo>> GetSuspensionsNearingReturnAsync(int daysBeforeReturn = 3)
        {
            try
            {
                // Get "Suspended" status
                var suspendedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.Suspended);
                if (suspendedStatus == null)
                {
                    _logger.LogWarning("Suspended status not found in lookup data");
                    return new List<SuspensionReturnReminderInfo>();
                }

                // Get suspension request type
                var allRequestTypes = await _lookUpRepository.GetByTypeAsync("AcademicRequestType");
                var suspensionRequestTypes = allRequestTypes
                    .Where(rt => (rt.Name ?? "").ToLower().Contains("suspension"))
                    .Select(rt => rt.Id)
                    .ToList();

                if (!suspensionRequestTypes.Any())
                {
                    _logger.LogWarning("No suspension request types found in lookup data");
                    return new List<SuspensionReturnReminderInfo>();
                }

                var today = DateOnly.FromDateTime(DateTime.Today);
                var reminderDate = today.AddDays(daysBeforeReturn);

                // Get suspended requests where end date is N days from now
                var requests = await _context.Set<Domain.Entities.ACAD_AcademicRequest>()
                    .Where(r => suspensionRequestTypes.Contains(r.RequestTypeID) &&
                               r.AcademicRequestStatusID == suspendedStatus.Id &&
                               r.SuspensionEndDate.HasValue &&
                               r.SuspensionEndDate.Value == reminderDate)
                    .Include(r => r.Student)
                        .ThenInclude(s => s.Account)
                    .ToListAsync();

                var result = requests.Select(r => new SuspensionReturnReminderInfo
                {
                    RequestId = r.Id,
                    StudentId = r.StudentID,
                    StudentName = r.Student?.Account?.FullName ?? "Unknown",
                    StudentEmail = r.Student?.Account?.Email ?? "Unknown",
                    EndDate = r.SuspensionEndDate!.Value,
                    ExpectedReturnDate = r.ExpectedReturnDate ?? r.SuspensionEndDate.Value.AddDays(1),
                    DaysUntilReturn = daysBeforeReturn
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching suspensions nearing return");
                throw;
            }
        }

        #endregion

        #region Worker 4: Auto Dropout

        public async Task<List<SuspensionOverdueReturnInfo>> GetOverdueReturnSuspensionsAsync(int gracePeriodDays = 14)
        {
            try
            {
                // Get "AwaitingReturn" status
                var awaitingReturnStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.AwaitingReturn);
                if (awaitingReturnStatus == null)
                {
                    _logger.LogWarning("AwaitingReturn status not found in lookup data");
                    return new List<SuspensionOverdueReturnInfo>();
                }

                // Get suspension request type
                var allRequestTypes = await _lookUpRepository.GetByTypeAsync(LookUpTypes.AcademicRequestType);
                var suspensionRequestTypes = allRequestTypes
                    .Where(rt => (rt.Name ?? "").ToLower().Contains("suspension"))
                    .Select(rt => rt.Id)
                    .ToList();

                if (!suspensionRequestTypes.Any())
                {
                    _logger.LogWarning("No suspension request types found in lookup data");
                    return new List<SuspensionOverdueReturnInfo>();
                }

                var today = DateOnly.FromDateTime(DateTime.Today);
                var gracePeriodEndDate = today.AddDays(-gracePeriodDays);

                // Get AwaitingReturn requests where end date + grace period has passed
                var requests = await _context.Set<Domain.Entities.ACAD_AcademicRequest>()
                    .Where(r => suspensionRequestTypes.Contains(r.RequestTypeID) &&
                               r.AcademicRequestStatusID == awaitingReturnStatus.Id &&
                               r.SuspensionEndDate.HasValue &&
                               r.SuspensionEndDate.Value <= gracePeriodEndDate)
                    .Include(r => r.Student)
                        .ThenInclude(s => s.Account)
                    .ToListAsync();

                var result = requests.Select(r => new SuspensionOverdueReturnInfo
                {
                    RequestId = r.Id,
                    StudentId = r.StudentID,
                    StudentName = r.Student?.Account?.FullName ?? "Unknown",
                    StudentEmail = r.Student?.Account?.Email ?? "Unknown",
                    EndDate = r.SuspensionEndDate!.Value,
                    ExpectedReturnDate = r.ExpectedReturnDate ?? r.SuspensionEndDate.Value.AddDays(1),
                    DaysOverdue = today.DayNumber - r.SuspensionEndDate.Value.DayNumber
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching overdue return suspensions");
                throw;
            }
        }

        public async Task ProcessAutoDropoutAsync(Guid requestId)
        {
            try
            {
                var request = await _requestRepo.GetByIdAsync(requestId);
                if (request == null)
                {
                    _logger.LogWarning($"Request {requestId} not found");
                    return;
                }

                // Get "AutoDroppedOut" status for academic request
                var autoDroppedOutStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, SuspensionStatuses.AutoDroppedOut);
                if (autoDroppedOutStatus == null)
                {
                    _logger.LogError("AutoDroppedOut status not found in lookup data");
                    return;
                }

                // Get "Dropped" enrollment status
                var droppedOutEnrollmentStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.EnrollmentStatus, "Dropped");

                // Update request status to AutoDroppedOut
                request.AcademicRequestStatusID = autoDroppedOutStatus.Id;

                // Update enrollment status to Dropped and remove class assignment
                if (droppedOutEnrollmentStatus != null && request.EnrollmentID.HasValue)
                {
                    var enrollment = await _enrollmentRepo.GetByIdAsync(request.EnrollmentID.Value);
                    if (enrollment != null)
                    {
                        enrollment.EnrollmentStatusID = droppedOutEnrollmentStatus.Id;
                        enrollment.ClassID = null; // Remove class assignment when dropped out
                        _enrollmentRepo.Update(enrollment);
                        _logger.LogInformation($"Updated enrollment {enrollment.Id} status to Dropped and removed class assignment");
                    }
                    else
                    {
                        _logger.LogWarning($"Enrollment {request.EnrollmentID.Value} not found for request {requestId}");
                    }
                }
                else
                {
                    if (!request.EnrollmentID.HasValue)
                        _logger.LogWarning($"Request {requestId} has no associated enrollment");
                    if (droppedOutEnrollmentStatus == null)
                        _logger.LogWarning("Dropped enrollment status not found in lookup data");
                }

                _requestRepo.Update(request);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully processed auto-dropout for request {requestId} - Status changed to AutoDroppedOut");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while processing auto-dropout for request {requestId}");
                throw;
            }
        }

        #endregion
    }
}


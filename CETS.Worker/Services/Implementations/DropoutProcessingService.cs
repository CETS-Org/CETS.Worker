using CETS.Worker.Services.Interfaces;
using Domain.Constants;
using Domain.Data;
using Domain.Interfaces.ACAD;
using Domain.Interfaces.CORE;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Implementations
{
    public class DropoutProcessingService : IDropoutProcessingService
    {
        private readonly AppDbContext _context;
        private readonly IACAD_AcademicRequestRepository _requestRepo;
        private readonly ICORE_LookUpRepository _lookUpRepository;
        private readonly ILogger<DropoutProcessingService> _logger;

        public DropoutProcessingService(
            AppDbContext context,
            IACAD_AcademicRequestRepository requestRepo,
            ICORE_LookUpRepository lookUpRepository,
            ILogger<DropoutProcessingService> logger)
        {
            _context = context;
            _requestRepo = requestRepo;
            _lookUpRepository = lookUpRepository;
            _logger = logger;
        }

        public async Task<List<DropoutRequestInfo>> GetApprovedDropoutRequestsAsync()
        {
            try
            {
                // Get "Approved" status
                var approvedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, "Approved");
                if (approvedStatus == null)
                {
                    _logger.LogWarning("Approved status not found in lookup data");
                    return new List<DropoutRequestInfo>();
                }

                // Get all dropout request types
                var allRequestTypes = await _lookUpRepository.GetByTypeAsync("AcademicRequestType");
                var dropoutRequestTypes = allRequestTypes
                    .Where(rt => (rt.Name ?? "").ToLower().Contains("dropout"))
                    .Select(rt => rt.Id)
                    .ToList();

                if (!dropoutRequestTypes.Any())
                {
                    _logger.LogWarning("No dropout request types found in lookup data");
                    return new List<DropoutRequestInfo>();
                }

                var today = DateOnly.FromDateTime(DateTime.Today);

                // Get approved dropout requests where effective date is today
                var requests = await _context.Set<Domain.Entities.ACAD_AcademicRequest>()
                    .Where(r => dropoutRequestTypes.Contains(r.RequestTypeID) &&
                               r.AcademicRequestStatusID == approvedStatus.Id &&
                               r.EffectiveDate.HasValue &&
                               r.EffectiveDate.Value == today)
                    .Include(r => r.Student)
                        .ThenInclude(s => s.Account)
                    .ToListAsync();

                var result = requests.Select(r => new DropoutRequestInfo
                {
                    RequestId = r.Id,
                    StudentId = r.StudentID,
                    StudentName = r.Student?.Account?.FullName ?? "Unknown",
                    StudentEmail = r.Student?.Account?.Email ?? "Unknown",
                    EffectiveDate = r.EffectiveDate!.Value,
                    DaysUntilEffective = 0, // Today
                    ReasonCategory = r.ReasonCategory,
                    Reason = r.Reason
                }).ToList();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching approved dropout requests");
                throw;
            }
        }

        public async Task ProcessDropoutAsync(Guid requestId)
        {
            try
            {
                var request = await _requestRepo.GetByIdAsync(requestId);
                if (request == null)
                {
                    _logger.LogWarning($"Request {requestId} not found");
                    return;
                }

                // Get "Completed" status
                var completedStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.AcademicRequestStatus, "Completed");
                if (completedStatus == null)
                {
                    _logger.LogError("Completed status not found in lookup data");
                    return;
                }

                // Update request status to Completed
                request.AcademicRequestStatusID = completedStatus.Id;
                request.ProcessedAt = DateTime.Now;

                _requestRepo.Update(request);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Successfully processed dropout request {requestId} - Status changed to Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while processing dropout request {requestId}");
                throw;
            }
        }
    }
}


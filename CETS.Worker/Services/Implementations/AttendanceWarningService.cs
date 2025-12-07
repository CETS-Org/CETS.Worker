using Application.Interfaces.Common.Email;
using CETS.Worker.Services.Interfaces;
using Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CETS.Worker.Services.Implementations
{
    public class AttendanceWarningService : IAttendanceWarningService
    {
        private readonly AppDbContext _context;
        private readonly IMailService _mailService;
        private readonly IEmailTemplateBuilder _templateBuilder;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AttendanceWarningService> _logger;

        public AttendanceWarningService(
            AppDbContext context,
            IMailService mailService,
            IEmailTemplateBuilder templateBuilder,
            IMemoryCache cache,
            ILogger<AttendanceWarningService> logger)
        {
            _context = context;
            _mailService = mailService;
            _templateBuilder = templateBuilder;
            _cache = cache;
            _logger = logger;
        }

        public async Task ProcessAttendanceWarningsAsync()
        {
            // 1️⃣ Lấy enrollments đang học
            var activeEnrollments = await _context.ACAD_Enrollments
                .Include(e => e.EnrollmentStatus)
                .Include(e => e.Student).ThenInclude(s => s.Account)
                .Include(e => e.Course)
                .Where(e => e.EnrollmentStatus.Code == "Enrolled" &&
                 e.ClassID != null)
                .ToListAsync();

            if (!activeEnrollments.Any())
                return;

            var groups = activeEnrollments.GroupBy(e => e.ClassID).ToList();

            foreach (var group in groups)
            {
                var classId = group.Key;

                var className = await _context.ACAD_Enrollments
                      .Include(d => d.Class)
                    .Where(c => c.ClassID == classId)
                         .Select(d => d.Class.ClassName)
                    .FirstOrDefaultAsync() ?? "(Unknown Class)";

                // 4️⃣ Tổng số buổi học theo ClassID
                var totalSessions = await _context.ACAD_ClassMeetings
                    .Where(m => m.ClassID == classId && !m.IsDeleted)
                    .CountAsync();

                if (totalSessions == 0)
                    continue;

                var maxAbsent = (int)Math.Floor(totalSessions * 0.5);

                foreach (var enrollment in group)
                {
                    var stu = enrollment.Student;

                    var studentId = stu.Account.Id;

                    var absent = await _context.ACAD_Attendances
                        .Where(a => a.StudentID == studentId &&
                                    a.Meeting.ClassID == classId &&
                                    a.AttendanceStatus.Code == "Absent")
                        .CountAsync();

                    var warningThreshold = (int)Math.Ceiling(totalSessions * 0.1);
                    
                    // Send email if student has reached warning threshold (10%) and hasn't exceeded max (30%)
                    if (absent >= warningThreshold && absent <= maxAbsent)
                    {
                        string cacheKey = $"attendance-warning-{studentId}-{classId}-{absent}";

                        if (_cache.TryGetValue(cacheKey, out _))
                        {
                            _logger.LogInformation("Skipping attendance warning email - Student Code: {StudentCode}, Absent: {Absent}/{TotalSessions} (already sent recently)", 
                                stu.StudentCode, absent, totalSessions);
                            continue;
                        }

                        _logger.LogInformation("Sending attendance warning email - Student Code: {StudentCode}, Student Name: {StudentName}, Email: {Email}, Absent: {Absent}/{TotalSessions}, Class: {ClassName}", 
                            stu.StudentCode, stu.Account.FullName, stu.Account.Email, absent, totalSessions, className);

                        var emailBody = _templateBuilder.BuildAttendanceWarningEmail(
                            stu.Account.FullName,
                            enrollment.Course.CourseName,
                            className,
                            absent,
                            totalSessions,
                            maxAbsent
                        );

                        await _mailService.SendEmailAsync(
                            stu.Account.Email,
                            "Attendance Warning",
                            emailBody
                        );

                        _logger.LogInformation("Successfully sent attendance warning email - Student Code: {StudentCode}, Absent: {Absent}/{TotalSessions}", 
                            stu.StudentCode, absent, totalSessions);

                        _cache.Set(cacheKey, true, TimeSpan.FromHours(24));
                    }
                    else if (absent > 0)
                    {
                        _logger.LogDebug("Student attendance check - Student Code: {StudentCode}, Absent: {Absent}/{TotalSessions}, Warning Threshold: {WarningThreshold}, Max Absent: {MaxAbsent}", 
                            stu.StudentCode, absent, totalSessions, warningThreshold, maxAbsent);
                    }
                }

            }
        }
    }
}

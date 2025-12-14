using CETS.Worker.Services.Interfaces;
using Domain.Constants;
using Domain.Data;
using Domain.Interfaces.CORE;
using DTOs.ACAD.ACAD_Course.Responses;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Implementations
{
    public class CourseProcessingService : ICourseProcessingService
    {
        private readonly AppDbContext _context;
        private readonly ICORE_LookUpRepository _lookUpRepository;
        private readonly ILogger<CourseProcessingService> _logger;

        public CourseProcessingService(
            AppDbContext context,
            ICORE_LookUpRepository lookUpRepository,
            ILogger<CourseProcessingService> logger)
        {
            _context = context;
            _lookUpRepository = lookUpRepository;
            _logger = logger;
        }

        public async Task<List<UpcomingCourseEnrollmentInfo>> GetEnrollmentsForUpcomingClassesAsync(int daysBefore)
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                var targetDate = today.AddDays(daysBefore);

                // Lấy ID trạng thái "Enrolled"
                var enrolledStatus = await _lookUpRepository.GetByCodeAsync(LookUpTypes.EnrollmentStatus, "Enrolled");

                if (enrolledStatus == null)
                {
                    _logger.LogWarning("❌ 'Enrolled' status not found in lookup data.");
                    return new List<UpcomingCourseEnrollmentInfo>();
                }

                // Query chính
                var enrollments = await _context.Set<Domain.Entities.ACAD_Enrollment>()
                    // Include các bảng liên quan để tránh null reference (dù Select đã handle, nhưng include giúp debug dễ hơn)
                    .Include(e => e.Class)
                    .Include(e => e.Student)
                        .ThenInclude(s => s.Account)
                    .Where(e => e.ClassID.HasValue
                             && e.Class.StartDate == targetDate
                             && e.EnrollmentStatusID == enrolledStatus.Id
                             && e.Student.Account.Email != null)
                    .Select(e => new UpcomingCourseEnrollmentInfo
                    {
                        EnrollmentId = e.Id,
                        StudentId = e.StudentID,
                        StudentName = e.Student.Account.FullName ?? "Student",
                        StudentEmail = e.Student.Account.Email,

                        // Thông tin chung về lớp/khóa học
                        ClassCode = e.Class.ClassName,
                        CourseName = e.Class.TeacherAssignment != null
                                     ? e.Class.TeacherAssignment.Course.CourseName
                                     : "Unknown Course",
                        StartDate = e.Class.StartDate,

                        // --- LẤY TỪ CLASS MEETING ---
                        // Logic: Tìm buổi học trùng với ngày khai giảng (StartDate) hoặc buổi đầu tiên
                        RoomName = e.Class.ACAD_ClassMeetings
                                    .Where(m => m.Date == e.Class.StartDate) // Lấy buổi học ngày khai giảng
                                    .Select(m => m.Room != null ? m.Room.RoomCode : "TBA")
                                    .FirstOrDefault() ?? "TBA",

                        TimeSlot = e.Class.ACAD_ClassMeetings
                                    .Where(m => m.Date == e.Class.StartDate)
                                    .Select(m => m.Slot != null
                                                 ? m.Slot.Name // Hoặc: m.TimeSlot.StartTime + " - " + m.TimeSlot.EndTime
                                                 : "Check Schedule")
                                    .FirstOrDefault() ?? "Check Schedule"
                    })
                    .ToListAsync();

                return enrollments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error while fetching enrollments for upcoming classes.");
                throw;
            }
        }
    }
}

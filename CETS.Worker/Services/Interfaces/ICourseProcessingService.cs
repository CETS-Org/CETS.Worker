using DTOs.ACAD.ACAD_Course.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Interfaces
{
    public interface ICourseProcessingService
    {
        Task<List<UpcomingCourseEnrollmentInfo>> GetEnrollmentsForUpcomingClassesAsync(int daysBefore);
    }
}

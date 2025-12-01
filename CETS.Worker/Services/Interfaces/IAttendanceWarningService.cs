using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Services.Interfaces
{
    public interface IAttendanceWarningService
    {
        Task ProcessAttendanceWarningsAsync();
    }
}

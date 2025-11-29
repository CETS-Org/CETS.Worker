using Application.Interfaces;
using System;

namespace CETS.Worker.Services.Implementations
{
    /// <summary>
    /// Implementation of ICurrentUserService for background worker context
    /// where there is no HTTP context or authenticated user
    /// </summary>
    public class WorkerCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null; // No user in background worker context
        
        public string? UserEmail => "system@worker"; // System user for worker
    }
}







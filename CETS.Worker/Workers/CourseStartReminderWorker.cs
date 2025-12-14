using Application.Interfaces.COM;
using Application.Interfaces.Common.Email;
using CETS.Worker.Helpers;
using CETS.Worker.Services.Interfaces;
using DTOs.COM.COM_Notification.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Workers
{
    public class CourseStartReminderWorker : BackgroundService
    {
        private readonly ILogger<CourseStartReminderWorker> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration; // Thêm Configuration để đọc setting

        // Cấu hình mặc định nếu không tìm thấy trong appsettings
        private const int DefaultRunHour = 5; // Mặc định chạy lúc 8:00 AM
        private const int DefaultRunMinute = 0;
        private const int DaysBeforeStart = 7;

        public CourseStartReminderWorker(
            ILogger<CourseStartReminderWorker> logger,
            IServiceScopeFactory serviceScopeFactory,
            IConfiguration configuration) // Inject Configuration
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Đọc giờ chạy từ Config, nếu không có thì lấy mặc định (8:00)
            int runHour = _configuration.GetValue<int>("CourseReminder:RunHour", DefaultRunHour);
            int runMinute = _configuration.GetValue<int>("CourseReminder:RunMinute", DefaultRunMinute);

            _logger.LogInformation($"📢 Course Start Reminder Worker is starting. Scheduled to run daily at {runHour:D2}:{runMinute:D2}.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Tính toán thời gian chờ đến giờ chạy tiếp theo
                    var delay = CalculateDelayUntilScheduledTime(runHour, runMinute);

                    var nextRunTime = DateTime.Now.Add(delay);
                    _logger.LogInformation(
                        $"⏰ Next course reminder check at {nextRunTime:yyyy-MM-dd HH:mm:ss} (in {delay.TotalHours:F1} hours)");

                    // Chờ đến giờ hẹn
                    await Task.Delay(delay, stoppingToken);

                    // Thực thi logic
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await CheckAndProcessRemindersAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("⚠️ Course Start Reminder Worker is stopping.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred in Course Start Reminder Worker.");
                    // Nếu lỗi, chờ 1 phút rồi thử lại vòng lặp (để tính lại delay)
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        /// <summary>
        /// Hàm tính toán thời gian delay đến giờ chạy mong muốn
        /// </summary>
        private TimeSpan CalculateDelayUntilScheduledTime(int hour, int minute)
        {
            var now = DateTime.Now;

            // Tạo đối tượng thời gian chạy cho ngày hôm nay
            var nextRun = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

            // Logic: Nếu giờ hiện tại đã trôi qua giờ hẹn của hôm nay (ví dụ: hẹn 8h sáng mà giờ là 9h sáng)
            // Thì lịch chạy sẽ là 8h sáng ngày mai.
            if (now >= nextRun)
            {
                nextRun = nextRun.AddDays(1);
            }

            return nextRun - now;
        }

        private async Task CheckAndProcessRemindersAsync()
        {
            _logger.LogInformation("🔍 Starting upcoming course check at: {time}", DateTime.Now);

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var courseService = scope.ServiceProvider.GetRequiredService<ICourseProcessingService>();
                var notificationService = scope.ServiceProvider.GetRequiredService<ICOM_NotificationService>();
                var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();
                var emailTemplateBuilder = scope.ServiceProvider.GetRequiredService<IEmailTemplateBuilder>();

                try
                {
                    var upcomingEnrollments = await courseService.GetEnrollmentsForUpcomingClassesAsync(DaysBeforeStart);

                    if (upcomingEnrollments.Count == 0)
                    {
                        _logger.LogInformation($"✅ No classes starting in exactly {DaysBeforeStart} days.");
                        return;
                    }

                    _logger.LogInformation($"📋 Found {upcomingEnrollments.Count} enrollment(s) starting in {DaysBeforeStart} days. Sending reminders...");

                    var successCount = 0;
                    var failureCount = 0;

                    foreach (var item in upcomingEnrollments)
                    {
                        try
                        {
                            // 1. Gửi Notification
                            var notifRequest = new CreateNotificationRequest
                            {
                                UserId = item.StudentId.ToString().ToUpperInvariant(),
                                Title = "📅 Reminder: Class Starting Soon",
                                Message = $"Hi {item.StudentName}, your class {item.ClassCode} ({item.CourseName}) is scheduled to start on {item.StartDate:MMMM dd, yyyy}. Please check your schedule.",
                                Type = "info",
                                IsRead = false
                            };
                            await notificationService.CreateAsync(notifRequest);

                            // 2. Gửi Email
                            try
                            {
                                var emailBody = emailTemplateBuilder.BuildCourseStartReminderEmail(
                                    item.StudentName,
                                    item.CourseName,
                                    item.ClassCode,
                                    item.StartDate.ToString(" dd / MM / yyyy"),
                                    item.RoomName
                                );

                                await mailService.SendEmailAsync(
                                    item.StudentEmail,
                                    $"📅 Upcoming Class Reminder: {item.CourseName} - CETS",
                                    emailBody
                                );

                                _logger.LogInformation($"📧 Email sent to {item.StudentEmail} for class {item.ClassCode}");
                            }
                            catch (Exception emailEx)
                            {
                                _logger.LogError(emailEx, $"Failed to send email to {item.StudentEmail}");
                            }

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"❌ Failed to process reminder for student {item.StudentName} (Enrollment: {item.EnrollmentId})");
                            failureCount++;
                        }
                    }

                    _logger.LogInformation(
                        $"📊 Course reminder processing completed: {successCount} succeeded, {failureCount} failed out of {upcomingEnrollments.Count} total.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error while processing course reminders.");
                    throw;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("⚠️ Course Start Reminder Worker is stopping.");
            await base.StopAsync(cancellationToken);
        }
    }
}

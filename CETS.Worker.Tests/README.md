# CETS Worker Tests

## Overview
Comprehensive test suite for the Dropout Processing Worker, including unit tests and integration tests.

---

## Test Projects Structure

```
CETS.Worker.Tests/
├── Services/
│   └── DropoutProcessingServiceTests.cs      (Unit tests for service)
├── Workers/
│   └── DropoutProcessingWorkerTests.cs       (Unit tests for worker)
├── Integration/
│   └── DropoutProcessingIntegrationTests.cs  (End-to-end tests)
├── GlobalUsings.cs
├── CETS.Worker.Tests.csproj
└── README.md
```

---

## Running Tests

### Run All Tests
```bash
cd CETS.Worker/CETS.Worker.Tests
dotnet test
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~DropoutProcessingServiceTests"
dotnet test --filter "FullyQualifiedName~DropoutProcessingWorkerTests"
dotnet test --filter "FullyQualifiedName~DropoutProcessingIntegrationTests"
```

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~GetApprovedDropoutRequestsAsync_ReturnsOnlyTodaysDropouts"
```

### Run with Detailed Output
```bash
dotnet test --verbosity detailed
```

### Run with Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverageReportFormat=opencover
```

---

## Test Categories

### Unit Tests - DropoutProcessingServiceTests

**Test Cases:**
1. ✅ `GetApprovedDropoutRequestsAsync_ReturnsOnlyTodaysDropouts`
   - Verifies only dropouts with today's effective date are returned
   - Tests date filtering logic

2. ✅ `GetApprovedDropoutRequestsAsync_ReturnsEmptyList_WhenNoDropoutsToday`
   - Verifies empty list when no dropouts due today
   - Tests edge case handling

3. ✅ `GetApprovedDropoutRequestsAsync_ExcludesNonApprovedRequests`
   - Verifies only "Approved" status requests are returned
   - Tests status filtering

4. ✅ `GetApprovedDropoutRequestsAsync_ExcludesDeletedRequests`
   - Verifies soft-deleted requests are excluded
   - Tests IsDeleted flag filtering

5. ✅ `GetApprovedDropoutRequestsAsync_HandlesMultipleDropoutsToday`
   - Verifies multiple dropouts are processed correctly
   - Tests batch processing

6. ✅ `ProcessDropoutAsync_ChangesStatusToCompleted`
   - Verifies status changes from Approved to Completed
   - Tests core processing logic

7. ✅ `ProcessDropoutAsync_HandlesRequestNotFound`
   - Verifies graceful handling of missing requests
   - Tests error resilience

8. ✅ `ProcessDropoutAsync_LogsWarning_WhenRequestNotFound`
   - Verifies appropriate logging
   - Tests observability

9. ✅ `GetApprovedDropoutRequestsAsync_ReturnsCorrectDropoutInfo`
   - Verifies all fields are mapped correctly
   - Tests data integrity

### Unit Tests - DropoutProcessingWorkerTests

**Test Cases:**
1. ✅ `Worker_ProcessesSingleDropout_Successfully`
   - Tests worker can process a single dropout
   - Verifies service calls

2. ✅ `GetDropoutInfo_ContainsCorrectData`
   - Tests DropoutRequestInfo data structure
   - Verifies data mapping

3. ✅ `DropoutService_ProcessesMultipleDropouts`
   - Tests handling of multiple dropouts
   - Verifies batch processing

4. ✅ `NotificationService_CreatesCorrectNotification`
   - Tests notification creation
   - Verifies notification content

### Integration Tests - DropoutProcessingIntegrationTests

**Test Cases:**
1. ✅ `EndToEnd_ProcessDropout_Success`
   - Full end-to-end test with real database
   - Tests complete workflow

2. ✅ `EndToEnd_MultipleDropouts_AllProcessedSuccessfully`
   - Tests processing 5 dropouts simultaneously
   - Verifies scalability

3. ✅ `EndToEnd_FiltersByEffectiveDate_Correctly`
   - Tests date filtering with past, present, future dates
   - Verifies temporal logic

4. ✅ `EndToEnd_FiltersByStatus_Correctly`
   - Tests status filtering (Pending, Approved, Completed)
   - Verifies status logic

5. ✅ `EndToEnd_ProcessDropout_UpdatesTimestamp`
   - Tests ProcessedAt timestamp is set correctly
   - Verifies audit trail

---

## Test Coverage

### Service Layer
- ✅ Query logic (filtering by date, status, type)
- ✅ Status updates (Approved → Completed)
- ✅ Timestamp management
- ✅ Error handling
- ✅ Logging

### Worker Layer
- ✅ Service orchestration
- ✅ Notification creation
- ✅ Error resilience
- ✅ Batch processing

### Integration
- ✅ Database operations
- ✅ Repository interactions
- ✅ End-to-end workflow
- ✅ Data integrity

---

## Test Data

### Lookup Data (Seeded in Tests)
```csharp
// Statuses
Pending   - Code: "Pending"
Approved  - Code: "Approved"
Completed - Code: "Completed"

// Request Types
Dropout - Code: "Dropout", Name: "Dropout"

// Priorities
Medium - Code: "Medium", Name: "Medium"
```

### Test Students
```csharp
new IDN_Account
{
    FullName = "Integration Test Student",
    Email = "integration@test.com",
    Role = "Student"
}
```

### Test Dropout Requests
```csharp
new ACAD_AcademicRequest
{
    StudentID = studentId,
    RequestTypeID = dropoutTypeId,
    AcademicRequestStatusID = approvedStatusId,
    EffectiveDate = today,
    CompletedExitSurvey = true,
    Reason = "Test dropout reason"
}
```

---

## Assertions Used

### FluentAssertions Examples
```csharp
// Collection assertions
result.Should().HaveCount(1);
result.Should().BeEmpty();
result.Should().OnlyContain(r => r.Status == "Completed");

// Value assertions
request.Status.Should().Be("Completed");
request.ProcessedAt.Should().NotBeNull();
request.ProcessedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));

// String assertions
dropout.StudentName.Should().Be("John Doe");
dropdown.Reason.Should().Contain("financial");
```

---

## Continuous Integration

### GitHub Actions Example
```yaml
name: Worker Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 8.0.x
      - name: Restore dependencies
        run: dotnet restore CETS.Worker/CETS.Worker.Tests
      - name: Build
        run: dotnet build CETS.Worker/CETS.Worker.Tests --no-restore
      - name: Test
        run: dotnet test CETS.Worker/CETS.Worker.Tests --no-build --verbosity normal
```

---

## Expected Test Results

### All Tests Passing
```
Test Run Successful.
Total tests: 14
     Passed: 14
     Failed: 0
    Skipped: 0
 Total time: 2.5 seconds
```

### Test Execution Time
- Unit Tests: ~500ms
- Integration Tests: ~1.5s
- Total: ~2.5s

---

## Troubleshooting

### Tests Fail to Build
```bash
# Restore packages
dotnet restore CETS.Worker/CETS.Worker.Tests

# Clean and rebuild
dotnet clean CETS.Worker/CETS.Worker.Tests
dotnet build CETS.Worker/CETS.Worker.Tests
```

### Tests Fail to Run
```bash
# Check test discovery
dotnet test --list-tests

# Run with detailed output
dotnet test --verbosity detailed
```

### In-Memory Database Issues
- Each test uses a unique database name (Guid)
- Database is deleted in TearDown
- No conflicts between tests

---

## Next Steps

### Add More Tests
1. **Performance Tests**: Test with 1000+ dropouts
2. **Concurrency Tests**: Test parallel processing
3. **Failure Tests**: Test database failures, network issues
4. **Notification Tests**: Test MongoDB integration

### Improve Coverage
1. Add tests for edge cases
2. Test error scenarios
3. Test logging output
4. Test configuration variations

---

## Summary

✅ **14 Test Cases** covering all scenarios  
✅ **3 Test Categories** (Unit, Worker, Integration)  
✅ **In-Memory Database** for fast, isolated tests  
✅ **Mocking** for external dependencies  
✅ **FluentAssertions** for readable assertions  
✅ **NUnit** test framework  

Run `dotnet test` to execute all tests and verify the worker is functioning correctly! 🧪


# Exception Diagnosis Runbook

## When to Use
When Application Insights shows elevated exception counts or new exception types.

## Diagnostic Steps

### 1. **Identify Exception Pattern**
- Review the **top-exceptions** evidence query
- Look for:
  - New exception types (may indicate code deployment issue)
  - Spike in existing exception type (degradation)
  - Correlation with recent deployments or config changes

### 2. **Check Exception Context**
- Extract `operation_Id` (correlation ID) from top-exceptions
- Use this to trace the full request flow through dependencies
- Check if exceptions are coming from:
  - Application code (NullReferenceException, IndexOutOfRangeException)
  - Framework/runtime (OutOfMemoryException, ThreadAbortException)
  - External calls that were not properly handled

### 3. **Correlate with Other Evidence**
- **Failed Dependencies**: Check if exception correlates with failed downstream service
- **Failed Requests**: Look for 500 errors matching exception timeline
- **Error Trends**: Confirm if this is a spike or steady baseline

### 4. **Remediation Paths**
- **Application Exception**: Code issue, likely requires deployment fix or rollback
- **Dependency Exception**: Downstream service problem, may require traffic reroute
- **Infrastructure Exception**: Memory/CPU/network issue, may require scaling

## Key Metrics to Track
- Exception count (total volume)
- Exception rate (count per minute)
- First/last seen times (incident window)
- Unique correlation IDs (how many requests affected)

---
*Slice 106: App Insights as First-Class Evidence*

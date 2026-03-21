# Dependency Failure Diagnosis Runbook

## When to Use
When Application Insights shows elevated failure rates in the **failed-dependencies** evidence query.

## Diagnostic Steps

### 1. **Identify Failing Dependencies**
- Review the **failed-dependencies** evidence query
- Look for:
  - **type**: e.g., "HTTP", "SQL", "Redis", "Service Bus"
  - **target**: e.g., "api.example.com", "mydb.database.windows.net"
  - Result codes (timeouts, 5xx, connection errors)

### 2. **Assess Failure Scope**
- **All requests to target failing**: Full downstream outage (dev team owns upstream)
- **Some requests failing**: Transient issues, circuit-breaker open, or resource exhaustion
- **Newly failing**: Config change or deployment on dependency side

### 3. **Cross-Check with Other Evidence**
- **Failed Requests**: Check if client-side 5xx errors match dependency failure timeline
- **Timeout Patterns**: Long durations may indicate dependency slowness
- **Error Trends**: Is the failure rate increasing or plateauing?

### 4. **Action Paths**

#### If Dependency is External (Third-party API, SaaS)
- Check third-party status page
- Check network connectivity (firewall rules, DNS)
- Verify API keys and authentication

#### If Dependency is Internal (Database, Cache, Service Bus)
- Check Azure Resource Health
- Monitor CPU, memory, connection pool exhaustion
- Review recent scaling or config changes
- Check Azure service logs

#### If Dependency is Critical Path (Database, Auth Service)
- Enable circuit-breaker or fallback
- Route traffic to standby replica if available
- Consider degraded mode (return cached data if possible)

## Key Signals
- **Operation_Id**: Trace this through full dependency chain
- **Result Code**: HTTP 5xx, SQL timeout, connection refused, etc.
- **Target**: Identify which instance/cluster is failing
- **Count + Rate**: Is this 1% of traffic or 50%?

---
*Slice 106: App Insights as First-Class Evidence*

# Disk Full / Low Disk Space
tags: disk, storage, capacity, disk-full

## Symptoms
- Disk utilization above 90%
- Application errors related to write failures
- Log ingestion stops or becomes intermittent
- Database transaction log full

## Diagnosis Steps

1. **Check disk utilization**: Use `df -h` (Linux) or disk management tools (Windows).
2. **Identify large files**: `du -sh /var/log/*` or similar to find space consumers.
3. **Check log rotation**: Verify logrotate or equivalent is configured and running.
4. **Review temporary files**: Look for orphaned temp files or core dumps.

## KQL Queries

```kql
// Monitor disk space trends
Perf
| where ObjectName == "LogicalDisk" and CounterName == "% Free Space"
| where CounterValue < 10
| summarize MinFreeSpace = min(CounterValue) by Computer, InstanceName, bin(TimeGenerated, 15m)
| order by MinFreeSpace asc
```

## Remediation

1. **Clear old logs**: Remove or archive log files older than retention period.
2. **Compress large files**: Gzip old log files that must be retained.
3. **Expand disk**: Increase the disk size if using cloud storage.
4. **Move data**: Relocate large datasets to a separate volume or object storage.
5. **Fix log rotation**: Ensure logrotate is properly configured with size limits.

## Prevention
- Set up alerts at 80% disk usage (warning) and 90% (critical)
- Implement automated log rotation with size-based policies
- Use separate volumes for application data and logs

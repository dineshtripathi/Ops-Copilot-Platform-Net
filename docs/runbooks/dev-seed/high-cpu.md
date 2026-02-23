# High CPU Troubleshooting
tags: cpu, performance, high-cpu, compute

## Symptoms
- CPU utilization consistently above 80% for more than 10 minutes
- Application response times degraded
- Alerts from monitoring showing high CPU on one or more nodes

## Diagnosis Steps

1. **Identify the affected resource**: Check which VM, container, or node is experiencing high CPU.
2. **Check top processes**: Use `top`, `htop`, or Task Manager to identify the process consuming the most CPU.
3. **Review recent deployments**: Check if a recent deployment introduced a regression.
4. **Check for runaway queries**: Database queries or analytics jobs can cause CPU spikes.

## KQL Queries

```kql
// Find high CPU events in the last hour
Perf
| where ObjectName == "Processor" and CounterName == "% Processor Time"
| where CounterValue > 80
| summarize AvgCpu = avg(CounterValue) by Computer, bin(TimeGenerated, 5m)
| order by AvgCpu desc
```

## Remediation

1. **Scale up**: Increase the VM size or container resource limits.
2. **Scale out**: Add more instances behind the load balancer.
3. **Optimize code**: Profile the application to find hot paths.
4. **Kill runaway process**: If a single process is the culprit, terminate it after capturing diagnostics.
5. **Restart the service**: As a last resort, restart the affected service.

## Escalation
If CPU remains high after remediation, escalate to the platform engineering team with:
- Resource ID and region
- Timeline of the issue
- Actions already taken

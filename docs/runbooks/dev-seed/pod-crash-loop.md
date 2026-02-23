# Pod CrashLoopBackOff
tags: kubernetes, pod, crash, crashloop, k8s, container

## Symptoms
- Pod status shows CrashLoopBackOff
- Container restarts increasing rapidly
- Application unavailable or degraded
- Events show Back-off restarting failed container

## Diagnosis Steps

1. **Check pod events**: `kubectl describe pod <pod-name> -n <namespace>`
2. **Review container logs**: `kubectl logs <pod-name> -n <namespace> --previous`
3. **Check resource limits**: Verify the pod isn't being OOMKilled.
4. **Inspect liveness probes**: Misconfigured probes can cause unnecessary restarts.
5. **Check image version**: Ensure the correct image tag is deployed.

## KQL Queries

```kql
// Find pods in CrashLoopBackOff
KubePodInventory
| where PodStatus == "Failed" or ContainerStatusReason == "CrashLoopBackOff"
| summarize RestartCount = max(ContainerRestartCount) by PodName, Namespace, Computer
| where RestartCount > 3
| order by RestartCount desc
```

## Common Causes

1. **Application error on startup**: Missing configuration, wrong connection strings.
2. **OOMKilled**: Container exceeds memory limits — check `kubectl describe pod` for OOMKilled reason.
3. **Liveness probe failure**: Probe path wrong, timeout too short, or app takes too long to start.
4. **Missing dependencies**: Required services (database, message queue) not available.
5. **Permission issues**: Container can't write to required paths.

## Remediation

1. **Fix the root cause** identified in diagnosis.
2. **Increase resource limits** if OOMKilled.
3. **Adjust probes**: Increase `initialDelaySeconds` and `timeoutSeconds`.
4. **Rollback**: If caused by a bad deployment, rollback to the previous version.
5. **Delete and recreate**: `kubectl delete pod <pod-name>` to force a fresh start.

## Escalation
Include the following when escalating:
- Pod describe output
- Container logs (current and previous)
- Recent deployment changes
- Cluster resource utilization

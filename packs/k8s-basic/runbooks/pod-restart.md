# Pod Restart Runbook

## Prerequisites
- kubectl access to the cluster.
- Namespace-scoped RBAC at minimum.

## Steps
1. Identify the unhealthy pod: `kubectl get pods -n <namespace>`.
2. Delete the pod to trigger a restart: `kubectl delete pod <pod-name> -n <namespace>`.
3. Wait for the replacement pod: `kubectl get pods -n <namespace> -w`.
4. Verify readiness probes pass.

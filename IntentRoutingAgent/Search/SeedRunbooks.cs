namespace IntentRoutingAgent.Search;

public static class SeedRunbooks
{
    public static IReadOnlyList<KnowledgeDocument> Create() =>
    [
        new KnowledgeDocument(
            "INC-101",
            "Redis Outage - Cache Saturation",
            "The Redis cluster became unavailable due to memory exhaustion. "
            + "Eviction was disabled, causing requests to fail. "
            + "Resolution: increase memory limits, enable eviction (LRU), and add alerting on memory usage."
        ),
        new KnowledgeDocument(
            "RUN-201",
            "Database Connection Pool Runbook",
            "If the application experiences slowdowns, check the database connection pool. "
            + "A saturated pool can block incoming requests. "
            + "Actions: increase pool size, identify connection leaks, and add metrics for pool wait time."
        ),
        new KnowledgeDocument(
            "INC-305",
            "High CPU Usage on API Nodes",
            "API servers showed sustained high CPU usage due to inefficient JSON serialization. "
            + "Actions: switch to source-generated serializers, reduce allocations, and cache hot responses."
        ),
        new KnowledgeDocument(
            "RUN-404",
            "Kubernetes Pod Restart Troubleshooting",
            "Repeated pod restarts are often caused by failing health checks or insufficient memory limits. "
            + "Actions: inspect logs, check OOMKilled events, and adjust probes and resource requests/limits."
        )
    ];
}

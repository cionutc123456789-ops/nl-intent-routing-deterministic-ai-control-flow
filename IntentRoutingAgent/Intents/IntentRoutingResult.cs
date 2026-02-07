namespace IntentRoutingAgent.Intents;

public sealed record IntentRoutingResult(
    Intent Intent,
    double Confidence,
    RoutingPath Path,
    string ResponseText
);

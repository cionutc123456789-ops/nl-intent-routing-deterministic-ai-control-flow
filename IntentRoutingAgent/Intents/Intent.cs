namespace IntentRoutingAgent.Intents;

public enum Intent
{
    Unknown = 0,
    WorldTime = 1,
    RunbookSearch = 2,
    GeneralOpsAdvice = 3
}

public enum RoutingPath
{
    RulesOnly = 0,
    RulesPlusEmbeddings = 1
}

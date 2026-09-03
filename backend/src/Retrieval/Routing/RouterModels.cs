namespace LiaraDocsAssistant.Retrieval.Routing;

public sealed record RouterRequest(string Message, string Mode, IReadOnlyList<string>? RecentMessages = null);

public sealed record RouterResult(string Scope, string? Reason, IReadOnlyList<string> SubQueries)
{
    public const string ScopeTrivial = "trivial";
    public const string ScopeOutOfScope = "out_of_scope";
    public const string ScopeInScope = "in_scope";
}

public interface IRouterService
{
    Task<RouterResult> ClassifyAsync(RouterRequest request, CancellationToken ct = default);
}

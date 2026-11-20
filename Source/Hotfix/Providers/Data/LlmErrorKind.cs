namespace Hotfix.Data;

public enum LlmErrorKind
{
    Network,
    Timeout,
    Authentication,
    RateLimited,
    Overloaded,
    InvalidRequest,
    ContextTooLong,
    Server,
    Cancelled,
    Unknown
}
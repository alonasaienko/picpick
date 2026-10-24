namespace WebApp.Infrastructure;

public abstract class ApiException(int statusCode, string errorCode, string detail,
    Dictionary<string, string[]>? errors = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}

public sealed class NotFoundException(string errorCode, string detail)
    : ApiException(404, errorCode, detail);

public sealed class ConflictException(string errorCode, string detail)
    : ApiException(409, errorCode, detail);

public sealed class DomainValidationException(string errorCode, string detail,
    Dictionary<string, string[]> errors) : ApiException(422, errorCode, detail, errors);

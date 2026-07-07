namespace CommerceOps.Application.Lumora;

public sealed record LumoraClientResult<T>(T? Data, LumoraClientError? Error)
{
    public bool IsSuccess => Error is null;

    public static LumoraClientResult<T> Success(T data) => new(data, null);

    public static LumoraClientResult<T> Failure(string code, string message, int? statusCode = null) =>
        new(default, new LumoraClientError(code, message, statusCode));
}

public sealed record LumoraClientError(string Code, string Message, int? StatusCode = null);

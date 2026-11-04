namespace VFMS.Contracts.Common;

/// <summary>
/// Generic result wrapper returned from all Application services.
/// </summary>
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Data { get; }
    public string? ErrorMessage { get; }
    public string? ErrorCode { get; }
    public int StatusCode { get; }

    private Result(bool isSuccess, T? data, string? errorMessage, string? errorCode, int statusCode)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public static Result<T> Success(T data, int statusCode = 200) =>
        new(true, data, null, null, statusCode);

    public static Result<T> Failure(string errorMessage, string errorCode = "ERROR", int statusCode = 400) =>
        new(false, default, errorMessage, errorCode, statusCode);
}

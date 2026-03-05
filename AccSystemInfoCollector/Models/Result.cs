namespace TEMS.ACC.Models;

public class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) =>
        new(true, value, null);

    public static Result<T> Failure(string error, Exception? exception = null)
    {
        var message = exception is not null ? $"{error}: {exception.Message}" : error;
        return new(false, default, message);
    }

    public static async Task<Result<T>> From(Func<Task<T>> task)
    {
        try
        {
            var value = await task();
            return Success(value);
        }
        catch (Exception ex)
        {
            return Failure("An error occurred", ex);
        }
    }
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error, Exception? exception = null) =>
        Result<T>.Failure(error, exception);
}
namespace VisionPlatform.Core
{
    /// <summary>操作结果：成功标志 + 消息。用于在各层之间传递失败原因。</summary>
    public class OpResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public OpResult() { }

        public OpResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public static OpResult Ok(string message = "OK")
        {
            return new OpResult(true, message);
        }

        public static OpResult Fail(string message)
        {
            return new OpResult(false, message);
        }

        public static OpResult Fail(string format, params object[] args)
        {
            return new OpResult(false, string.Format(format, args));
        }
    }

    /// <summary>带返回值的操作结果。</summary>
    public class OpResult<T> : OpResult
    {
        public T Value { get; set; }

        public OpResult() { }

        public OpResult(bool success, string message, T value)
        {
            Success = success;
            Message = message;
            Value = value;
        }

        public static OpResult<T> Ok(T value, string message = "OK")
        {
            return new OpResult<T>(true, message, value);
        }

        public new static OpResult<T> Fail(string message)
        {
            return new OpResult<T>(false, message, default(T));
        }
    }
}

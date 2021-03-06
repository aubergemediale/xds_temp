﻿using Newtonsoft.Json;

namespace UnnamedCoin.Bitcoin
{
    /// <summary>
    ///     A generic result type that can be serialized.
    /// </summary>
    /// <typeparam name="T">The type of the value to return if the result was successful.</typeparam>
    public sealed class SerializableResult<T>
    {
        [JsonConstructor]
        SerializableResult()
        {
        }

        SerializableResult(bool isSuccess, T value, string message)
        {
            this.IsSuccess = isSuccess;
            this.Message = message;
            this.Value = value;
        }

        [JsonProperty("isSuccess")] public bool IsSuccess { get; private set; }

        [JsonProperty("message")] public string Message { get; private set; }

        [JsonProperty("value")] public T Value { get; private set; }

        public static SerializableResult<T> Ok(T value, string message = null)
        {
            return new SerializableResult<T>(true, value, message);
        }

        public static SerializableResult<T> Fail(string message)
        {
            return new SerializableResult<T>(false, default, message);
        }
    }
}
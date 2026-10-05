using System;

namespace DuraIT.FastBinaryJson
{
    /// <summary>
    /// The exception thrown when a payload cannot be read or written, or a type it names cannot be created.
    /// </summary>
    public sealed class BjsonException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BjsonException"/> class.
        /// </summary>
        public BjsonException() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="BjsonException"/> class with a message.
        /// </summary>
        /// <param name="message">The message that describes the failure.</param>
        public BjsonException(string message)
            : base(message) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="BjsonException"/> class with a message and the exception that caused it.
        /// </summary>
        /// <param name="message">The message that describes the failure.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public BjsonException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}

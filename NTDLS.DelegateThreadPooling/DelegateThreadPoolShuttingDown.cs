namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Thrown when the thread pool is being shutdown.
    /// </summary>
    public class DelegateThreadPoolShuttingDown : Exception
    {
        /// <summary>
        /// Instantiates a new exception.
        /// </summary>
        public DelegateThreadPoolShuttingDown()
        {
        }

        /// <summary>
        /// Instantiates a new exception with a message.
        /// </summary>
        /// <param name="message"></param>
        public DelegateThreadPoolShuttingDown(string message)
            : base(message)
        {
        }
    }
}

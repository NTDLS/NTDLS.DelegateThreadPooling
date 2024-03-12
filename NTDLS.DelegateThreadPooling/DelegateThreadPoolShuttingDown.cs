namespace NTDLS.DelegateThreadPooling
{
    /// <summary>
    /// Thrown when the thread pool is being shutdown.
    /// </summary>
    public class DelegateThreadPoolShuttingDown: Exception
    {
        /// <summary>
        /// Instanciates a new exception.
        /// </summary>
        public DelegateThreadPoolShuttingDown()
        {
        }

        /// <summary>
        /// Instanciates a new exception with a message.
        /// </summary>
        /// <param name="message"></param>
        public DelegateThreadPoolShuttingDown(string message)
            :base (message)
        {
        }
    }
}

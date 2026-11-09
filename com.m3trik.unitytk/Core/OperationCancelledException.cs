using System;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Thrown at a checkpoint once the run was cancelled (an <c>OpContext</c> check,
    /// or a compile still waiting): the operation stops there, its outcome
    /// <c>cancelled</c>. pythontk's <c>OperationCancelled</c>.
    /// </summary>
    public sealed class OperationCancelledException : Exception
    {
        /// <param name="reason">Why the run stopped.</param>
        public OperationCancelledException(string reason) : base(reason) { }
    }
}

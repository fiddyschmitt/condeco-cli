using System;

namespace libCondeco.Extensions
{
    public static class ExceptionExtensions
    {
        //A blocking .Result / .Wait() wraps the real exception in an AggregateException (sometimes several
        //layers deep). Peel those away so callers can classify or display the underlying cause - "A task was
        //canceled.", a 401, an HttpRequestException - instead of the generic "One or more errors occurred."
        //wrapper. A non-AggregateException (or one with no inner) is returned unchanged.
        public static Exception Unwrap(this Exception exception)
        {
            var innermost = exception;
            while (innermost is AggregateException aggregate && aggregate.InnerException != null)
            {
                innermost = aggregate.InnerException;
            }

            return innermost;
        }
    }
}

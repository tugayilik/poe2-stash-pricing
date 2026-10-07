using System;

namespace PoeStashPricer
{
    // Preserve the original recovery waits. Shorter waits need controlled game testing
    // with actual first-pass misses; arithmetic tests alone cannot prove recovery.
    public static class RetryTiming
    {
        public static int HoverDelay(int configured, bool likely)
        {
            return likely ? Math.Max(90, configured * 2) : configured;
        }

        public static int CopyTimeout(int configured, int measuredTimeout, bool measured, bool likely)
        {
            return measured ? Math.Max(120, measuredTimeout * 2)
                : likely ? Math.Max(250, configured) : configured;
        }
    }
}

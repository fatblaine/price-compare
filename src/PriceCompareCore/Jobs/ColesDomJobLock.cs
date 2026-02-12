using System.Threading;

namespace PriceCompareCore.Jobs
{
    internal static class ColesDomJobLock
    {
        public static readonly SemaphoreSlim Gate = new(1, 1);
    }
}

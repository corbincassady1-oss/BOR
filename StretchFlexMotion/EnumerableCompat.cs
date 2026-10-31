using System;
using System.Collections.Generic;

namespace StretchFlexMotion
{
    internal static class EnumerableCompat
    {
        public static T FirstOrDefault<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (var item in source)
                if (predicate(item)) return item;
            return default(T);
        }
    }
}

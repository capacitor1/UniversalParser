using System.Collections.Immutable;

namespace UniversalParser.Core.Model;

/// <summary>Shared argument checks for the immutable collections used by the model types.</summary>
internal static class CollectionGuard
{
    public static void ThrowIfAnyNull<T>(this ImmutableArray<T> items, string paramName, string message)
        where T : class
    {
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException(message, paramName);
            }
        }
    }
}

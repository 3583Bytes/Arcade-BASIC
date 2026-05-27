// Compiler-feature polyfills for targets that don't ship them in the BCL.
// Included into every non-net9 build via Directory.Build.props. All types are
// `internal` so they never become part of the public surface.

#if !NET5_0_OR_GREATER

namespace System.Collections.Generic
{
    // Reference-keyed dictionary comparer. Built into the BCL from .NET 5 onward;
    // shipped here for older targets so analyzer side-tables keyed by AST nodes
    // compile the same on every framework.
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

namespace System.Runtime.CompilerServices
{
    // Required to use `init` accessors and `record` types.
    internal static class IsExternalInit { }

    // Required to use the `required` keyword (C# 11) on members.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field
        | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    // Marks an API as requiring a particular compiler feature. Used by the
    // compiler to fail-fast when consumed by an older compiler that doesn't
    // recognise the feature.
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; init; }

        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    // Lets a constructor signal that it initialises all `required` members so
    // callers don't have to re-set them.
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif

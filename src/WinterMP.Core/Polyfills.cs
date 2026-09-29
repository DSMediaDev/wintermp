// Compiler-recognised attributes that .NET 3.5 does not ship. The compiler matches them by name, so
// internal copies are enough. Only compiled for the net35 target; modern targets use the real ones.
#if NET35
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif

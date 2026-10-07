using System.Runtime.CompilerServices;

// Bypass access checks to game assembly internals
[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("Unity.TextMeshPro")]
[assembly: IgnoresAccessChecksTo("DOTween")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }
        public string AssemblyName { get; }
    }
}
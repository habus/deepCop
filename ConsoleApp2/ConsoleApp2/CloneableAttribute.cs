using System;

namespace ConsoleApp2
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    internal sealed class CloneableAttribute : Attribute
    {
        public CloningMode Mode { get; }

        public CloneableAttribute(CloningMode mode)
        {
            Mode = mode;
        }
    }
}

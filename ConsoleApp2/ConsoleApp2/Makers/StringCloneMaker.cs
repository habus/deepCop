using System;

namespace ConsoleApp2
{
    internal class StringCloneMaker : CloneMaker
    {
        protected override T CloneInternal<T>(T source, CloningMode mode)
        {
            if (source is string str)
            {
                switch (mode)
                {
                    case CloningMode.Deep:
                        return (T)(object)string.Copy(str);
                    case CloningMode.Shallow:
                        return source;
                    default:
                        throw new ArgumentException("Deep or shallow mode should be defined.", nameof(mode));
                }
            }
            throw new ArgumentException("Parameter should be string", nameof(source));
        }
    }
}

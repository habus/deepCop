namespace ConsoleApp2
{
    internal abstract class CloneMaker : ICloneMaker
    {
        public T Clone<T>(T source, CloningMode mode)
        {
            if (source == null) return source;
            return CloneInternal(source, mode);
        }
        protected abstract T CloneInternal<T>(T source, CloningMode mode);
    }
}

namespace ConsoleApp2
{
    /// <summary>
    /// Return new copy for value types and reference copy for reference type.
    /// </summary>
    internal class DefaultCloneMaker : CloneMaker
    {
        protected override T CloneInternal<T>(T source, CloningMode mode)
        {
            return source;
        }
    }
}

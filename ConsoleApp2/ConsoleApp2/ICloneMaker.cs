namespace ConsoleApp2
{
    internal interface ICloneMaker
    {
        T Clone<T>(T source, CloningMode mode);
    }
}

namespace ConsoleApp2
{
    public interface ICloningService
    {
        T Clone<T>(T source);
    }
}

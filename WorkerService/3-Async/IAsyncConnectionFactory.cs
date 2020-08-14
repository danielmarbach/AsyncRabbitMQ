namespace WorkerService
{
    public interface IAsyncConnectionFactory
    {
        bool DispatchConsumersAsync { get; set; }
    }
}
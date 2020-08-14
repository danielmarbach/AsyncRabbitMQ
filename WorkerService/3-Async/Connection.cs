using WorkerService.Start;

namespace WorkerService.Async
{
    public class Connection
    {
        public Connection(IConnectionFactory factory)
        {
            var asyncConnectionFactory = factory as IAsyncConnectionFactory;
            if (asyncConnectionFactory != null && asyncConnectionFactory.DispatchConsumersAsync)
            {
                ConsumerWorkService = new AsyncConsumerWorkService();
            }
            else
            {
                ConsumerWorkService = new ConsumerWorkService();
            }
        }

        internal ConsumerWorkService ConsumerWorkService { get; }
    }
}
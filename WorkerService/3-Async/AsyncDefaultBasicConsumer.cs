using System.Threading.Tasks;

namespace WorkerService
{
    public class AsyncDefaultBasicConsumer /*: IBasicConsumer, IAsyncBasicConsumer*/
    {
        public AsyncDefaultBasicConsumer()
        {
            //ShutdownReason = null;
            Model = null;
            IsRunning = false;
            ConsumerTag = null;
        }

        public AsyncDefaultBasicConsumer(IModel model)
        {
            //ShutdownReason = null;
            IsRunning = false;
            ConsumerTag = null;
            Model = model;
        }

        public string ConsumerTag { get; set; }

        public bool IsRunning { get; protected set; }

        public IModel Model { get; set; }

        public virtual Task HandleBasicCancel(string consumerTag)
        {
            return TaskExtensions.CompletedTask;
        }

        public virtual Task HandleBasicCancelOk(string consumerTag)
        {
            return TaskExtensions.CompletedTask;
        }

        public virtual Task HandleBasicConsumeOk(string consumerTag)
        {
            ConsumerTag = consumerTag;
            IsRunning = true;
            return TaskExtensions.CompletedTask;
        }

        // rest omitted
    }
}
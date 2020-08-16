using System;
using System.Threading.Tasks;

namespace WorkerService
{
    public class AsyncEventingBasicConsumer : AsyncDefaultBasicConsumer
    {
        public AsyncEventingBasicConsumer(IModel model) : base(model)
        {
        }

        // public event AsyncEventHandler<BasicDeliverEventArgs> Received;

        // public override async Task HandleBasicDeliver(string consumerTag,
        //     ulong deliveryTag,
        //     bool redelivered,
        //     string exchange,
        //     string routingKey,
        //     IBasicProperties properties,
        //     byte[] body)
        // {
        //     await base.HandleBasicDeliver(consumerTag,
        //         deliveryTag,
        //         redelivered,
        //         exchange,
        //         routingKey,
        //         properties,
        //         body).ConfigureAwait(false);
        //     await Raise(Received, new BasicDeliverEventArgs(consumerTag,
        //         deliveryTag,
        //         redelivered,
        //         exchange,
        //         routingKey,
        //         properties,
        //         body)).ConfigureAwait(false);
        // }

        private Task Raise<TEvent>(AsyncEventHandler<TEvent> eventHandler, TEvent evt)
            where TEvent : EventArgs
        {
            var handler = eventHandler;
            if (handler != null)
            {
                return handler(this, evt);
            }
            return TaskExtensions.CompletedTask;
        }
    }
}
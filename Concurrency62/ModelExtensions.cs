using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace Concurrency
{
    static class ModelExtensions
    {
        sealed class MessageState
        {
            public MessageState(IModel channel, ulong deliveryTag)
            {
                Channel = channel;
                DeliveryTag = deliveryTag;
            }
            public IModel Channel { get; }

            public ulong DeliveryTag { get; }
        }

        public static Task BasicAckSingle(this IModel channel, ulong deliveryTag)
        {
            return Task.Factory.StartNew(state =>
                {
                    var messageState = (MessageState) state;
                    messageState.Channel.BasicAck(messageState.DeliveryTag, false);
                }, new MessageState(channel, deliveryTag), CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }
    }
}
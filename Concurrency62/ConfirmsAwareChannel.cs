using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Concurrency
{
    sealed class ConfirmsAwareChannel : IDisposable
    {
        public ConfirmsAwareChannel(IConnection connection)
        {
            channel = connection.CreateModel();
            channel.BasicAcks += Channel_BasicAcks;
            channel.BasicNacks += Channel_BasicNacks;
            channel.BasicReturn += Channel_BasicReturn;
            channel.ModelShutdown += Channel_ModelShutdown;

            channel.ConfirmSelect();

            messages = new ConcurrentDictionary<ulong, TaskCompletionSource<bool>>();
        }

        public IBasicProperties CreateBasicProperties()
        {
            var properties = channel.CreateBasicProperties();
            properties.Headers = new Dictionary<string, object>();
            return properties;
        }

        public bool IsOpen => channel.IsOpen;

        public bool IsClosed => channel.IsClosed;

        public Task SendMessage(string address, string message, IBasicProperties properties)
        {
            try
            {
                var task = GetConfirmationTask();
                properties.SetConfirmationId(channel.NextPublishSeqNo);

                channel.BasicPublish(address, string.Empty, true, properties, Encoding.UTF8.GetBytes(message));
                return task;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }


        }

        private Task GetConfirmationTask()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var added = messages.TryAdd(channel.NextPublishSeqNo, tcs);

            if (!added)
            {
                throw new Exception($"Cannot publish a message with sequence number '{channel.NextPublishSeqNo}' on this channel. A message was already published on this channel with the same confirmation number.");
            }

            return tcs.Task;
        }

        void Channel_BasicAcks(object sender, BasicAckEventArgs e)
        {
            if (!e.Multiple)
            {
                SetResult(e.DeliveryTag);
            }
            else
            {
                foreach (var message in messages)
                {
                    if (message.Key <= e.DeliveryTag)
                    {
                        SetResult(message.Key);
                    }
                }
            }
        }

        void Channel_BasicNacks(object sender, BasicNackEventArgs e)
        {
            if (!e.Multiple)
            {
                SetException(e.DeliveryTag, "Message rejected by broker.");
            }
            else
            {
                foreach (var message in messages)
                {
                    if (message.Key <= e.DeliveryTag)
                    {
                        SetException(message.Key, "Message rejected by broker.");
                    }
                }
            }
        }

        void Channel_BasicReturn(object sender, BasicReturnEventArgs e)
        {
            var message = $"Message could not be routed to {e.Exchange + e.RoutingKey}: {e.ReplyCode} {e.ReplyText}";

            if (e.BasicProperties.TryGetConfirmationId(out var deliveryTag))
            {
                SetException(deliveryTag, message);
            }
            else
            {
                // omit demo
            }
        }

        void Channel_ModelShutdown(object sender, ShutdownEventArgs e)
        {
            do
            {
                foreach (var message in messages)
                {
                    SetException(message.Key, $"Channel has been closed: {e}");
                }
            } while (!messages.IsEmpty);
        }

        void SetResult(ulong key)
        {
            if (messages.TryRemove(key, out var tcs))
            {
                tcs.SetResult(true);
            }
        }

        void SetException(ulong key, string exceptionMessage)
        {
            if (messages.TryRemove(key, out var tcs))
            {
                tcs.SetException(new Exception(exceptionMessage));
            }
        }

        public void Dispose()
        {
            channel?.Dispose();
        }

        IModel channel;

        readonly ConcurrentDictionary<ulong, TaskCompletionSource<bool>> messages;

    }
}
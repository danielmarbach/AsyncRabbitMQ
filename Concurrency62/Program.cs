using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Concurrency
{
    class Program
    {
        private const string InputQueue = "concurrency-62";
        private const string ConsumerTag = "concurrency-62";

        static async Task Main(string[] args)
        {
            var connectionFactory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5672,
                UseBackgroundThreadsForIO = true,
                DispatchConsumersAsync = true,
                ConsumerDispatchConcurrency = 4
            };

            #region TopologyAndSending

            CreateTopologyIfNecessary(InputQueue, connectionFactory);

            var cts = new CancellationTokenSource();
            var sendConnection = connectionFactory.CreateConnection($"{InputQueue} sender");
            var senderChannel = new ConfirmsAwareChannel(sendConnection);
            var sendMessagesTask = Task.Run(() => SendMessages(senderChannel, InputQueue, cts.Token), CancellationToken.None);

            var receiveConnection = connectionFactory.CreateConnection($"{InputQueue} pump");
            var receiveModel = receiveConnection.CreateModel();

            #endregion

            receiveModel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new AsyncEventingBasicConsumer(receiveModel);

            #region NotRelevant

            consumer.Registered += Consumer_Registered;
            receiveConnection.ConnectionShutdown += Connection_ConnectionShutdown;

            #endregion

            consumer.Received += (sender,
                deliverEventArgs) => Consumer_Received(deliverEventArgs,
                receiveModel,
                cts.Token);

            receiveModel.BasicConsume(InputQueue, false, ConsumerTag, consumer);

            #region Stop

            await Console.Error.WriteLineAsync("Press any key to stop");
            Console.ReadLine();
            await Console.Error.WriteLineAsync("Shutting down");

            cts.Cancel();

            try
            {
                await sendMessagesTask;
            }
            catch (OperationCanceledException)
            {
            }

            receiveModel.Close();
            receiveConnection.Close();
            senderChannel.Dispose();
            sendConnection.Close();

            #endregion
        }

        private static async Task Consumer_Received(
            BasicDeliverEventArgs e,
            IModel receiveModel,
            CancellationToken cancellationToken)
        {
            await Console.Out.WriteLineAsync($"v: {Encoding.UTF8.GetString(e.Body.Span)} / q: {receiveModel.MessageCount(InputQueue)}");

            await Task.Delay(200, cancellationToken);

            await receiveModel.BasicAckSingle(e.DeliveryTag);
        }

        private static async Task SendMessages(ConfirmsAwareChannel senderChannel, string inputQueue,
            CancellationToken cancellationToken)
        {
            var messageNumber = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var properties = senderChannel.CreateBasicProperties();
                await senderChannel.SendMessage(inputQueue, messageNumber++.ToString(), properties);
                await Task.Delay(100, cancellationToken);
            }
        }

        #region NotRelevant

        private static Task Consumer_Registered(object? sender, ConsumerEventArgs e)
        {
            Console.Error.WriteLine($"Consumer(s) '{string.Join(" ", e.ConsumerTags)}' registered");
            return Task.CompletedTask;
        }

        private static void Connection_ConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            Console.Error.WriteLine("Shutdown");
        }

        private static void CreateTopologyIfNecessary(string queue, IConnectionFactory connectionFactory)
        {
            var adminConnection = connectionFactory.CreateConnection("Admin connection");
            var channel = adminConnection.CreateModel();
            channel.QueueDeclare(queue, durable: true, false, false, null);
            CreateExchange(queue);
            channel.QueueBind(queue, queue, string.Empty);
            channel.QueuePurge(queue);
            channel.Close();
            adminConnection.Close();

            void CreateExchange(string exchangeName)
            {
                try
                {
                    channel.ExchangeDeclare(exchangeName, ExchangeType.Fanout, durable: true);
                }
                // ReSharper disable EmptyGeneralCatchClause
                catch (Exception)
                    // ReSharper restore EmptyGeneralCatchClause
                {
                    // TODO: Any better way to make this idempotent?
                }
            }
        }

        #endregion
    }
}

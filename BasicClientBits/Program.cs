using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BasicClientBits
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var inputQueue = "basic-bits";
            var consumerTag = "basic-bits";

            var connectionFactory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5672,
                UseBackgroundThreadsForIO = true
            };

            #region TopologyAndSending

            CreateTopologyIfNecessary(inputQueue, connectionFactory);

            var cts = new CancellationTokenSource();
            var sendConnection = connectionFactory.CreateConnection($"{inputQueue} sender");
            var senderChannel = new ConfirmsAwareChannel(sendConnection);
            var sendMessagesTask = Task.Run(() => SendMessages(cts, senderChannel, inputQueue), CancellationToken.None);

            var receiveConnection = connectionFactory.CreateConnection($"{inputQueue} pump");
            var receiveChannel = receiveConnection.CreateModel();

            #endregion

            receiveChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new EventingBasicConsumer(receiveChannel);

            consumer.Registered += Consumer_Registered;
            receiveConnection.ConnectionShutdown += Connection_ConnectionShutdown;

            consumer.Received += (sender, args) => Consumer_Received(sender, args, receiveChannel);

            receiveChannel.BasicConsume(inputQueue, false, consumerTag, consumer);

            #region Stop

            await Console.Error.WriteLineAsync("Press any key to stop");
            Console.ReadLine();

            cts.Cancel();

            await sendMessagesTask;

            receiveChannel.Close();
            receiveConnection.Close();
            senderChannel.Dispose();
            sendConnection.Close();

            #endregion
        }

        private static async Task SendMessages(CancellationTokenSource cts, ConfirmsAwareChannel senderChannel,
            string inputQueue)
        {
            var messageNumber = 0;
            while (!cts.IsCancellationRequested)
            {
                var properties = senderChannel.CreateBasicProperties();
                await senderChannel.SendMessage(inputQueue, messageNumber++.ToString(), properties);
                await Task.Delay(1000);
            }
        }

        private static void Consumer_Received(object? sender, BasicDeliverEventArgs e, IModel channel)
        {
            Console.Out.Write($"{Encoding.UTF8.GetString(e.Body.Span)} ");

            channel.BasicAckSingle(e.DeliveryTag, TaskScheduler.Default);
        }

        #region NotRelevant

        private static void Consumer_Registered(object? sender, ConsumerEventArgs e)
        {
            Console.Error.WriteLine($"Consumer(s) '{string.Join(" ", e.ConsumerTags)}' registered");
        }

        private static void Connection_ConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            Console.Error.WriteLine("Shutdown");
        }

        private static void CreateTopologyIfNecessary(string queue, ConnectionFactory connectionFactory)
        {
            var adminConnection = connectionFactory.CreateConnection("Admin connection");
            var channel = adminConnection.CreateModel();
            channel.QueueDeclare(queue, durable: true, false, false, null);
            CreateExchange(queue);
            channel.QueueBind(queue, queue, string.Empty);
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

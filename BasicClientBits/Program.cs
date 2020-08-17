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
        private const string InputQueue = "basic-bits";
        private const string ConsumerTag = "basic-bits";

        static async Task Main(string[] args)
        {
            var connectionFactory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5672,
                UseBackgroundThreadsForIO = true
            };

            #region TopologyAndSending

            CreateTopologyIfNecessary(InputQueue, connectionFactory);

            var cts = new CancellationTokenSource();
            var sendConnection = connectionFactory.CreateConnection($"{InputQueue} sender");
            var senderChannel = new ConfirmsAwareChannel(sendConnection);
            var sendMessagesTask = Task.Run(() => SendMessages(cts, senderChannel, InputQueue), CancellationToken.None);

            var receiveConnection = connectionFactory.CreateConnection($"{InputQueue} pump");
            var receiveChannel = receiveConnection.CreateModel();

            #endregion

            receiveChannel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

            var consumer = new EventingBasicConsumer(receiveChannel);

            #region NotRelevant

            consumer.Registered += Consumer_Registered;
            receiveConnection.ConnectionShutdown += Connection_ConnectionShutdown;

            #endregion

            consumer.Received += (sender, args) => Consumer_Received(sender, args, receiveChannel);

            receiveChannel.BasicConsume(InputQueue, false, ConsumerTag, consumer);

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

        private static void Consumer_Received(object? sender, BasicDeliverEventArgs e, IModel channel)
        {
            Console.Out.WriteLine($"v: {Encoding.UTF8.GetString(e.Body.Span)} / q: {channel.MessageCount(InputQueue)}");

            Thread.Sleep(1000);

            channel.BasicAck(e.DeliveryTag, false);
        }

        private static async Task SendMessages(CancellationTokenSource cts, ConfirmsAwareChannel senderChannel,
            string inputQueue)
        {
            var messageNumber = 0;
            while (!cts.IsCancellationRequested)
            {
                var properties = senderChannel.CreateBasicProperties();
                await senderChannel.SendMessage(inputQueue, messageNumber++.ToString(), properties);
                await Task.Delay(450);
            }
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

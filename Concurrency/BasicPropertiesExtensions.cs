using System;
using System.Text;
using RabbitMQ.Client;

namespace Concurrency
{
    public static class BasicPropertiesExtensions
    {
        public const string ConfirmationIdHeader = "RabbitMQ.ConfirmationId";

        public static void SetConfirmationId(this IBasicProperties properties, ulong confirmationId)
        {
            properties.Headers[ConfirmationIdHeader] = confirmationId.ToString();
        }

        public static bool TryGetConfirmationId(this IBasicProperties properties, out ulong confirmationId)
        {
            confirmationId = 0;

            return properties.Headers.TryGetValue(ConfirmationIdHeader, out var value) &&
                   ulong.TryParse(Encoding.UTF8.GetString(value as byte[] ?? Array.Empty<byte>()), out confirmationId);
        }
    }
}
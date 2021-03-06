﻿using System;
using System.Threading.Tasks;
using AccidentalFish.ApplicationSupport.Core.Queues;
using Microsoft.ServiceBus.Messaging;

namespace AccidentalFish.ApplicationSupport.Azure.Queues
{
    internal class AsynchronousBrokeredMessageQueue<T> : IAsynchronousQueue<T> where T : class
    {
        private readonly IQueueSerializer _queueSerializer;
        private readonly QueueClient _client;

        public AsynchronousBrokeredMessageQueue(
            IQueueSerializer queueSerializer,
            string connectionString,
            string queueName)
        {
            _queueSerializer = queueSerializer;
            _client = QueueClient.CreateFromConnectionString(connectionString, queueName);
        }

        public async Task EnqueueAsync(T item)
        {
            string value = _queueSerializer.Serialize(item);
            BrokeredMessage message = new BrokeredMessage(value);
            await _client.SendAsync(message);
        }

        public async Task EnqueueAsync(T item, TimeSpan initialVisibilityDelay)
        {
            string value = _queueSerializer.Serialize(item);
            BrokeredMessage message = new BrokeredMessage(value);
            message.ScheduledEnqueueTimeUtc = DateTimeOffset.UtcNow.Add(initialVisibilityDelay).DateTime;
            await _client.SendAsync(message);
        }

        public async Task DequeueAsync(Func<IQueueItem<T>, Task<bool>> process)
        {
            BrokeredMessage message = await _client.ReceiveAsync();
            
            if (message != null)
            {
                try
                {
                    string body = message.GetBody<string>();
                    T payload = _queueSerializer.Deserialize<T>(body);
                    BrokeredMessageQueueItem<T> queueItem = new BrokeredMessageQueueItem<T>(message, payload, message.DeliveryCount, null);
                    bool markComplete = await process(queueItem);
                    if (markComplete)
                    {
                        await message.CompleteAsync();
                    }
                    else
                    {
                        await message.AbandonAsync();
                    }
                }
                catch (Exception)
                {
                    message.Abandon();
                }
            }
            else
            {
                // we pass null into the process function as it may still want to take action based on their being no message.
                await process(null);
            }
        }

        public async Task ExtendLeaseAsync(IQueueItem<T> queueItem, TimeSpan visibilityTimeout)
        {
            BrokeredMessageQueueItem<T> brokeredMessageQueueItem = queueItem as BrokeredMessageQueueItem<T>;
            if (brokeredMessageQueueItem != null)
            {
                await brokeredMessageQueueItem.Message.RenewLockAsync();
            }
            else
            {
                throw new InvalidOperationException("Not a brokered message queue item");
            }
        }
    }
}

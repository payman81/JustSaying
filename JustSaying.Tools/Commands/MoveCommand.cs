using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using JustSaying.AwsTools;
using JustSaying.AwsTools.MessageHandling;
using Microsoft.Extensions.Logging;

namespace JustSaying.Tools.Commands
{
    public class MoveCommand : ICommand
    {
        public MoveCommand(string sourceQueueName, string destinationQueueName, string region, int count)
        {
            SourceQueueName = sourceQueueName;
            DestinationQueueName = destinationQueueName;
            Region = region;
            Count = count;
        }

        public string SourceQueueName { get; set; }
        public string DestinationQueueName { get; set; }
        public string Region { get; set; }
        public int Count { get; set; }

        public bool Execute()
        {
            Console.WriteLine($"Moving {Count} messages from {SourceQueueName} to {DestinationQueueName} in {Region}.");
            var loggerFactory = new LoggerFactory();

            var config = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(Region) };
            var client = new DefaultAwsClientFactory().GetSqsClient(config.RegionEndpoint);
            var sourceQueue = new SqsQueueByName(config.RegionEndpoint, SourceQueueName, client, JustSayingConstants.DefaultHandlerRetryCount, loggerFactory);
            var destinationQueue = new SqsQueueByName(config.RegionEndpoint, DestinationQueueName, client, JustSayingConstants.DefaultHandlerRetryCount, loggerFactory);

            EnsureQueueExistsAsync(sourceQueue).GetAwaiter().GetResult();
            EnsureQueueExistsAsync(destinationQueue).GetAwaiter().GetResult();

            var messages = PopMessagesFromSourceQueue(sourceQueue);
            var receiptHandles = messages.ToDictionary(m => m.MessageId, m => m.ReceiptHandle);

            var sendResponse = destinationQueue.Client.SendMessageBatch(new SendMessageBatchRequest
            {
                QueueUrl = destinationQueue.Uri.AbsoluteUri,
                Entries = messages.Select(x => new SendMessageBatchRequestEntry { Id = x.MessageId, MessageBody = x.Body }).ToList()
            });

            sourceQueue.Client.DeleteMessageBatch(new DeleteMessageBatchRequest
            {
                QueueUrl = sourceQueue.Uri.AbsoluteUri,
                Entries = sendResponse.Successful.Select(x => new DeleteMessageBatchRequestEntry
                {
                    Id = x.Id,
                    ReceiptHandle = receiptHandles[x.Id]
                }).ToList()
            });

            Console.WriteLine($"Moved {sendResponse.Successful.Count} messages from {SourceQueueName} to {DestinationQueueName} in {Region}.");

            return true;
        }

        private async Task EnsureQueueExistsAsync(SqsQueueByName queue)
        {
            if (!await queue.ExistsAsync())
            {
                throw new InvalidOperationException($"{queue.QueueName} does not exist.");
            }
        }

        private List<Message> PopMessagesFromSourceQueue(SqsQueueByName sourceQueue)
        {
            var messages = new List<Message>();
            ReceiveMessageResponse receiveResponse;
            do
            {
                receiveResponse = sourceQueue.Client.ReceiveMessage(new ReceiveMessageRequest
                {
                    QueueUrl = sourceQueue.Uri.AbsoluteUri,
                    MaxNumberOfMessages = Count
                });
                messages.AddRange(receiveResponse.Messages);
            } while (messages.Count < Count && receiveResponse.Messages.Any());


            return messages;
        }
    }
}

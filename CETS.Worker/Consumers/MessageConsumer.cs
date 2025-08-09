using DTOs;
using MassTransit;
using Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Consumers
{
    public class MessageConsumer : IConsumer<Message>
    {
        private readonly IMessageService _messageService;
        private readonly ILogger<MessageConsumer> _logger;
        public MessageConsumer(IMessageService messageService, ILogger<MessageConsumer> logger)
        {
            _messageService = messageService;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<Message> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            Message message = context.Message;

            _logger.LogInformation($"Received message: {message.message}");
            await _messageService.SendMessageAsync(message);

        }
    }
}

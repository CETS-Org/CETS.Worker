using Application.Implementations;
using DTOs;
using DTOs.Message.Requests;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace CETS.Worker.Consumers.Message
{
    public class CreateMessageConsumer : IConsumer<CreateMessageRequest>
    {
        private readonly IMessageService _messageService;
        private readonly ILogger<CreateMessageConsumer> _logger;
        public CreateMessageConsumer(IMessageService messageService, ILogger<CreateMessageConsumer> logger)
        {
            _messageService = messageService;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<CreateMessageRequest> context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            CreateMessageRequest message = context.Message;

            _logger.LogInformation($"Received message: {message.message}");
            await _messageService.SendMessageAsync(message);

        }
    }
}

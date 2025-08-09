using CETS.Worker.Consumers;
using MassTransit;
using Services.Implementations;
using Services.Interfaces;

namespace CETS.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddHostedService<Worker>();
            builder.Services.AddScoped<IMessageService, MessageService>();


            var rabbitMqSettings = builder.Configuration.GetSection("RabbitMq");

            builder.Services.AddMassTransit(x =>
            {
                x.AddConsumer<MessageConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(
                        rabbitMqSettings["Host"],
                        rabbitMqSettings["VirtualHost"],
                        h =>
                        {
                            h.Username(rabbitMqSettings["Username"]);
                            h.Password(rabbitMqSettings["Password"]);
                        });
                    cfg.ConfigureEndpoints(context);
                });
            });


            var host = builder.Build();
            host.Run();
        }
    }
}
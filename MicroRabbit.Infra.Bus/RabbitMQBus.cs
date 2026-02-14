
using System.Text;
using MediatR;
using MicroRabbit.Domain.Core.Bus;
using MicroRabbit.Domain.Core.Commands;
using MicroRabbit.Domain.Core.Events;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MicroRabbit.Infra.Bus
{
    public sealed class RabbitMQBus : IEventsBus
    {   
        private readonly RabbitMQSettings _settings;
        private readonly IMediator _mediator;
        private readonly Dictionary<String, List<Type>> _handlers;
        private readonly List<Type> _eventTypes;

        public RabbitMQBus(IMediator mediator, IOptions<RabbitMQSettings> settings)
        {
            _mediator = mediator;
            _handlers = new Dictionary<string, List<Type>>();
            _eventTypes = new List<Type>();
            _settings = settings.Value;
        }

        public void Publish<T>(T @event) where T : Event
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                UserName = _settings.UserName,
                Password = _settings.Password
            };

            using var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            using var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();
            { 
                var eventName = @event.GetType().Name;
                channel.QueueDeclareAsync(eventName, false, false, false, null);
                var message = JsonConvert.SerializeObject(@event);

                var body = Encoding.UTF8.GetBytes(message);
                // Crear propiedades básicas válidas para RabbitMQ
                var properties = new RabbitMQ.Client.BasicProperties
                {
                    ContentType = "application/json"
                };

                // Usar el tipo correcto para BasicPublishAsync
                channel.BasicPublishAsync(
                    exchange: "",
                    routingKey: eventName,
                    mandatory: false,
                    basicProperties: properties,
                    body: body
                ).GetAwaiter().GetResult();
            }
        }

        public Task SendCommand<T>(T command) where T : Command
        {
           return _mediator.Send(command);
        }

        public void Subscribe<T, TH>()
            where T : Event
            where TH : IEventHandler<T>
        {
            var eventName = typeof(T).Name;
            var handlerType = typeof(TH);
            
            if(!_eventTypes.Contains(typeof(T)))
            {
                _eventTypes.Add(typeof(T));
            }

            if(!_handlers.ContainsKey(eventName))
            {
                _handlers.Add(eventName, new List<Type>());
            }

            if(_handlers[eventName].Any(s => s.GetType() == handlerType))
            {
                throw new ArgumentException($"El handler exception {handlerType.Name} ya fue registrado anteriormente por'{eventName}'", nameof(handlerType));
            }

            _handlers[eventName].Add(handlerType);
            StartBasicConsumer<T>();
        }

        private void StartBasicConsumer<T>() where T : Event
        { 
            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                UserName = _settings.UserName,
                Password = _settings.Password,
               //DispatchConsumersAsync = true
            };

            var connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            var channel = connection.CreateChannelAsync().GetAwaiter().GetResult();

            var eventName = typeof(T).Name;

            channel.QueueDeclareAsync(eventName, false, false, false, null).GetAwaiter().GetResult();

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += consumer_Received;

            channel.BasicConsumeAsync(eventName, true, consumer).GetAwaiter().GetResult();
        }

        private async Task consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var eventName = e.RoutingKey;
            var message = Encoding.UTF8.GetString(e.Body.Span);

            try 
            {
               await ProcessEvent(eventName, message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Manejar la excepción de manera adecuada, por ejemplo, registrándola o reintentando
                Console.WriteLine($"Error al procesar el mensaje: {ex.Message}");
            }
        }

        private async Task ProcessEvent(string eventName, string message)
        {
            if (_handlers.ContainsKey(eventName))
            { 
                var subscriptions = _handlers[eventName];

                foreach (var subscription in subscriptions) 
                {
                    var handler = Activator.CreateInstance(subscription);

                    if (handler == null) continue;

                    var eventType = _eventTypes.SingleOrDefault(t => t.Name == eventName);

                    var @event = JsonConvert.DeserializeObject(message, eventType);

                    var concreteType = typeof(IEventHandler<>).MakeGenericType(eventType);

                    await (Task)concreteType.GetMethod("Handle").Invoke(handler, new object[] { @event });
                }
            }
        }
    }
}

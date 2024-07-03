using Confluent.Kafka;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Reflection;
using Microsoft.Extensions.Options;
namespace Microsoft.AspNetCore.Builder
{
    public static class MemoryCacheEvictorKafkaExtensions
    {
        public static IApplicationBuilder UseMemoryCacheEvictor(this IApplicationBuilder app)
        {
            return UseMemoryCacheEvictor<string>(app);
        }
        public static IApplicationBuilder UseMemoryCacheEvictor(this IApplicationBuilder app, Action<MemoryCacheEvictorKafkaOptions> setupAction)
        {
            return UseMemoryCacheEvictor<string>(app, setupAction);
        }
        public static IApplicationBuilder UseMemoryCacheEvictor<T>(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }
            UseMemoryCacheEvictorKafka<T>(app);
            return app;
        }
        public static IApplicationBuilder UseMemoryCacheEvictor<T>(this IApplicationBuilder app, Action<MemoryCacheEvictorKafkaOptions> setupAction)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (setupAction == null)
            {
                throw new ArgumentNullException(nameof(setupAction));
            }

            var options = new MemoryCacheEvictorKafkaOptions {ConsumerConfig = GetNewConsumerConfig(app) };
            setupAction.Invoke(options);
            UseMemoryCacheEvictorKafka<T>(app, options);
            return app;
        }
        private static void UseMemoryCacheEvictorKafka<T>(IApplicationBuilder app)
        {
            var options = new MemoryCacheEvictorKafkaOptions { ConsumerConfig = GetNewConsumerConfig(app) };
            UseMemoryCacheEvictorKafka<T>(app, options);
        }
        private static void UseMemoryCacheEvictorKafka<T>(IApplicationBuilder app, MemoryCacheEvictorKafkaOptions options)
        {
            Task.Run(() => {
                var _memoryCache = app.ApplicationServices.GetService<IMemoryCache>();
                if (_memoryCache == null)
                    return;

                var consumerConfig = options.ConsumerConfig;

                var _logger = app.ApplicationServices.GetRequiredService<ILogger<MemoryCacheEvictorKafkaOptions>>();

                using (var c = new ConsumerBuilder<Ignore, string>(consumerConfig).Build())
                {
                    c.Subscribe(options.Topic);

                    try
                    {
                        while (true)
                        {
                            ConsumeResult<Ignore, string> consumeResult = null;
                            try
                            {
                                consumeResult = c.Consume();
                                T key = default;
                                try
                                {
                                    key = JsonSerializer.Deserialize<T>(consumeResult?.Message?.Value);
                                }
                                catch (JsonException ex)
                                {
                                    _logger.LogDebug(ex, $"Failed to deserialize message to type '{typeof(T)}'. Message: '{consumeResult?.Message?.Value}'");
                                }

                                if (key != null)
                                {
                                    _memoryCache.Remove(key);
                                }
                                _logger.LogInformation($"Consumed '{options.Topic}' message '{consumeResult?.Message?.Value}' at: '{consumeResult?.TopicPartitionOffset}'.");
                            }
                            catch (ConsumeException ex)
                            {
                                _logger.LogError(ex, $"Error consuming '{options.Topic}' message '{consumeResult?.Message?.Value}' at: '{consumeResult?.TopicPartitionOffset}' reason: '{ex.Error?.Reason}'.");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        c.Close();
                    }
                }
            });
        }
        private static ConsumerConfig GetNewConsumerConfig(IApplicationBuilder app)
        {
            var injectConsumerConfig = app.ApplicationServices.GetService<ConsumerConfig>() ?? app.ApplicationServices.GetService<IOptions<ConsumerConfig>>()?.Value;
            var result = new ConsumerConfig();
            var uid = Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "");
            if (injectConsumerConfig != null)
            {
                result.GroupId = $"{injectConsumerConfig.GroupId}_{Environment.MachineName}_{uid}";
                result.BootstrapServers = injectConsumerConfig.BootstrapServers;
                result.SaslMechanism = injectConsumerConfig.SaslMechanism;
                result.SecurityProtocol = injectConsumerConfig.SecurityProtocol;
                result.SslEndpointIdentificationAlgorithm = injectConsumerConfig.SslEndpointIdentificationAlgorithm;
                result.SslCaLocation = injectConsumerConfig.SslCaLocation;
                result.SaslUsername = injectConsumerConfig.SaslUsername;
                result.SaslPassword = injectConsumerConfig.SaslPassword;
            }
            else
            {
                result.GroupId = $"{Assembly.GetEntryAssembly().GetName().FullName}_{Environment.MachineName}_{uid}";
            }
            result.AutoOffsetReset = AutoOffsetReset.Latest;
            result.EnableAutoCommit = true;
            result.AllowAutoCreateTopics = true;

            return result;
        }
    }
}

using Confluent.Kafka;
using System;

namespace Microsoft.Extensions.DependencyInjection
{
    public class MemoryCacheEvictorKafkaOptions
    {
        public ConsumerConfig ConsumerConfig { get; set; }
        public string Topic { get; set; } = "MemoryCacheEvictor";
    }
}
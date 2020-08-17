# Async RabbitMQ .NET Webinar

he RabbitMQ .NET Client with more than 31 million downloads has a long and evolutionary history. In this webinar, Daniel Marbach dives into the inception of the first ideas on how to gradually move the client from its synchronous resource-hogging-nature to a more modern, asynchronous, and joyful client truly embracing concurrency. 

In this webinar, you’ll learn about:
- The RabbitMQ .NET Client whereabouts
- The inception of an asynchronous code path into a purely synchronous code base
- The improvements of the message consumption loops through several iterations up to using channels
- The impact of async and concurrency on the client

## Basic Client Bits

RabbitMQ for historic reasons implements AMQP 0-9-1 protocol. AMQP 1.0 is supported via a plugin on the broker only. AMQP 0-9-1 has become the RabbitMQ protocol because there is as far as I know nobody else that supports AMQP 0-9-1. These are the basic primitives we need to know about

- ConnectionFactory (`IConnectionFactory`) constructs connections
- Connections (`IConnection`) represent a long-lived AMQP 0-9-1 connection that owns connection recovery, mapping of frames to headers, commands and more
- Model (`IModel`) represents an AMQP 0-9-1 channel that is meant to be long-lived providing the operations to interact with the broker (protocol methods). For applications that use multiple threads/processes for processing, it is very common to open a new channel per thread/process and not share channels between them. As a rule of thumb, IModel instance usage by more than one thread simultaneously should be avoided. Application code should maintain a clear notion of thread ownership for IModel instances. This is a hard requirement for publishers: sharing a channel (an IModel instance) for concurrent publishing will lead to incorrect frame interleaving at the protocol level. Channel instances must not be shared by threads that publish on them. A single connection can have at max 100 channels (setting on the server) but managing many channels at the same time can decrease performance/throughput. 
- Consumer (`IBasicConsumer` and other flavours) consumes protocol interactions from the broker (message received...)

The demo shows that we are handling one message at a time and if the sender increases the speed of sending messages they start piling up on the broker.

## Concurrency

- The basic consumer makes it very hard to achieve concurrency and asynchronicity. Ideally we'd want to use task based async for high throughput by `void` returning method make that painful
- Either custom offloading to the worker thread pool can be used or the evil `async void`
- Unfortunately any sort of concurrency within the consumer invalidates the client assumptions being able to return internally managed buffers once the consumer returned which then requires copying the body before yielding (will be solved in 6.2.0)
- Concurrency has to be limited by using custom techniques like SemaphoreSlim
- Because the semaphore `WaitAsync` might complete synchronously if there are still slots available `Yield` needs to be used to offload the current thread back to the worker thread pool to avoid blocking the clients reader loop thread.
- Because interleaves on the same model are not allowed all model/channel operations need to be executed on a dedicated custom scheduler that prevents critical operations to interleave, see `BasicAckSingle`

## Recap

- Even though IO-bound the RabbitMQ client is mostly purely sync
- Trying to bring asynchronicity even if only from a consumer perspective is a bit of a hassle and requires quite advanced trickery to get things working

How can we achieve better concurrency and asynchronicity support as a first class citizen in the client?

Touching the model/channel based methods was not yet an option at the time so we started from the receiver perspective.

## WorkerService

The worker service or consumer work service is the core worker service associated with a connection that "pumps" work to the consumers. 

### Start

To get a better grasp of what is going on we first inspected the sync loop.

|                   | 1 consumer | 32 consumer |
|-------------------|------------|-------------|
| Core 2 Duo E6550  | 6908 msg/s | 7667 msg/s  |
| i7-2600K (Sandy)  | 7619 msg/s | 11124 msg/s |
| i7-4870HQ         | 6103 msg/s | 5237 msg/s  |
| i5-5200U          | 7178 msg/s | 1928 msg/s  |
| i7-6500U          | 2243 msg/s | 1253 msg/s  |
| i7-6700K (Skylake)| 6476 msg/s | 2213 msg/s  |

The lock contention was so high that with more modern CPUs (at that time) when introducing concurrency only the throughput dropped in orders of magnitude.
 
The reason was the `BatchingWorkPool`.
 
Takeaways:
 
 - Lock contention, bad!
 - Locks bad in general because you cannot await inside lock statements (thread that enters lock needs to release it)
 - If you don't work on your lock problems not even async is going to help you much
 
 ### OptimizeSync
 
See [RabbitMQ NServiceBus 6 updates](https://particular.net/blog/rabbitmq-updates-in-nservicebus-6)
 
By using a `ConcurrentDictionary` to map the models to the worker pools and using a `ConcurrentQueue` with a dedicated `AutoResetEvent` to unleash the consumption loop we did not only better understand the loop but also tremendously improve the receive performance.
 
 |Matchup|VersionsCompared|Send ThroughputImprovement|Receive ThroughputImprovement|
 |--- |--- |--- |--- |
 |V6 improvements only|3.5 => 4.1|5.45|1.69|
 |V6 + lock contention fix|3.4 => 4.1|5.54|6.66|
 
Takeaways:
  
  - Removing locks can be liberating
  - Orders of magnitude of throughput improvements just by doing that
 
 ### Async
 
 Because of the large userbase of the client we wanted to introduce gradual changes. A good way to do so is to start introducing an alternative async I/O path that can be opted-in. So we created an `IAsyncConnectionFactory` and a `DispatchConsumersAsync` that then opts-in a new consumer work service with a dedicated `AsyncDefaultBasicConsumer` as well as `AsyncEventingBasicConsumer` that deals with asynchronicity. Still targeting .NET 4.5.x required us to make a few workarounds.
 
 At this stage it was possible to declare receive methods that return a task. Achieving concurrency was still a bit of a nightmare because the client wouldn't call the received handler concurrently you still needed to do all concurrency handling yourself including preventing interleaves.
 
Takeaways:

- Freeing up the threadpool threads is crucial to achieve high IO saturation
- Task based APIs are the key to achieve that and there is no way you can avoid supporting them
- Sometimes but not always it is possible to focus on a single code path to plug in async and Tasks/ValueTask without being affected by the viral nature of async/await. Async all the way is the key. Unfortunately we are still not there yet because many of the IO-bound channel methods are still sync and require dedicated offloading. 
 
 ### Cleanup
 
 Once we targeted .NET 4.6 and higher we were able to get rid of some of the workarounds we had in place.
 
 At some point we tried to replace the TaskCompletionSource with a SemaphoreSlim (below benchmark explores ingestion/enqueue speed)
 
 ``` ini
 
 BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19041.450 (2004/?/20H1)
 AMD Ryzen Threadripper 1920X, 1 CPU, 24 logical and 12 physical cores
 .NET Core SDK=3.1.302
   [Host]   : .NET Core 3.1.6 (CoreCLR 4.700.20.26901, CoreFX 4.700.20.31603), X64 RyuJIT
   ShortRun : .NET Core 3.1.6 (CoreCLR 4.700.20.26901, CoreFX 4.700.20.31603), X64 RyuJIT
 
 Job=ShortRun  IterationCount=3  LaunchCount=1  
 WarmupCount=3  
 
 ```
 |               Method | Elements |            Mean |            Error |         StdDev |          Median | Ratio | RatioSD |     Gen 0 | Gen 1 | Gen 2 |   Allocated |
 |--------------------- |--------- |----------------:|-----------------:|---------------:|----------------:|------:|--------:|----------:|------:|------:|------------:|
  |        SemaphoreSlim |     1000 |       234.47 μs |      1,140.08 μs |      62.491 μs |       200.14 μs |  6.66 |    1.56 |         - |     - |     - |     16384 B |
 | TaskCompletionSource |     1000 |        35.03 μs |         21.80 μs |       1.195 μs |        34.98 μs |  1.00 |    0.00 |    0.6714 |     - |     - |     18363 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
  |        SemaphoreSlim |    10000 |     1,438.37 μs |      1,177.41 μs |      64.538 μs |     1,428.71 μs |  4.29 |    0.13 |         - |     - |     - |    163842 B |
 | TaskCompletionSource |    10000 |       335.52 μs |         93.06 μs |       5.101 μs |       334.69 μs |  1.00 |    0.00 |    7.8125 |     - |     - |    189706 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
  |        SemaphoreSlim |   100000 |    17,884.81 μs |    149,107.67 μs |   8,173.095 μs |    13,361.81 μs |  5.57 |    2.59 |         - |     - |     - |   1572909 B |
 | TaskCompletionSource |   100000 |     3,221.54 μs |      1,694.54 μs |      92.883 μs |     3,196.08 μs |  1.00 |    0.00 |   66.4063 |     - |     - |   1859820 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
  |        SemaphoreSlim |  1000000 |   343,370.58 μs |  6,778,783.54 μs | 371,568.000 μs |   129,102.90 μs | 10.33 |   11.43 |         - |     - |     - |  16777472 B |
 | TaskCompletionSource |  1000000 |    33,965.98 μs |     25,943.49 μs |   1,422.050 μs |    33,497.74 μs |  1.00 |    0.00 |  750.0000 |     - |     - |  18924852 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
  |        SemaphoreSlim | 10000000 | 2,477,222.77 μs | 10,618,315.06 μs | 582,025.678 μs | 2,426,359.80 μs |  7.22 |    1.53 |         - |     - |     - | 167774720 B |
 | TaskCompletionSource | 10000000 |   341,844.47 μs |    260,680.55 μs |  14,288.781 μs |   346,426.60 μs |  1.00 |    0.00 | 6000.0000 |     - |     - | 192727136 B |
 
Takeway:

- Measure, measure, measure
 
 ### Channels
 
 ``` ini
 
 BenchmarkDotNet=v0.12.1, OS=Windows 10.0.19041.450 (2004/?/20H1)
 AMD Ryzen Threadripper 1920X, 1 CPU, 24 logical and 12 physical cores
 .NET Core SDK=3.1.302
   [Host]   : .NET Core 3.1.6 (CoreCLR 4.700.20.26901, CoreFX 4.700.20.31603), X64 RyuJIT
   ShortRun : .NET Core 3.1.6 (CoreCLR 4.700.20.26901, CoreFX 4.700.20.31603), X64 RyuJIT
 
 Job=ShortRun  IterationCount=3  LaunchCount=1  
 WarmupCount=3  
 
 ```
 |               Method | Elements |            Mean |            Error |         StdDev |          Median | Ratio | RatioSD |     Gen 0 | Gen 1 | Gen 2 |   Allocated |
 |--------------------- |--------- |----------------:|-----------------:|---------------:|----------------:|------:|--------:|----------:|------:|------:|------------:|
 |              **Channel** |     **1000** |        **91.21 μs** |        **268.03 μs** |      **14.692 μs** |        **89.52 μs** |  **2.60** |    **0.40** |         **-** |     **-** |     **-** |         **6 B** |
 |        SemaphoreSlim |     1000 |       234.47 μs |      1,140.08 μs |      62.491 μs |       200.14 μs |  6.66 |    1.56 |         - |     - |     - |     16384 B |
 | TaskCompletionSource |     1000 |        35.03 μs |         21.80 μs |       1.195 μs |        34.98 μs |  1.00 |    0.00 |    0.6714 |     - |     - |     18363 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |    **10000** |       **735.49 μs** |      **1,689.63 μs** |      **92.614 μs** |       **785.76 μs** |  **2.20** |    **0.31** |         **-** |     **-** |     **-** |        **51 B** |
 |        SemaphoreSlim |    10000 |     1,438.37 μs |      1,177.41 μs |      64.538 μs |     1,428.71 μs |  4.29 |    0.13 |         - |     - |     - |    163842 B |
 | TaskCompletionSource |    10000 |       335.52 μs |         93.06 μs |       5.101 μs |       334.69 μs |  1.00 |    0.00 |    7.8125 |     - |     - |    189706 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |   **100000** |     **6,480.49 μs** |     **23,604.57 μs** |   **1,293.846 μs** |     **6,164.78 μs** |  **2.01** |    **0.35** |         **-** |     **-** |     **-** |       **290 B** |
 |        SemaphoreSlim |   100000 |    17,884.81 μs |    149,107.67 μs |   8,173.095 μs |    13,361.81 μs |  5.57 |    2.59 |         - |     - |     - |   1572909 B |
 | TaskCompletionSource |   100000 |     3,221.54 μs |      1,694.54 μs |      92.883 μs |     3,196.08 μs |  1.00 |    0.00 |   66.4063 |     - |     - |   1859820 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |  **1000000** |    **95,381.12 μs** |    **544,484.47 μs** |  **29,845.031 μs** |    **85,179.71 μs** |  **2.82** |    **0.96** |         **-** |     **-** |     **-** |      **3231 B** |
 |        SemaphoreSlim |  1000000 |   343,370.58 μs |  6,778,783.54 μs | 371,568.000 μs |   129,102.90 μs | 10.33 |   11.43 |         - |     - |     - |  16777472 B |
 | TaskCompletionSource |  1000000 |    33,965.98 μs |     25,943.49 μs |   1,422.050 μs |    33,497.74 μs |  1.00 |    0.00 |  750.0000 |     - |     - |  18924852 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** | **10000000** |   **781,592.13 μs** |  **1,910,114.79 μs** | **104,699.837 μs** |   **821,476.40 μs** |  **2.29** |    **0.36** |         **-** |     **-** |     **-** |      **7744 B** |
 |        SemaphoreSlim | 10000000 | 2,477,222.77 μs | 10,618,315.06 μs | 582,025.678 μs | 2,426,359.80 μs |  7.22 |    1.53 |         - |     - |     - | 167774720 B |
 | TaskCompletionSource | 10000000 |   341,844.47 μs |    260,680.55 μs |  14,288.781 μs |   346,426.60 μs |  1.00 |    0.00 | 6000.0000 |     - |     - | 192727136 B |

 
 ### Concurrency
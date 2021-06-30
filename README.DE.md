# Evolutionsgeschichte des RabbitMQ .NET Clients in Richtung Nebenläufigkeit

Übersetzt mit www.DeepL.com/Translator (kostenlose Version)

## Zusammenfassung

Der RabbitMQ .NET Client mit mehr als 31 Millionen Downloads hat eine lange und evolutionäre Geschichte. In diesem Vortrag tauche ich in die Anfänge der ersten Ideen ein, wie man den Client schrittweise von seiner synchronen, ressourcenfressenden Natur zu einem moderneren und asynchronen Client bewegen kann, der Nebenläufigkeit wirklich verinnerlicht. Sie werden Folgendes lernen:

- Das API des RabbitMQ .NET-Clients
- Die Einführung eines asynchronen Codepfads in eine rein synchrone Codebasis
- Die Verbesserungen der Nachrichtenverarbeitungsschleife durch mehrere Iterationen bis hin zur Verwendung von Channels
- Die Auswirkungen von Asynchronität und Nebenläufigkeit auf den Client

## Intro

Seit mehreren Jahren trage ich zum RabbitMQ .NET-Client bei und habe dabei geholfen, den Client schrittweise zu einer moderneren und nebenläufigkeitsfähigen Bibliothek weiterzuentwickeln, zusammen mit dem VMWare-Team und anderen hervorragenden Community-Mitgliedern. In diesem Vortrag werde ich Ihnen wertvolle Lektionen vermitteln, die ich über async und Nebenläufigkeit gelernt habe und wie man Codebasen im Laufe der Zeit zu moderneren Nebenläufigkeitsparadigmen weiterentwickelt. Viele der Lektionen lassen sich auch auf Ihren Geschäftscode übertragen. Darüber hinaus erhalten Sie tiefes Wissen über async / TPL und rekapitulieren die Grundlagen der asynchronen und gleichzeitigen Programmierung in .NET.

## Aufbau

1. Machen Sie alle mit der RabbitMQ-API zum Empfang von Nachrichten vom Broker vertraut, so dass jeder einen Überblick auf hohem Niveau hat.
1. Zeigen Sie, wie schwierig es ist, Gleichzeitigkeit mit dem grundlegenden Consumer zu erreichen
1. Sprechen Sie über den Prozess, wie der Client schrittweise weiterentwickelt wurde, indem
   1. Verstehen des synchronen Pfads
   1. Optimieren des synchronen Pfades
   1. Einen rundandanten asynchronen Pfad einführen, um Verbraucher anzuschließen
   1. Aufräumen des entstandenen Durcheinanders
   1. Benutzerdefinierten Code herausreißen und durch `System.Threading.Channels` ersetzen
   1. Erstklassige Unterstützung für Gleichzeitigkeit innerhalb des Clients
1. Einpacken

## Basic Client Bits

> RabbitMQ ist der am weitesten verbreitete Open Source Message Broker.

RabbitMQ implementiert aus historischen Gründen das AMQP 0-9-1-Protokoll. AMQP 1.0 wird nur über ein Plugin auf dem Broker unterstützt. AMQP 0-9-1 ist das RabbitMQ-Protokoll geworden, weil es meines Wissens nach niemanden gibt, der AMQP 0-9-1 unterstützt. Dies sind die grundlegenden Primitive, über die wir Bescheid wissen müssen

- ConnectionFactory (`IConnectionFactory`) konstruiert Verbindungen
- Connections (`IConnection`) repräsentiert eine langlebige AMQP 0-9-1 Verbindung, die Verbindungswiederherstellung, Mapping von Frames auf Header, Befehle und mehr besitzt
- Model (`IModel`) repräsentiert einen AMQP 0-9-1-Kanal, der langlebig sein soll und die Operationen zur Interaktion mit dem Broker (Protokollmethoden) bereitstellt. Bei Anwendungen, die mehrere Threads/Prozesse für die Verarbeitung verwenden, ist es sehr üblich, einen neuen Kanal pro Thread/Prozess zu öffnen und die Kanäle nicht untereinander zu teilen. Als Faustregel gilt, dass die Verwendung von `IModel`-Instanzen durch mehr als einen Thread gleichzeitig vermieden werden sollte. Der Anwendungscode sollte eine klare Vorstellung von der Thread-Eigentümerschaft für IModel-Instanzen haben. Dies ist eine harte Anforderung für Publisher: Die gemeinsame Nutzung eines Kanals (einer IModel-Instanz) für gleichzeitiges Publishing führt zu falschem Frame-Interleaving auf Protokollebene. Kanalinstanzen dürfen nicht von Threads geteilt werden, die auf ihnen veröffentlichen. Eine einzelne Verbindung kann maximal 100 Kanäle haben (Einstellung auf dem Server), aber die Verwaltung vieler Kanäle zur gleichen Zeit kann die Leistung/den Durchsatz verringern.
- Consumer (`IBasicConsumer` und andere Varianten) konsumiert Protokollinteraktionen vom Broker (empfangene Nachrichten...)

Die Demo zeigt, dass wir jeweils eine Nachricht verarbeiten, und wenn der Sender die Geschwindigkeit des Versendens von Nachrichten erhöht, beginnen sie sich beim Broker zu stapeln. Eine Möglichkeit, die Verarbeitung der Nachrichten zu beschleunigen, wäre es, den Consumer zu skalieren (d.h. mehrere Instanzen zu haben), aber bevor wir das tun, wäre es schön, wenn wir sicherstellen könnten, dass ein einzelner Consumer mehrere Nachrichten parallel empfangen kann (Scale-up). Es wäre möglich, mehrere Modelle zu erstellen oder mehrere Basis-Consumer auf demselben Modell zu registrieren. Dieser Ansatz kann jedoch den Empfang und die Verarbeitung von Nachrichten extrem komplex machen. Lassen Sie uns sehen, was wir sonst noch tun können.

## Concurrency

- Der Basic Consumer macht es sehr schwer, Gleichzeitigkeit und Asynchronität zu erreichen. Idealerweise würden wir Task-basierte Asynchronität für einen hohen Durchsatz verwenden wollen, da die `void`-Rückgabemethode das sehr schwierig macht
- Entweder kann ein benutzerdefiniertes Offloading auf den Worker-Thread-Pool verwendet werden oder das böse `async void`
- Leider macht jede Art von Gleichzeitigkeit innerhalb des Consumers die Annahme des Clients ungültig, dass er intern verwaltete Puffer zurückgeben kann, sobald der Consumer zurückgekehrt ist, was dann das Kopieren des Bodys vor dem Yielding erfordert (wird in 6.2.0 gelöst).
- Die Gleichzeitigkeit muss mit Hilfe eigener Techniken wie `SemaphoreSlim` begrenzt werden.
- Da der Semaphor `WaitAsync` möglicherweise synchron ausgeführt wird, wenn noch Slots verfügbar sind, muss `Yield` verwendet werden, um den aktuellen Thread zurück in den Worker-Thread-Pool zu verlagern, damit der Thread der Leserschleife des Clients nicht blockiert wird.
- Da Interleaves auf demselben Modell nicht erlaubt sind, müssen alle Modell-/Kanal-Operationen auf einem dedizierten, benutzerdefinierten Scheduler ausgeführt werden, der verhindert, dass kritische Operationen verschachtelt werden, siehe `BasicAckSingle`

## Rekapitulation

- Auch wenn der RabbitMQ-Client IO-gebunden ist, ist er meist rein synchron
- Der Versuch, Asynchronität einzubringen, selbst wenn dies nur aus der Perspektive des Konsumenten geschieht, ist ein bisschen mühsam und erfordert ziemlich fortgeschrittene Tricks, um die Dinge zum Laufen zu bringen

Wie können wir eine bessere Unterstützung von Gleichzeitigkeit und Asynchronität als Bürger erster Klasse im Client erreichen?

Das Anfassen der modell- bzw. kanalbasierten Methoden war zu diesem Zeitpunkt noch keine Option, also begannen wir mit der Empfängerperspektive. Die übergeordnete Idee ist also, den besten Einstiegspunkt zu finden, um Asynchronität zu aktivieren, ohne alle Verbraucher zu zerstören, und von dort aus die Asynchronitätsunterstützung schrittweise zu erweitern. Bevor wir das tun können, müssen wir verstehen, was die Nachrichten an die Verbraucher antreibt. Werfen wir einen Blick auf den `WorkerService`.

## WorkerService

Der WorkerService oder Verbraucher-Arbeitsdienst ist der Kern-Arbeitsdienst, der mit einer Verbindung verbunden ist und Arbeit zu den Verbrauchern "pumpt".

## Start

Um ein besseres Verständnis für das Geschehen zu bekommen, haben wir uns zunächst die Sync-Schleife angesehen.

|                   | 1 consumer | 32 consumer |
|-------------------|------------|-------------|
| Core 2 Duo E6550  | 6908 msg/s | 7667 msg/s  |
| i7-2600K (Sandy)  | 7619 msg/s | 11124 msg/s |
| i7-4870HQ         | 6103 msg/s | 5237 msg/s  |
| i5-5200U          | 7178 msg/s | 1928 msg/s  |
| i7-6500U          | 2243 msg/s | 1253 msg/s  |
| i7-6700K (Skylake)| 6476 msg/s | 2213 msg/s  |

Die Lockcontention war so hoch, dass bei moderneren CPUs (zu dieser Zeit) bei Einführung der Gleichzeitigkeit nur der Durchsatz in Größenordnungen sank.

Der Grund dafür war der `BatchingWorkPool` und seine Verwendung von Locks sowie die blockierende Collection.

Schlussfolgerungen:

 - Lock Contention, schlecht!
 - Locks sind generell schlecht, weil man innerhalb von Lock-Anweisungen nicht warten kann (ein Thread, der eine Lock betritt, muss sie auch wieder freigeben)
 - Wenn Sie nicht an Ihren Lock-Problemen arbeiten, wird Ihnen auch async nicht viel helfen

### OptimizeSync

Siehe [RabbitMQ NServiceBus 6 Updates](https://particular.net/blog/rabbitmq-updates-in-nservicebus-6)

Durch die Verwendung eines `ConcurrentDictionary`, um die Modelle auf die Worker-Pools abzubilden und die Verwendung einer `ConcurrentQueue` mit einem dedizierten `AutoResetEvent`, um die Verbrauchsschleife auszulösen, haben wir nicht nur die Schleife besser verstanden, sondern auch die Empfangsleistung enorm verbessert.

 |Matchup|VersionsCompared|Send ThroughputImprovement|Receive ThroughputImprovement|
 |--- |--- |--- |--- |
 |V6 improvements only|3.5 => 4.1|5.45|1.69|
 |V6 + lock contention fix|3.4 => 4.1|5.54|6.66|

Mitnahmen:

  - Das Entfernen von Sperren kann befreiend sein
  - Durchsatzverbesserungen in Größenordnungen, nur indem man das tut

### Async

 Aufgrund der großen Nutzerbasis des Clients wollten wir schrittweise Änderungen einführen. Ein guter Weg, dies zu tun, ist die Einführung eines alternativen asynchronen E/A-Pfads, der aktiviert werden kann. Also haben wir eine `IAsyncConnectionFactory` und ein `DispatchConsumersAsync` erstellt, das dann einen neuen Consumer-Work-Service mit einem dedizierten `AsyncDefaultBasicConsumer` sowie einem `AsyncEventingBasicConsumer`, der sich mit Asynchronität beschäftigt, einbindet. Da wir immer noch auf .NET 4.5.x abzielen, mussten wir ein paar Workarounds machen.

 In diesem Stadium war es möglich, Empfangsmethoden zu deklarieren, die einen Task zurückgeben. Das Erreichen von Gleichzeitigkeit war immer noch ein kleiner Alptraum, da der Client den Receive-Handler nicht gleichzeitig aufrufen würde. Sie mussten immer noch die gesamte Gleichzeitigkeitsbehandlung selbst durchführen, einschließlich der Vermeidung von Interleaves.

Schlussfolgerungen:

- Das Freigeben der Threadpool-Threads ist entscheidend, um eine hohe IO-Sättigung zu erreichen
- Task-basierte APIs sind der Schlüssel zum Erreichen dieses Ziels, und es führt kein Weg daran vorbei, sie zu unterstützen
- Manchmal, aber nicht immer, ist es möglich, sich auf einen einzigen Codepfad zu konzentrieren, um async und Tasks/ValueTask einzubauen, ohne von der viralen Natur von async/await betroffen zu sein. Async all the way ist der Schlüssel. Leider sind wir noch nicht so weit, da viele der IO-gebundenen Kanalmethoden immer noch sync sind und ein dediziertes Offloading erfordern.

### Cleanup

 Sobald wir .NET 4.6 und höher im Visier hatten, konnten wir einige der Workarounds loswerden, die wir im Einsatz hatten.

 Irgendwann versuchten wir, die TaskCompletionSource durch eine SemaphoreSlim zu ersetzen (unter dem Benchmark wird die Ingestion/Enqueue-Geschwindigkeit untersucht)

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

Mitnahme:

- Messen, messen, messen

### Channels

 Mit der neuen 6.x-Hauptversion hatten wir endlich die Möglichkeit, `System.Threading.Channels` zu verwenden, um insgesamt die Zuweisungen des Clients und hoffentlich den Gesamtdurchsatz zu verbessern.

 - Ein einzelner unbeschränkter Kanal wird verwendet, um Arbeit in ihn zu schreiben
 - Eine dedizierte asynchrone Arbeit wird in den Worker-Thread-Pool eingeplant, der Nachrichten aus dem Kanal konsumiert und die Consumer aufruft

 Hier ist der Vergleichs-Benchmark, der `System.Threading.Channels` mit `TaskCompletionSource` auf dem Ingestion-Pfad vergleicht. Wie Sie sehen können, ist `System.Threading.Channels` etwas langsamer, aber der Overhead ist vernachlässigbar, da der Verbrauch weniger allokationslastig ist und im Allgemeinen mit Channels viel optimierter ist.

 Außerdem ist die Möglichkeit, einen Teil der Komplexität an eine von Microsoft bereitgestellte Lösung auszulagern, sehr nützlich, da mit der Zeit, wenn die Channels verbessert werden, alle Benutzer des RabbitmQ-Clients automatisch von diesen Verbesserungen profitieren können. Aus der Wartungsperspektive können wir auch sagen, dass es generell eine gute Sache ist, weniger Code zu besitzen.

 Takeway:

 - Channels sind schöne Nebenläufigkeitsdatenstrukturen für Producer/Consumer-Muster
 - Nicht immer ist die schnellste Lösung auch die beste. Alle Faktoren müssen berücksichtigt werden


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
 | TaskCompletionSource |     1000 |        35.03 μs |         21.80 μs |       1.195 μs |        34.98 μs |  1.00 |    0.00 |    0.6714 |     - |     - |     18363 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |    **10000** |       **735.49 μs** |      **1,689.63 μs** |      **92.614 μs** |       **785.76 μs** |  **2.20** |    **0.31** |         **-** |     **-** |     **-** |        **51 B** |
 | TaskCompletionSource |    10000 |       335.52 μs |         93.06 μs |       5.101 μs |       334.69 μs |  1.00 |    0.00 |    7.8125 |     - |     - |    189706 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |   **100000** |     **6,480.49 μs** |     **23,604.57 μs** |   **1,293.846 μs** |     **6,164.78 μs** |  **2.01** |    **0.35** |         **-** |     **-** |     **-** |       **290 B** |
 | TaskCompletionSource |   100000 |     3,221.54 μs |      1,694.54 μs |      92.883 μs |     3,196.08 μs |  1.00 |    0.00 |   66.4063 |     - |     - |   1859820 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** |  **1000000** |    **95,381.12 μs** |    **544,484.47 μs** |  **29,845.031 μs** |    **85,179.71 μs** |  **2.82** |    **0.96** |         **-** |     **-** |     **-** |      **3231 B** |
 | TaskCompletionSource |  1000000 |    33,965.98 μs |     25,943.49 μs |   1,422.050 μs |    33,497.74 μs |  1.00 |    0.00 |  750.0000 |     - |     - |  18924852 B |
 |                      |          |                 |                  |                |                 |       |         |           |       |       |             |
 |              **Channel** | **10000000** |   **781,592.13 μs** |  **1,910,114.79 μs** | **104,699.837 μs** |   **821,476.40 μs** |  **2.29** |    **0.36** |         **-** |     **-** |     **-** |      **7744 B** |
 | TaskCompletionSource | 10000000 |   341,844.47 μs |    260,680.55 μs |  14,288.781 μs |   346,426.60 μs |  1.00 |    0.00 | 6000.0000 |     - |     - | 192727136 B |

### Concurrency

Mit der Version 6.2 wird es eine erstklassige Unterstützung für Gleichzeitigkeit geben, die zwei redundante Codepfade implementiert. Einen für den Fall der Gleichzeitigkeit = 1 und einen für den Fall der Gleichzeitigkeit > 1.

Mitnahme:

  - Die First-Class-Unterstützung für Gleichzeitigkeit macht den Client so viel benutzerfreundlicher
  - Da der Client nun auch intern `System.Threading.Channels` verwendet, gibt es keine Verschachtelungsprobleme mehr

## Was bringt die Zukunft?

- Bemühungen der Community, eine vollständig async-fähige Channel-API einzuführen, um Threads wirklich zu entblocken
- Noch mehr Allokationsreduzierungen

## Zusammenfassung und Schlussbemerkung

- Finden Sie die IO-gebundenen Pfade in Ihrem Code
- Schauen Sie sich den IO-Pfad genau an, um sicherzustellen, dass Sie die Sperrkonflikte loswerden können
- Wenn möglich, führen Sie einen redundanten Codepfad ein, der durchgängig asynchron ist.
- Beginnen Sie allmählich, Ihre IO-Pfade so zu erweitern, dass sie niemals Threads blockieren. Führen Sie nur benutzerdefinierte Offloads durch, wenn es wirklich notwendig ist.
- Halten Sie sich an die async/best practices
- Wenn Sie auf .NET 5 abzielen, ziehen Sie [ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/) in Betracht, da Sie viele Vorteile aus dem [Value Task Pooling](https://devblogs.microsoft.com/dotnet/async-valuetask-pooling-in-net-5/) ziehen können
- Code, den Sie nicht selbst schreiben und pflegen müssen und der durch eine zuverlässige BCL-Komponente wie `System.Threading.Channels` ersetzt werden kann, sollte bevorzugt werden
- Zu guter Letzt, wenn Sie nach Möglichkeiten suchen, zu OSS beizutragen, besuchen Sie https://github.com/rabbitmq/rabbitmq-dotnet-client

## Haftungsausschluss

Ich war maßgeblich an den Worker-Service-Änderungen von der Client-Version 4.1 bis zu den jüngsten Änderungen zum Zeitpunkt des Schreibens beteiligt, die den redundanten asynchronen Code-Pfad einführten, über den ich hier sprechen werde. Aber natürlich würde ich nie behaupten, der alleinige Eigentümer dieser Änderungen zu sein. Das VMWare-Team nämlich

- [Luke Bakken](https://github.com/lukebakken)
- [Michael Klishin](https://github.com/michaelklishin)

sowie die Community wie

- [Brandon Ording](https://github.com/bording)
- [Szymon Kulec](https://github.com/scooletz)
- [Stefán Jökull Sigurðarson](https://github.com/stebet)
- [Sandro Bollhalder](https://github.com/bollhals)
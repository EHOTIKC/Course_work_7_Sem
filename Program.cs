using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Course_work_7_Sem
{
    public enum AggregateStatus
    {
        New,
        PrimarySuccess,
        PrimaryRejected
    }

    public class Aggregate
    {
        public int Id { get; }
        public double ArrivalTime { get; }
        public AggregateStatus Status { get; set; }

        public Aggregate(int id, double arrivalTime)
        {
            Id = id;
            ArrivalTime = arrivalTime;
            Status = AggregateStatus.New;
        }
    }

    public enum EventType
    {
        Arrival,
        ServiceEnd
    }

    public class SimEvent
    {
        public double Time { get; }
        public EventType Type { get; }
        public ServiceChannel? Channel { get; }

        public SimEvent(double time, EventType type, ServiceChannel? channel = null)
        {
            Time = time;
            Type = type;
            Channel = channel;
        }
    }

    public class ServiceChannel
    {
        public string Name { get; }
        public double MeanServiceTime { get; set; }
        public int BatchSize { get; }
        public bool IsBusy { get; private set; }

        public QueueTracker InputQueue { get; }
        public QueueTracker? NextQueue { get; set; }
        public ServiceChannel? NextChannel { get; set; }
        public AggregateStatus? OutputStatus { get; set; }

        public List<Aggregate> CurrentBatch { get; } = new();
        public double TotalBusyTime { get; private set; }
        public int ProcessedBatchesCount { get; private set; }
        public int ProcessedAggregatesCount => ProcessedBatchesCount * BatchSize;

        private double _lastStateChangeTime;

        public ServiceChannel(string name, double meanServiceTime, int batchSize, QueueTracker inputQueue)
        {
            Name = name;
            MeanServiceTime = meanServiceTime;
            BatchSize = batchSize;
            InputQueue = inputQueue;
            IsBusy = false;
            TotalBusyTime = 0.0;
            ProcessedBatchesCount = 0;
            _lastStateChangeTime = 0.0;
        }

        public void ResetStats(double currentTime)
        {
            TotalBusyTime = 0.0;
            ProcessedBatchesCount = 0;
            _lastStateChangeTime = currentTime;
        }

        public void StartService(IEnumerable<Aggregate> items, double currentTime)
        {
            IsBusy = true;
            CurrentBatch.Clear();
            CurrentBatch.AddRange(items);
            _lastStateChangeTime = currentTime;
        }

        public List<Aggregate> CompleteService(double currentTime)
        {
            IsBusy = false;
            TotalBusyTime += (currentTime - _lastStateChangeTime);
            ProcessedBatchesCount++;
            _lastStateChangeTime = currentTime;

            var finished = new List<Aggregate>(CurrentBatch);
            CurrentBatch.Clear();
            return finished;
        }

        public void FinalizeTime(double currentTime)
        {
            if (IsBusy)
            {
                TotalBusyTime += (currentTime - _lastStateChangeTime);
                _lastStateChangeTime = currentTime;
            }
        }
    }

    public class QueueTracker
    {
        public string Name { get; }
        private readonly Queue<Aggregate> _queue = new();
        private double _lastChangeTime = 0.0;
        private double _areaUnderCurve = 0.0;
        public int MaxObserved { get; private set; } = 0;

        public int Count => _queue.Count;

        public QueueTracker(string name)
        {
            Name = name;
        }

        public void ResetStats(double currentTime)
        {
            _areaUnderCurve = 0.0;
            MaxObserved = _queue.Count;
            _lastChangeTime = currentTime;
        }

        public void Enqueue(Aggregate item, double currentTime)
        {
            UpdateStats(currentTime);
            _queue.Enqueue(item);
            if (_queue.Count > MaxObserved) MaxObserved = _queue.Count;
        }

        public Aggregate Dequeue(double currentTime)
        {
            UpdateStats(currentTime);
            return _queue.Dequeue();
        }

        public List<Aggregate> DequeueBatch(int batchSize, double currentTime)
        {
            var batch = new List<Aggregate>(batchSize);
            for (int i = 0; i < batchSize; i++)
            {
                batch.Add(Dequeue(currentTime));
            }
            return batch;
        }

        public void UpdateStats(double currentTime)
        {
            double dt = currentTime - _lastChangeTime;
            _areaUnderCurve += _queue.Count * dt;
            _lastChangeTime = currentTime;
        }

        public double GetAverageLength(double totalTime)
        {
            return totalTime > 0 ? _areaUnderCurve / totalTime : 0.0;
        }
    }

    public class SimulationConfig
    {
        public double SimTime { get; set; } = 5000.0;
        public double MeanArrivalInterval { get; set; } = 30.0;
        public double MeanPrimaryTime { get; set; } = 30.0;
        public double MeanSecondaryTime { get; set; } = 30.0;
        public double MeanFullTime { get; set; } = 100.0;
        public int InputBufferCapacity { get; set; } = 0;
        public double WarmupTime { get; set; } = 1500.0;
    }

    public class ReplicationResult
    {
        public int ArrivedPairs { get; set; }
        public int RejectedPairs { get; set; }
        public double RejectionProbability { get; set; }
        public double UtilizationD1 { get; set; }
        public double UtilizationD2 { get; set; }
        public double UtilizationD3 { get; set; }
        public double AvgQueueD1 { get; set; }
        public int MaxQueueD1 { get; set; }
        public double AvgQueueSecondary { get; set; }
        public int MaxQueueSecondary { get; set; }
        public double AvgQueueFull { get; set; }
        public int MaxQueueFull { get; set; }
        public int CompletedAggregates { get; set; }
    }

    public class AggregatedMetric
    {
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }

        public static AggregatedMetric FromValues(IEnumerable<double> values)
        {
            var list = values.ToList();
            if (!list.Any()) return new AggregatedMetric();
            double mean = list.Average();
            double variance = list.Count > 1 ? list.Sum(x => Math.Pow(x - mean, 2)) / (list.Count - 1) : 0.0;
            return new AggregatedMetric
            {
                Mean = mean,
                StdDev = Math.Sqrt(variance),
                Min = list.Min(),
                Max = list.Max()
            };
        }
    }

    public class B2Simulator
    {
        private readonly SimulationConfig _config;
        private readonly Random _rnd;

        public B2Simulator(SimulationConfig config, int seed)
        {
            _config = config;
            _rnd = new Random(seed);
        }

        private double NextExp(double mean)
        {
            return -mean * Math.Log(1.0 - _rnd.NextDouble());
        }

        private void TryStartChannel(ServiceChannel channel, double currentTime, PriorityQueue<SimEvent, double> eventQueue)
        {
            if (!channel.IsBusy && channel.InputQueue.Count >= channel.BatchSize)
            {
                var batch = channel.InputQueue.DequeueBatch(channel.BatchSize, currentTime);
                channel.StartService(batch, currentTime);
                double dur = NextExp(channel.MeanServiceTime);
                eventQueue.Enqueue(new SimEvent(currentTime + dur, EventType.ServiceEnd, channel), currentTime + dur);
            }
        }

        public ReplicationResult Run()
        {
            double currentTime = 0.0;
            int aggregateCounter = 0;
            var eventQueue = new PriorityQueue<SimEvent, double>();

            var inputQueue = new QueueTracker("Вхідний буфер");
            var bufferSucc = new QueueTracker("Буфер успішних");
            var bufferFail = new QueueTracker("Буфер відхилених");

            var channelPrimary = new ServiceChannel("Первинне", _config.MeanPrimaryTime, 2, inputQueue);
            var channelSecondary = new ServiceChannel("Вторинне", _config.MeanSecondaryTime, 2, bufferSucc);
            var channelFull = new ServiceChannel("Повне", _config.MeanFullTime, 1, bufferFail);

            channelPrimary.NextQueue = bufferSucc;
            channelPrimary.NextChannel = channelSecondary;
            channelPrimary.OutputStatus = AggregateStatus.PrimarySuccess;

            int totalArrivedPairs = 0;
            int totalRejectedPairs = 0;
            int completedAggregates = 0;

            double firstArrival = NextExp(_config.MeanArrivalInterval);
            eventQueue.Enqueue(new SimEvent(firstArrival, EventType.Arrival), firstArrival);

            double totalEndTime = _config.WarmupTime + _config.SimTime;
            bool warmupDone = _config.WarmupTime <= 0.0;

            while (eventQueue.Count > 0)
            {
                var ev = eventQueue.Dequeue();

                if (!warmupDone && ev.Time >= _config.WarmupTime)
                {
                    double wt = _config.WarmupTime;
                    inputQueue.UpdateStats(wt);
                    bufferSucc.UpdateStats(wt);
                    bufferFail.UpdateStats(wt);
                    channelPrimary.FinalizeTime(wt);
                    channelSecondary.FinalizeTime(wt);
                    channelFull.FinalizeTime(wt);

                    inputQueue.ResetStats(wt);
                    bufferSucc.ResetStats(wt);
                    bufferFail.ResetStats(wt);
                    channelPrimary.ResetStats(wt);
                    channelSecondary.ResetStats(wt);
                    channelFull.ResetStats(wt);

                    totalArrivedPairs = 0;
                    totalRejectedPairs = 0;
                    completedAggregates = 0;
                    warmupDone = true;
                }

                if (ev.Time > totalEndTime)
                {
                    currentTime = totalEndTime;
                    break;
                }

                currentTime = ev.Time;
                inputQueue.UpdateStats(currentTime);
                bufferSucc.UpdateStats(currentTime);
                bufferFail.UpdateStats(currentTime);

                switch (ev.Type)
                {
                    case EventType.Arrival:
                        totalArrivedPairs++;
                        var a1 = new Aggregate(++aggregateCounter, currentTime);
                        var a2 = new Aggregate(++aggregateCounter, currentTime);

                        if (!channelPrimary.IsBusy && inputQueue.Count == 0)
                        {
                            channelPrimary.StartService(new[] { a1, a2 }, currentTime);
                            double dur = NextExp(channelPrimary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + dur, EventType.ServiceEnd, channelPrimary), currentTime + dur);
                        }
                        else
                        {
                            if (inputQueue.Count + 2 <= _config.InputBufferCapacity)
                            {
                                inputQueue.Enqueue(a1, currentTime);
                                inputQueue.Enqueue(a2, currentTime);
                            }
                            else
                            {
                                totalRejectedPairs++;
                                a1.Status = AggregateStatus.PrimaryRejected;
                                a2.Status = AggregateStatus.PrimaryRejected;
                                bufferFail.Enqueue(a1, currentTime);
                                bufferFail.Enqueue(a2, currentTime);

                                TryStartChannel(channelFull, currentTime, eventQueue);
                            }
                        }

                        double nextArr = currentTime + NextExp(_config.MeanArrivalInterval);
                        eventQueue.Enqueue(new SimEvent(nextArr, EventType.Arrival), nextArr);
                        break;

                    case EventType.ServiceEnd:
                        var channel = ev.Channel!;
                        var finishedBatch = channel.CompleteService(currentTime);

                        if (channel.NextQueue != null)
                        {
                            foreach (var agg in finishedBatch)
                            {
                                if (channel.OutputStatus.HasValue)
                                    agg.Status = channel.OutputStatus.Value;
                                channel.NextQueue.Enqueue(agg, currentTime);
                            }
                        }
                        else
                        {
                            completedAggregates += finishedBatch.Count;
                        }

                        TryStartChannel(channel, currentTime, eventQueue);

                        if (channel.NextChannel != null)
                        {
                            TryStartChannel(channel.NextChannel, currentTime, eventQueue);
                        }
                        break;
                }
            }

            channelPrimary.FinalizeTime(currentTime);
            channelSecondary.FinalizeTime(currentTime);
            channelFull.FinalizeTime(currentTime);

            inputQueue.UpdateStats(currentTime);
            bufferSucc.UpdateStats(currentTime);
            bufferFail.UpdateStats(currentTime);

            double statsDuration = currentTime - _config.WarmupTime;

            return new ReplicationResult
            {
                ArrivedPairs = totalArrivedPairs,
                RejectedPairs = totalRejectedPairs,
                RejectionProbability = totalArrivedPairs > 0 ? (double)totalRejectedPairs / totalArrivedPairs : 0.0,
                UtilizationD1 = channelPrimary.TotalBusyTime / statsDuration,
                UtilizationD2 = channelSecondary.TotalBusyTime / statsDuration,
                UtilizationD3 = channelFull.TotalBusyTime / statsDuration,
                AvgQueueD1 = inputQueue.GetAverageLength(statsDuration),
                MaxQueueD1 = inputQueue.MaxObserved,
                AvgQueueSecondary = bufferSucc.GetAverageLength(statsDuration),
                MaxQueueSecondary = bufferSucc.MaxObserved,
                AvgQueueFull = bufferFail.GetAverageLength(statsDuration),
                MaxQueueFull = bufferFail.MaxObserved,
                CompletedAggregates = completedAggregates
            };
        }
    }

    public class ExperimentRunner
    {
        public static List<ReplicationResult> RunReplications(SimulationConfig config, int replicationsCount = 100, int startSeed = 1000)
        {
            var results = new List<ReplicationResult>();
            for (int i = 0; i < replicationsCount; i++)
            {
                var sim = new B2Simulator(config, startSeed + i * 17);
                results.Add(sim.Run());
            }
            return results;
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine("================================================================================");
            Console.WriteLine("КПІ ім. Ігоря Сікорського. Курсова робота: Моделювання систем");
            Console.WriteLine("Завдання B2: Мережа масового обслуговування регулювальної ділянки цеху");
            Console.WriteLine("================================================================================\n");

            var baseConfig = new SimulationConfig
            {
                SimTime = 5000.0,
                MeanArrivalInterval = 30.0,
                MeanPrimaryTime = 30.0,
                MeanSecondaryTime = 30.0,
                MeanFullTime = 100.0,
                InputBufferCapacity = 0
            };

            Console.WriteLine(">>> Виконання 100 реплікацій базової моделі (N = 0)...");
            var baseReplications = ExperimentRunner.RunReplications(baseConfig, 100);

            var pRej = AggregatedMetric.FromValues(baseReplications.Select(r => r.RejectionProbability));
            var uD1 = AggregatedMetric.FromValues(baseReplications.Select(r => r.UtilizationD1));
            var uD2 = AggregatedMetric.FromValues(baseReplications.Select(r => r.UtilizationD2));
            var uD3 = AggregatedMetric.FromValues(baseReplications.Select(r => r.UtilizationD3));
            var qSec = AggregatedMetric.FromValues(baseReplications.Select(r => r.AvgQueueSecondary));
            var qFull = AggregatedMetric.FromValues(baseReplications.Select(r => r.AvgQueueFull));

            Console.WriteLine($"\nРЕЗУЛЬТАТИ БАЗОВОЇ МОДЕЛІ (Середнє ± Ст.відхилення [Min; Max]):");
            Console.WriteLine($"P_відм (ймовірність відмови):  {pRej.Mean:F4} ± {pRej.StdDev:F4} [{pRej.Min:F4}; {pRej.Max:F4}]");
            Console.WriteLine($"Завантаження D1 (Первинне):    {uD1.Mean:F4} ± {uD1.StdDev:F4} [{uD1.Min:F4}; {uD1.Max:F4}]");
            Console.WriteLine($"Завантаження D2 (Вторинне):    {uD2.Mean:F4} ± {uD2.StdDev:F4} [{uD2.Min:F4}; {uD2.Max:F4}]");
            Console.WriteLine($"Завантаження D3 (Повне):       {uD3.Mean:F4} ± {uD3.StdDev:F4} [{uD3.Min:F4}; {uD3.Max:F4}]");
            Console.WriteLine($"Сер. черга вторинного регул.: {qSec.Mean:F2} ± {qSec.StdDev:F2} [{qSec.Min:F2}; {qSec.Max:F2}]");
            Console.WriteLine($"Сер. черга повного регул.:    {qFull.Mean:F2} ± {qFull.StdDev:F2} [{qFull.Min:F2}; {qFull.Max:F2}]");

            Console.WriteLine("\n================================================================================");
            Console.WriteLine("ДОСЛІДЖЕННЯ МІСТКОСТІ ВХІДНОГО НАКОПИЧУВАЧА N (100 реплікацій на кожну точку):");
            Console.WriteLine("N (шт)\t| P_відм (Mean ± Std)\t| Завант. D1\t| Завант. D3\t| Черга D3 (Full)");
            Console.WriteLine("--------------------------------------------------------------------------------");

            int[] testCapacities = { 0, 2, 4, 6, 8, 10, 12, 14, 16, 20, 30, 40, 50, 60, 80, 100, 120, 150, 200 };
            foreach (var n in testCapacities)
            {
                var cfg = new SimulationConfig
                {
                    SimTime = 5000.0,
                    MeanArrivalInterval = 30.0,
                    MeanPrimaryTime = 30.0,
                    MeanSecondaryTime = 30.0,
                    MeanFullTime = 100.0,
                    InputBufferCapacity = n
                };
                var reps = ExperimentRunner.RunReplications(cfg, 100);
                var p = AggregatedMetric.FromValues(reps.Select(r => r.RejectionProbability));
                var d1 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD1));
                var d3 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD3));
                var qf = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueFull));

                Console.WriteLine($"{n}\t| {p.Mean:F4} ± {p.StdDev:F4}\t| {d1.Mean:F4}\t| {d3.Mean:F4}\t| {qf.Mean:F2}");
            }
        }
    }
}
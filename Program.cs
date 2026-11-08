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
        EndPrimary,
        EndSecondary,
        EndFull
    }

    public class SimEvent
    {
        public double Time { get; }
        public EventType Type { get; }

        public SimEvent(double time, EventType type)
        {
            Time = time;
            Type = type;
        }
    }

    public class ServiceChannel
    {
        public string Name { get; }
        public double MeanServiceTime { get; set; }
        public int BatchSize { get; }
        public bool IsBusy { get; private set; }

        public List<Aggregate> CurrentBatch { get; } = new();
        public double TotalBusyTime { get; private set; }
        public int ProcessedBatchesCount { get; private set; }
        public int ProcessedAggregatesCount => ProcessedBatchesCount * BatchSize;

        private double _lastStateChangeTime;

        public ServiceChannel(string name, double meanServiceTime, int batchSize)
        {
            Name = name;
            MeanServiceTime = meanServiceTime;
            BatchSize = batchSize;
            IsBusy = false;
            TotalBusyTime = 0.0;
            ProcessedBatchesCount = 0;
            _lastStateChangeTime = 0.0;
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

        public ReplicationResult Run()
        {
            double currentTime = 0.0;
            int aggregateCounter = 0;
            var eventQueue = new PriorityQueue<SimEvent, double>();

            var channelPrimary = new ServiceChannel("Первинне", _config.MeanPrimaryTime, 2);
            var channelSecondary = new ServiceChannel("Вторинне", _config.MeanSecondaryTime, 2);
            var channelFull = new ServiceChannel("Повне", _config.MeanFullTime, 1);

            var inputQueue = new QueueTracker("Вхідний буфер");
            var bufferSucc = new QueueTracker("Буфер успішних");
            var bufferFail = new QueueTracker("Буфер відхилених");

            int totalArrivedPairs = 0;
            int totalRejectedPairs = 0;
            int completedAggregates = 0;

            double firstArrival = NextExp(_config.MeanArrivalInterval);
            eventQueue.Enqueue(new SimEvent(firstArrival, EventType.Arrival), firstArrival);

            while (eventQueue.Count > 0)
            {
                var ev = eventQueue.Dequeue();
                if (ev.Time > _config.SimTime)
                {
                    currentTime = _config.SimTime;
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
                            eventQueue.Enqueue(new SimEvent(currentTime + dur, EventType.EndPrimary), currentTime + dur);
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

                                if (!channelFull.IsBusy && bufferFail.Count >= channelFull.BatchSize)
                                {
                                    var item = bufferFail.Dequeue(currentTime);
                                    channelFull.StartService(new[] { item }, currentTime);
                                    double durFull = NextExp(channelFull.MeanServiceTime);
                                    eventQueue.Enqueue(new SimEvent(currentTime + durFull, EventType.EndFull), currentTime + durFull);
                                }
                            }
                        }

                        double nextArr = currentTime + NextExp(_config.MeanArrivalInterval);
                        eventQueue.Enqueue(new SimEvent(nextArr, EventType.Arrival), nextArr);
                        break;

                    case EventType.EndPrimary:
                        var pBatch = channelPrimary.CompleteService(currentTime);
                        foreach (var agg in pBatch)
                        {
                            agg.Status = AggregateStatus.PrimarySuccess;
                            bufferSucc.Enqueue(agg, currentTime);
                        }

                        if (inputQueue.Count >= channelPrimary.BatchSize)
                        {
                            var b1 = inputQueue.Dequeue(currentTime);
                            var b2 = inputQueue.Dequeue(currentTime);
                            channelPrimary.StartService(new[] { b1, b2 }, currentTime);
                            double dur = NextExp(channelPrimary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + dur, EventType.EndPrimary), currentTime + dur);
                        }

                        if (!channelSecondary.IsBusy && bufferSucc.Count >= channelSecondary.BatchSize)
                        {
                            var s1 = bufferSucc.Dequeue(currentTime);
                            var s2 = bufferSucc.Dequeue(currentTime);
                            channelSecondary.StartService(new[] { s1, s2 }, currentTime);
                            double durSec = NextExp(channelSecondary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + durSec, EventType.EndSecondary), currentTime + durSec);
                        }
                        break;

                    case EventType.EndSecondary:
                        var sBatch = channelSecondary.CompleteService(currentTime);
                        completedAggregates += sBatch.Count;

                        if (bufferSucc.Count >= channelSecondary.BatchSize)
                        {
                            var s1 = bufferSucc.Dequeue(currentTime);
                            var s2 = bufferSucc.Dequeue(currentTime);
                            channelSecondary.StartService(new[] { s1, s2 }, currentTime);
                            double durSec = NextExp(channelSecondary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + durSec, EventType.EndSecondary), currentTime + durSec);
                        }
                        break;

                    case EventType.EndFull:
                        var fBatch = channelFull.CompleteService(currentTime);
                        completedAggregates += fBatch.Count;

                        if (bufferFail.Count >= channelFull.BatchSize)
                        {
                            var item = bufferFail.Dequeue(currentTime);
                            channelFull.StartService(new[] { item }, currentTime);
                            double durFull = NextExp(channelFull.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + durFull, EventType.EndFull), currentTime + durFull);
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

            return new ReplicationResult
            {
                ArrivedPairs = totalArrivedPairs,
                RejectedPairs = totalRejectedPairs,
                RejectionProbability = totalArrivedPairs > 0 ? (double)totalRejectedPairs / totalArrivedPairs : 0.0,
                UtilizationD1 = channelPrimary.TotalBusyTime / currentTime,
                UtilizationD2 = channelSecondary.TotalBusyTime / currentTime,
                UtilizationD3 = channelFull.TotalBusyTime / currentTime,
                AvgQueueD1 = inputQueue.GetAverageLength(currentTime),
                MaxQueueD1 = inputQueue.MaxObserved,
                AvgQueueSecondary = bufferSucc.GetAverageLength(currentTime),
                MaxQueueSecondary = bufferSucc.MaxObserved,
                AvgQueueFull = bufferFail.GetAverageLength(currentTime),
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

            // 1. БАЗОВА МОДЕЛЬ (100 реплікацій за нерівністю Чебишева)
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

            // 2. СЕРІЯ ЕКСПЕРИМЕНТІВ ВАРІЮВАННЯ ВХІДНОГО БУФЕРА N
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("ДОСЛІДЖЕННЯ МІСТКОСТІ ВХІДНОГО НАКОПИЧУВАЧА N (100 реплікацій на кожну точку):");
            Console.WriteLine("N (шт)\t| P_відм (Mean ± Std)\t| Завант. D1\t| Завант. D3\t| Черга D3 (Full)");
            Console.WriteLine("--------------------------------------------------------------------------------");

            //int[] testCapacities = { 0, 2, 4, 6, 8, 10, 12, 14, 16 };
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
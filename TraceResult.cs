using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Course_work_7_Sem
{
    public record QueueSample(double Time, int Qin, int Qsucc, int Qfail, int InSystem);

    public class TraceResult
    {
        public ReplicationResult Base { get; set; } = new();
        public List<QueueSample> Samples { get; } = new();
        public List<double> TimesMain { get; } = new();
        public List<double> TimesFull { get; } = new();
        public List<double> WaitsIn { get; } = new();
        public List<double> WaitsSucc { get; } = new();
        public List<double> WaitsFail { get; } = new();
    }

    public class TraceSimulator
    {
        private readonly SimulationConfig _config;
        private readonly Random _rnd;
        private readonly double _traceStep;

        public TraceSimulator(SimulationConfig config, int seed, double traceStep = 10.0)
        {
            _config = config;
            _rnd = new Random(seed);
            _traceStep = traceStep;
        }

        private double NextExp(double mean)
        {
            return -mean * Math.Log(1.0 - _rnd.NextDouble());
        }

        public TraceResult Run()
        {
            var tr = new TraceResult();

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

            var enqTime = new Dictionary<int, double>();

            void Put(QueueTracker q, Aggregate a)
            {
                enqTime[a.Id] = currentTime;
                q.Enqueue(a, currentTime);
            }

            Aggregate Take(QueueTracker q, List<double> waits)
            {
                var a = q.Dequeue(currentTime);
                waits.Add(currentTime - enqTime[a.Id]);
                enqTime.Remove(a.Id);
                return a;
            }

            double nextSample = 0.0;

            QueueSample Snapshot(double t) => new QueueSample(
                t,
                inputQueue.Count,
                bufferSucc.Count,
                bufferFail.Count,
                inputQueue.Count + bufferSucc.Count + bufferFail.Count
                + channelPrimary.CurrentBatch.Count
                + channelSecondary.CurrentBatch.Count
                + channelFull.CurrentBatch.Count);

            double firstArrival = NextExp(_config.MeanArrivalInterval);
            eventQueue.Enqueue(new SimEvent(firstArrival, EventType.Arrival), firstArrival);

            while (eventQueue.Count > 0)
            {
                var ev = eventQueue.Dequeue();

                double evTime = Math.Min(ev.Time, _config.SimTime);
                while (nextSample <= evTime)
                {
                    tr.Samples.Add(Snapshot(nextSample));
                    nextSample += _traceStep;
                }

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
                                Put(inputQueue, a1);
                                Put(inputQueue, a2);
                            }
                            else
                            {
                                totalRejectedPairs++;
                                a1.Status = AggregateStatus.PrimaryRejected;
                                a2.Status = AggregateStatus.PrimaryRejected;
                                Put(bufferFail, a1);
                                Put(bufferFail, a2);

                                if (!channelFull.IsBusy && bufferFail.Count >= channelFull.BatchSize)
                                {
                                    var item = Take(bufferFail, tr.WaitsFail);
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
                            Put(bufferSucc, agg);
                        }

                        if (inputQueue.Count >= channelPrimary.BatchSize)
                        {
                            var b1 = Take(inputQueue, tr.WaitsIn);
                            var b2 = Take(inputQueue, tr.WaitsIn);
                            channelPrimary.StartService(new[] { b1, b2 }, currentTime);
                            double dur = NextExp(channelPrimary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + dur, EventType.EndPrimary), currentTime + dur);
                        }

                        if (!channelSecondary.IsBusy && bufferSucc.Count >= channelSecondary.BatchSize)
                        {
                            var s1 = Take(bufferSucc, tr.WaitsSucc);
                            var s2 = Take(bufferSucc, tr.WaitsSucc);
                            channelSecondary.StartService(new[] { s1, s2 }, currentTime);
                            double durSec = NextExp(channelSecondary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + durSec, EventType.EndSecondary), currentTime + durSec);
                        }
                        break;

                    case EventType.EndSecondary:
                        var sBatch = channelSecondary.CompleteService(currentTime);
                        completedAggregates += sBatch.Count;
                        foreach (var done in sBatch)
                            tr.TimesMain.Add(currentTime - done.ArrivalTime);

                        if (bufferSucc.Count >= channelSecondary.BatchSize)
                        {
                            var s1 = Take(bufferSucc, tr.WaitsSucc);
                            var s2 = Take(bufferSucc, tr.WaitsSucc);
                            channelSecondary.StartService(new[] { s1, s2 }, currentTime);
                            double durSec = NextExp(channelSecondary.MeanServiceTime);
                            eventQueue.Enqueue(new SimEvent(currentTime + durSec, EventType.EndSecondary), currentTime + durSec);
                        }
                        break;

                    case EventType.EndFull:
                        var fBatch = channelFull.CompleteService(currentTime);
                        completedAggregates += fBatch.Count;
                        foreach (var done in fBatch)
                            tr.TimesFull.Add(currentTime - done.ArrivalTime);

                        if (bufferFail.Count >= channelFull.BatchSize)
                        {
                            var item = Take(bufferFail, tr.WaitsFail);
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

            tr.Base = new ReplicationResult
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
            return tr;
        }
    }

    public static class TraceRunner
    {
        public static List<TraceResult> RunReplications(SimulationConfig config, int replicationsCount = 100, int startSeed = 1000)
        {
            var results = new List<TraceResult>();
            for (int i = 0; i < replicationsCount; i++)
            {
                var sim = new TraceSimulator(config, startSeed + i * 17);
                results.Add(sim.Run());
            }
            return results;
        }
    }

    public static class TraceCsvExporter
    {
        private static string F(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        public static void Export(string dir, string tag, List<TraceResult> reps)
        {
            //Directory.CreateDirectory(dir);
            //using var qs = new StreamWriter(Path.Combine(dir, $"queues_{tag}.csv"));
            //using var rs = new StreamWriter(Path.Combine(dir, $"routes_{tag}.csv"));
            //using var ws = new StreamWriter(Path.Combine(dir, $"waits_{tag}.csv"));
            //using var sm = new StreamWriter(Path.Combine(dir, $"summary_{tag}.csv"));

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
            string fullOutDir = Path.Combine(projectRoot, dir);

            Directory.CreateDirectory(fullOutDir);
            using var qs = new StreamWriter(Path.Combine(fullOutDir, $"queues_{tag}.csv"));
            using var rs = new StreamWriter(Path.Combine(fullOutDir, $"routes_{tag}.csv"));
            using var ws = new StreamWriter(Path.Combine(fullOutDir, $"waits_{tag}.csv"));
            using var sm = new StreamWriter(Path.Combine(fullOutDir, $"summary_{tag}.csv"));

            qs.WriteLine("rep,t,qin,qsucc,qfail,total");
            rs.WriteLine("rep,route,time");
            ws.WriteLine("rep,queue,wait");
            sm.WriteLine("rep,p_rej,u1,u2,u3,qin_avg,qin_max,qsucc_avg,qsucc_max,qfail_avg,qfail_max,arrived,rejected,completed");

            for (int i = 0; i < reps.Count; i++)
            {
                var t = reps[i];
                var r = t.Base;
                foreach (var s in t.Samples)
                    qs.WriteLine($"{i},{F(s.Time)},{s.Qin},{s.Qsucc},{s.Qfail},{s.InSystem}");
                foreach (var x in t.TimesMain) rs.WriteLine($"{i},D1-D2,{F(x)}");
                foreach (var x in t.TimesFull) rs.WriteLine($"{i},D3,{F(x)}");
                foreach (var x in t.WaitsIn) ws.WriteLine($"{i},Q_in,{F(x)}");
                foreach (var x in t.WaitsSucc) ws.WriteLine($"{i},Q_succ,{F(x)}");
                foreach (var x in t.WaitsFail) ws.WriteLine($"{i},Q_fail,{F(x)}");

                sm.WriteLine($"{i},{F(r.RejectionProbability)},{F(r.UtilizationD1)},{F(r.UtilizationD2)},{F(r.UtilizationD3)}," +
                             $"{F(r.AvgQueueD1)},{r.MaxQueueD1},{F(r.AvgQueueSecondary)},{r.MaxQueueSecondary}," +
                             $"{F(r.AvgQueueFull)},{r.MaxQueueFull},{r.ArrivedPairs},{r.RejectedPairs},{r.CompletedAggregates}");
            }
        }
    }

    internal class TraceProgram
    {
        static SimulationConfig MakeConfig(int n, double simTime = 5000.0)
        {
            return new SimulationConfig
            {
                SimTime = simTime,
                MeanArrivalInterval = 30.0,
                MeanPrimaryTime = 30.0,
                MeanSecondaryTime = 30.0,
                MeanFullTime = 100.0,
                InputBufferCapacity = n
            };
        }

        static double MeanOrNaN(IEnumerable<double> values)
        {
            var l = values.ToList();
            return l.Count > 0 ? l.Average() : double.NaN;
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            Console.WriteLine(">>> Самоперевірка: TraceSimulator проти B2Simulator");
            bool allOk = true;
            foreach (var n in new[] { 0, 4, 14 })
            {
                var cfg = MakeConfig(n);
                var orig = ExperimentRunner.RunReplications(cfg, 100);
                var traced = TraceRunner.RunReplications(cfg, 100).Select(t => t.Base).ToList();
                bool ok = true;
                for (int i = 0; i < orig.Count; i++)
                {
                    if (orig[i].ArrivedPairs != traced[i].ArrivedPairs
                        || orig[i].RejectedPairs != traced[i].RejectedPairs
                        || orig[i].CompletedAggregates != traced[i].CompletedAggregates
                        || Math.Abs(orig[i].UtilizationD3 - traced[i].UtilizationD3) > 1e-12
                        || Math.Abs(orig[i].AvgQueueFull - traced[i].AvgQueueFull) > 1e-12)
                    {
                        ok = false;
                        break;
                    }
                }
                Console.WriteLine($"    N = {n}: {(ok ? "OK" : "РОЗБІЖНІСТЬ")}");
                allOk &= ok;
            }
            if (!allOk)
            {
                Console.WriteLine("Результати не збігаються з оригіналом, дані для рисунків використовувати не можна.");
                return;
            }

            Console.WriteLine("\n================================================================================");
            Console.WriteLine("РОЗШИРЕНЕ ДОСЛІДЖЕННЯ N (100 реплікацій на кожну точку, T = 5000 хв):");
            Console.WriteLine("N (шт)\t| P_відм (Mean ± Std)\t| Без відмов, %\t| Завант. D1\t| Завант. D3\t| Черга D3\t| Q_in сер.\t| Q_in макс.");
            Console.WriteLine("--------------------------------------------------------------------------------");

            int[] capacities =
            {
                0, 2, 4, 6, 8, 10, 12, 14, 16,
                20, 30, 40, 50, 60, 80, 100, 120, 124, 150, 200
            };

            foreach (var n in capacities)
            {
                var reps = ExperimentRunner.RunReplications(MakeConfig(n), 100);
                var p = AggregatedMetric.FromValues(reps.Select(r => r.RejectionProbability));
                var d1 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD1));
                var d3 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD3));
                var qf = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueFull));
                var qin = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueD1));
                var qinMax = AggregatedMetric.FromValues(reps.Select(r => (double)r.MaxQueueD1));
                double noRej = 100.0 * reps.Count(r => r.RejectedPairs == 0) / reps.Count;

                Console.WriteLine(
                    $"{n}\t| {p.Mean:F4} ± {p.StdDev:F4}\t| {noRej:F0}\t| {d1.Mean:F4}\t| {d3.Mean:F4}\t| {qf.Mean:F2}\t| {qin.Mean:F2}\t| {qinMax.Mean:F2}");
            }

            Console.WriteLine("\n>>> Експорт даних для рисунків у папку out ...");
            var runs = new List<(int N, double T)>
            {
                (0, 5000.0),
                (4, 5000.0),
                (14, 5000.0),
                (14, 10000.0),
                (124, 5000.0)
            };

            foreach (var (n, t) in runs)
            {
                var reps = TraceRunner.RunReplications(MakeConfig(n, t), 100);
                TraceCsvExporter.Export("out", $"N{n}_T{(int)t}", reps);

                double qinAvg = reps.Average(x => x.Base.AvgQueueD1);
                double qinMax = reps.Average(x => (double)x.Base.MaxQueueD1);
                double qfMax = reps.Average(x => (double)x.Base.MaxQueueFull);
                double wSucc = MeanOrNaN(reps.SelectMany(x => x.WaitsSucc));
                double wFail = MeanOrNaN(reps.SelectMany(x => x.WaitsFail));
                double wIn = MeanOrNaN(reps.SelectMany(x => x.WaitsIn));
                double tMain = MeanOrNaN(reps.SelectMany(x => x.TimesMain));
                double tFull = MeanOrNaN(reps.SelectMany(x => x.TimesFull));

                Console.WriteLine($"    N = {n}, T = {t}:");
                Console.WriteLine($"        Q_in: сер. {qinAvg:F2}, макс. (середнє) {qinMax:F2}; Q_fail макс. (середнє) {qfMax:F2}");
                Console.WriteLine($"        сер. очікування, хв: Q_in {wIn:F2}; Q_succ {wSucc:F2}; Q_fail {wFail:F2}");
                Console.WriteLine($"        сер. час у системі, хв: D1->D2 {tMain:F2}; D3 {tFull:F2}");
            }
        }
    }
}
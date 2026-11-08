using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Course_work_7_Sem
{
    // Додаткові експерименти: верифікація варіюванням вхідних змінних,
    // потрібна місткість буфера за 1000 реплікацій та перевірка оцінки з п. 5.2.
    // Використовує класи основного файлу без змін.
    internal class ExtraProgram
    {
        static SimulationConfig Cfg(int n, double simTime = 5000.0, double arr = 30.0,
            double prim = 30.0, double sec = 30.0, double full = 100.0)
        {
            return new SimulationConfig
            {
                SimTime = simTime,
                MeanArrivalInterval = arr,
                MeanPrimaryTime = prim,
                MeanSecondaryTime = sec,
                MeanFullTime = full,
                InputBufferCapacity = n
            };
        }

        // Стаціонарна ймовірність відмови для D1 як системи M/M/1/K:
        // rho = mean_primary / mean_interarrival, K = 1 + N/2 партій.
        static double TheoryP(double rho, int n)
        {
            int k = 1 + n / 2;
            if (Math.Abs(rho - 1.0) < 1e-12) return 1.0 / (k + 1);
            return (1.0 - rho) * Math.Pow(rho, k) / (1.0 - Math.Pow(rho, k + 1));
        }

        static int Percentile(int[] sorted, double q)
        {
            int idx = (int)Math.Ceiling(q * sorted.Length) - 1;
            idx = Math.Clamp(idx, 0, sorted.Length - 1);
            return sorted[idx];
        }

        static int ToEven(int v) => v % 2 == 0 ? v : v + 1;

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            // ---------------------------------------------------------------
            // 1. Верифікація варіюванням вхідних змінних (N = 0)
            // ---------------------------------------------------------------
            Console.WriteLine("================================================================================");
            Console.WriteLine("1. ВЕРИФІКАЦІЯ ВАРІЮВАННЯМ ВХІДНИХ ЗМІННИХ (N = 0, T = 5000 хв, 100 реплікацій)");
            Console.WriteLine("P_теор = rho/(1+rho), rho = mean_primary/mean_interarrival;");
            Console.WriteLine("D1_теор = (1-P_теор)*rho; rho3_теор = 2*P_теор*mean_full/mean_interarrival (навантаження D3)");
            Console.WriteLine("z = (P_сим - P_теор)/SE; |z| <= 1.96 позначено OK (серед 8 тестів 0-1 випадкових '!' припустимі)");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("Експеримент\t| P сим ± SE\t| P теор\t| z\t| D1 сим\t| D1 теор\t| D2 сим\t| D3 сим\t| rho3 теор\t| Q_succ\t| Q_fail");

            var tests = new (string Name, double Arr, double Prim, double Sec, double Full)[]
            {
                ("базова (30/30/30/100)", 30, 30, 30, 100),
                ("mean_interarrival = 60", 60, 30, 30, 100),
                ("mean_interarrival = 15", 15, 30, 30, 100),
                ("mean_interarrival = 300", 300, 30, 30, 100),
                ("mean_primary_time = 15", 30, 15, 30, 100),
                ("mean_primary_time = 60", 30, 60, 30, 100),
                ("mean_secondary_time = 100", 30, 30, 100, 100),
                ("mean_full_time = 10", 30, 30, 30, 10),
            };

            foreach (var t in tests)
            {
                var reps = ExperimentRunner.RunReplications(Cfg(0, 5000.0, t.Arr, t.Prim, t.Sec, t.Full), 100);
                var p = AggregatedMetric.FromValues(reps.Select(r => r.RejectionProbability));
                var d1 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD1));
                var d2 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD2));
                var d3 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD3));
                var qs = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueSecondary));
                var qf = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueFull));

                double se = p.StdDev / Math.Sqrt(reps.Count);
                double rho = t.Prim / t.Arr;
                double pTh = TheoryP(rho, 0);
                double d1Th = (1.0 - pTh) * rho;
                double rho3Th = 2.0 * pTh * t.Full / t.Arr;
                double z = se > 0 ? (p.Mean - pTh) / se : double.NaN;
                string flag = Math.Abs(z) <= 1.96 ? "OK" : "!";

                Console.WriteLine(
                    $"{t.Name}\t| {p.Mean:F4} ± {se:F4}\t| {pTh:F4}\t| {z:F2} {flag}\t| {d1.Mean:F4}\t| {d1Th:F4}\t| {d2.Mean:F4}\t| {d3.Mean:F4}\t| {rho3Th:F2}\t| {qs.Mean:F2}\t| {qf.Mean:F2}");
            }
            Console.WriteLine("Очікуване невелике зміщення P_сим вниз (близько 0.002-0.003) через старт з порожнього стану.");
            Console.WriteLine("mean_secondary_time та mean_full_time не мають впливати на P_відм (порівняйте з базовою).");

            // ---------------------------------------------------------------
            // 2. Потрібна місткість буфера: розподіл максимальної довжини Q_in
            //    при практично нескінченному буфері (1000 реплікацій).
            //    Якщо максимум реплікації M, то відмов не буде при будь-якому N >= M.
            // ---------------------------------------------------------------
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("2. ПОТРІБНА МІСТКІСТЬ ВХІДНОГО НАКОПИЧУВАЧА (1000 реплікацій, буфер практично нескінченний)");
            Console.WriteLine("T, хв\t| сер. макс\t| медіана\t| 95%\t| 99%\t| 99.9%\t| макс\t| N для 99%\t| N для 99.9%\t| N для 100%");
            Console.WriteLine("--------------------------------------------------------------------------------");

            foreach (var T in new[] { 2500.0, 5000.0, 10000.0 })
            {
                var reps = ExperimentRunner.RunReplications(Cfg(1_000_000, T), 1000);
                var maxima = reps.Select(r => r.MaxQueueD1).ToArray();
                var sorted = maxima.OrderBy(x => x).ToArray();

                int p50 = Percentile(sorted, 0.50);
                int p95 = Percentile(sorted, 0.95);
                int p99 = Percentile(sorted, 0.99);
                int p999 = Percentile(sorted, 0.999);
                int pMax = sorted[^1];

                Console.WriteLine(
                    $"{T:F0}\t| {maxima.Average():F2}\t| {p50}\t| {p95}\t| {p99}\t| {p999}\t| {pMax}\t| {ToEven(p99)}\t| {ToEven(p999)}\t| {ToEven(pMax)}");

                if (Math.Abs(T - 5000.0) < 1e-9)
                {
                    // Самоперевірка за таблицею 4.2: перші 100 реплікацій мають ті самі сіди
                    var first = maxima.Take(100).ToArray();
                    Console.WriteLine($"    (T = 5000, перші 100 реплікацій: сер. макс. = {first.Average():F2}; у таблиці 4.2 для N >= 100 це 42.90)");
                    var expected = new Dictionary<int, int> { { 20, 14 }, { 40, 49 }, { 60, 81 }, { 80, 97 }, { 100, 100 } };
                    foreach (var kv in expected)
                    {
                        double share = 100.0 * first.Count(m => m <= kv.Key) / first.Length;
                        Console.WriteLine($"    N = {kv.Key}: частка реплікацій без відмов {share:F0}% (у таблиці 4.2: {kv.Value}%)");
                    }
                }
            }

            // ---------------------------------------------------------------
            // 3. Перевірка оцінки з п. 5.2: зменшення середнього часу первинного
            //    регулювання з 30 до 24 хв при N = 14
            // ---------------------------------------------------------------
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("3. ЗМЕНШЕННЯ ЧАСУ ПЕРВИННОГО РЕГУЛЮВАННЯ (N = 14, 100 реплікацій)");
            Console.WriteLine("T, хв\t| mean_primary\t| P сим ± Std\t| P теор (стац.)\t| D1\t| D3\t| rho3 теор (стац.)\t| Черга D3\t| Q_in сер.");
            Console.WriteLine("--------------------------------------------------------------------------------");

            foreach (var T in new[] { 5000.0, 10000.0 })
            {
                foreach (var prim in new[] { 30.0, 24.0 })
                {
                    var reps = ExperimentRunner.RunReplications(Cfg(14, T, 30.0, prim), 100);
                    var p = AggregatedMetric.FromValues(reps.Select(r => r.RejectionProbability));
                    var d1 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD1));
                    var d3 = AggregatedMetric.FromValues(reps.Select(r => r.UtilizationD3));
                    var qf = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueFull));
                    var qin = AggregatedMetric.FromValues(reps.Select(r => r.AvgQueueD1));

                    double pTh = TheoryP(prim / 30.0, 14);
                    double rho3Th = 2.0 * pTh * 100.0 / 30.0;

                    Console.WriteLine(
                        $"{T:F0}\t| {prim:F0}\t| {p.Mean:F4} ± {p.StdDev:F4}\t| {pTh:F4}\t| {d1.Mean:F4}\t| {d3.Mean:F4}\t| {rho3Th:F2}\t| {qf.Mean:F2}\t| {qin.Mean:F2}");
                }
            }
        }
    }
}
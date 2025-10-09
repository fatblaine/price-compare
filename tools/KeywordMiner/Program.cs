using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PriceCompareData.Data;
using PriceCompareData.Entities.Compare;

static class Csv
{
    public static void WriteAll(string path, IEnumerable<string[]> rows)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (var r in rows)
        {
            w.WriteLine(string.Join(",", r.Select(Escape)));
        }

        static string Escape(string s)
        {
            if (s is null) return "";
            return s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        }
    }
}

class App
{
    private readonly AppDbContext _db;
    private readonly Regex _tokenRe = new(@"[a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly HashSet<string> _stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","with","for","from","of","to","in","on","by","a","an",
        "save","down","special","new","pack","pk","x","ea","each",
        "coles","woolworths","ww","g","kg","ml","l"
    };

    public App(AppDbContext db) => _db = db;

    // -------------------------------------------------------------
    // Generate: 从 Product.Name 提取高频词、短语，输出 CSV
    // -------------------------------------------------------------
    public void Generate(string outCsv = "keyword_candidates.csv", int minWordFreq = 5, int minBigramFreq = 3)
    {
        Console.WriteLine("Loading product names from database...");
        var names = _db.Products
            .Select(p => p.Name)
            .Where(n => n != null && n != "")
            .AsNoTracking()
            .ToList();

        if (names.Count == 0)
        {
            Console.WriteLine("⚠️  No products found in database. Please ensure Products table is populated.");
            return;
        }

        var wordFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bigramFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var tokens = _tokenRe.Matches(name.ToLowerInvariant()).Select(m => m.Value).ToList();

            // 单词统计
            foreach (var t in tokens.Where(t => !_stop.Contains(t)).Distinct())
                wordFreq[t] = wordFreq.TryGetValue(t, out var c) ? c + 1 : 1;

            // 二元词组
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                var a = tokens[i]; var b = tokens[i + 1];
                if (_stop.Contains(a) || _stop.Contains(b)) continue;
                var bg = a + " " + b;
                bigramFreq[bg] = bigramFreq.TryGetValue(bg, out var c) ? c + 1 : 1;
            }
        }

        var rows = new List<string[]>();
        rows.Add(new[] { "Term", "Type", "Freq", "SuggestedCategoryId", "IsBrand(0|1)", "MultiWord(0|1)", "Weight(1-10)", "Notes" });

        foreach (var kv in wordFreq.Where(kv => kv.Value >= minWordFreq).OrderByDescending(kv => kv.Value).Take(2000))
            rows.Add(new[] { kv.Key, "word", kv.Value.ToString(), "", "0", "0", "1", "" });

        foreach (var kv in bigramFreq.Where(kv => kv.Value >= minBigramFreq).OrderByDescending(kv => kv.Value).Take(2000))
            rows.Add(new[] { kv.Key, "bigram", kv.Value.ToString(), "", "0", "1", "2", "" });

        Csv.WriteAll(outCsv, rows);
        Console.WriteLine($"✅ Generated CSV: {Path.GetFullPath(outCsv)}");
        Console.WriteLine("请打开 CSV，人工在 SuggestedCategoryId/IsBrand/MultiWord/Weight/Notes 列填写。保存后执行 import。");
    }

    // -------------------------------------------------------------
    // Import: 导入审核后的关键词 CSV → CategoryKeyword
    // -------------------------------------------------------------
    public int Import(string reviewedCsvPath)
    {
        if (!File.Exists(reviewedCsvPath))
        {
            Console.WriteLine($"❌ File not found: {reviewedCsvPath}");
            return 1;
        }

        Console.WriteLine("Loading existing CategoryKeywords from DB...");
        var existing = _db.Set<CategoryKeyword>()
            .AsNoTracking()
            .Select(k => new { k.CategoryId, k.Keyword })
            .ToList()
            .Select(x => (x.CategoryId, x.Keyword.ToLower()))
            .ToHashSet();

        var lines = File.ReadAllLines(reviewedCsvPath, Encoding.UTF8);
        int imported = 0, skipped = 0;

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cols = ParseCsvLine(line);
            if (cols.Length < 7) { skipped++; continue; }

            var term = cols[0]?.Trim();
            if (string.IsNullOrWhiteSpace(term)) { skipped++; continue; }

            if (!int.TryParse(cols[3], out var categoryId)) { skipped++; continue; }
            if (!int.TryParse(cols[6], out var weight)) weight = 1;
            weight = Math.Clamp(weight, 1, 10);

            if (existing.Contains((categoryId, term.ToLower())))
            {
                skipped++;
                continue;
            }

            _db.Set<CategoryKeyword>().Add(new CategoryKeyword
            {
                CategoryId = categoryId,
                Keyword = term,
                Weight = weight
            });
            imported++;
        }

        _db.SaveChanges();
        Console.WriteLine($"✅ Import done. Inserted: {imported}, Skipped: {skipped}");
        return 0;

        static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (ch == ',' && !inQuotes)
                {
                    result.Add(sb.ToString()); sb.Clear();
                }
                else sb.Append(ch);
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }
    }
}

// -------------------------------------------------------------
// Program Entry
// -------------------------------------------------------------
class Program
{
    static int Main(string[] args)
    {
        try
        {
            // ✅ 强制配置加载路径为程序目录
            var basePath = AppContext.BaseDirectory;
            Console.WriteLine($"[Config] Base path: {basePath}");

            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "KEYWORDMINER_")
                .Build();

            var conn = config.GetConnectionString("Default");
            Console.WriteLine($"[Config] Connection: {(string.IsNullOrEmpty(conn) ? "(not found)" : conn.Substring(0, Math.Min(50, conn.Length)) + "...")}");

            if (string.IsNullOrWhiteSpace(conn))
            {
                Console.WriteLine("❌ Connection string not found. Please configure in appsettings.json or environment variable KEYWORDMINER_ConnectionStrings__Default.");
                return 2;
            }

            // ✅ 使用 PostgreSQL Provider
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(conn)
                .Options;

            using var db = new AppDbContext(options);
            var app = new App(db);

            if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run -- generate                       生成 keyword_candidates.csv");
                Console.WriteLine("  dotnet run -- import <reviewed.csv>          导入已标注 CSV 到 CategoryKeywords");
                return 0;
            }

            var cmd = args[0].ToLowerInvariant();

            switch (cmd)
            {
                case "generate":
                    app.Generate();
                    return 0;
                case "import":
                    if (args.Length >= 2)
                    {
                        return app.Import(args[1]);
                    }
                    Console.WriteLine("❌ Missing parameter <reviewed.csv>");
                    return 1;
                default:
                    Console.WriteLine("❌ Unknown command. Use: generate / import");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ Failed: " + ex.Message);
            Console.WriteLine(ex);
            return 9;
        }
    }
}

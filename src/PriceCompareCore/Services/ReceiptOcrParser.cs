using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PriceCompareData.Entities.Receipts;

namespace PriceCompareCore.Services
{
    public class ReceiptOcrParser
    {
        /// <summary>
        /// Identify the store name from OCR text lines.
        /// For supermarket receipts: prioritize matching common keywords (Coles / Woolworths), otherwise look for uppercase text in the first few lines.
        /// </summary>
        public string TryDetectStoreName(List<OcrTextLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }
            int maxCheck = lines.Count;
            if (maxCheck > 15)
            {
                maxCheck = 15; // Only check the first 15 lines
            }

            // 1. Prioritize matching common supermarket names
            for (int i = 0; i < maxCheck; i++)
            {
                string text = lines[i].Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string lower = text.ToLowerInvariant();

                if (lower.Contains("coles"))
                {
                    return "Coles";
                }

                if (lower.Contains("woolworths"))
                {
                    return "Woolworths";
                }
            }

            // 2. If no match found, look for a line that is all uppercase
            for (int i = 0; i < maxCheck; i++)
            {
                string text = lines[i].Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string trimmed = text.Trim();
                if (trimmed.Length <= 2)
                {
                    continue;
                }

                string upper = trimmed.ToUpperInvariant();
                if (upper == trimmed)
                {
                    return trimmed;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// datetime
        ///  - 24/10/2025
        ///  - 24/10/25
        ///  - 24/10/2025 18:37
        ///  - 24/10/25 18:37
        ///  - 20-06-2025 
        /// </summary>
        public DateTime? TryDetectPurchaseDate(List<OcrTextLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            string[] formats = new string[]
            {
                "dd/MM/yyyy",
                "dd/MM/yy",
                "dd/MM/yyyy HH:mm",
                "dd/MM/yy HH:mm",
                "dd-MM-yyyy",
                "dd-MM-yy",
                "dd-MM-yyyy HH:mm",
                "dd-MM-yy HH:mm"
            };

            DateTime parsed;
            int count = lines.Count;

            for (int i = 0; i < count; i++)
            {
                string text = lines[i].Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string trimmed = text.Trim();

                // 
                if (DateTime.TryParse(trimmed, out parsed))
                {
                    return parsed;
                }

                // 
                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int pCount = parts.Length;

                for (int j = 0; j < pCount; j++)
                {
                    string token = parts[j];

                    if (j + 1 < pCount)
                    {
                        string combined = token + " " + parts[j + 1];
                        if (TryParseWithFormats(combined, formats, out parsed))
                        {
                            return parsed;
                        }
                    }

                    if (TryParseWithFormats(token, formats, out parsed))
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        private bool TryParseWithFormats(string text, string[] formats, out DateTime result)
        {
            for (int i = 0; i < formats.Length; i++)
            {
                if (DateTime.TryParseExact(
                    text,
                    formats[i],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out result))
                {
                    return true;
                }
            }

            result = DateTime.MinValue;
            return false;
        }

        /// <summary>
        /// parse items from OCR text lines
        /// 通用策略（适配 Coles + Woolworths）：
        ///  1. 维护一个 inItemsSection 标志，判断是否进入“商品区”
        ///  2. Woolworths：商品描述行 + 下一行 "Qty ..." 是价格行
        ///  3. Coles：一行内直接是 "商品名   3.50" 这样的形式
        ///  4. 遇到 "TOTAL", "SUBTOTAL", "EFT", "GST", "Total savings" 等停止
        /// </summary>
        public List<ReceiptItem> ExtractItems(List<OcrTextLine> lines)
        {
            List<ReceiptItem> items = new List<ReceiptItem>();

            if (lines == null || lines.Count == 0)
            {
                return items;
            }

            int count = lines.Count;

            bool inItemsSection = false;        // 是否已经进入“商品区”
            string pendingColesName = null;     // Coles：待配对的商品名
            string pendingWooliesDesc = null;   // Woolworths：待配对的商品描述
            bool seenDescriptionHeader = false; // Coles：是否已经看到 Description 标记

            for (int i = 0; i < count; i++)
            {
                string originalText = lines[i].Text;
                if (string.IsNullOrWhiteSpace(originalText))
                {
                    continue;
                }

                string text = originalText.Trim();
                text = text.TrimStart(',', '.', '-'); // 去掉票据行前的杂字符
                // 有些 ^ 被 OCR 成单字符前缀（如 "n "、"A "），去掉首个单字符 token
                var firstToken = text.Split(' ').FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstToken) && firstToken.Length == 1 && text.Length > 2)
                {
                    text = text.Substring(firstToken.Length).TrimStart();
                }
                string lower = text.ToLowerInvariant();

                bool IsPriceOnly(string s)
                {
                    var token = s.Trim().TrimStart('$');
                    return Decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
                }

                bool IsNoise(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return true;
                    var t = s.Trim().ToLowerInvariant();
                    return t == "each" || t == "$" || t == "@";
                }

                bool IsLikelyProductLine(string s)
                {
                    // 有字母且不是纯价格/each/subtotal/total 等
                    if (string.IsNullOrWhiteSpace(s)) return false;
                    if (!s.Any(char.IsLetter)) return false;
                    if (IsPriceOnly(s)) return false;
                    var l = s.ToLowerInvariant();
                    if (l.StartsWith("total") || l.StartsWith("subtotal") || l.StartsWith("eft") || l.StartsWith("gst")) return false;
                    if (l.Contains("description")) return false;
                    if (l.Contains("tax invoice") || l.Contains("abn") || l.Contains("ph:")) return false;
                    return true;
                }

                bool IsHeaderLine(string s)
                {
                    var l = s.ToLowerInvariant();
                    return l.Contains("store ") || l.Contains("store:") ||
                           l.Contains("manager") || l.Contains("served by") ||
                           l.Contains("register") || l.Contains("receipt") ||
                           l.Contains("tax invoice") || l.Contains("phone") ||
                           l.Contains("time:") || l.Contains("date:") ||
                           l.Contains("coles supermarkets");
                }

                bool TryParsePriceLine(string s, out decimal price)
                {
                    // 清洗：去掉货币符号、空格、逗号，保留数字和点
                    var cleaned = Regex.Replace(s, @"[^\d\.]", " ");
                    cleaned = string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    // 取最后一个看起来像金额的 token
                    var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int idx = tokens.Length - 1; idx >= 0; idx--)
                    {
                        var token = tokens[idx];
                        if (!token.Contains('.')) continue; // 必须有小数点，避免整数（时间/编号）
                        if (decimal.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price))
                        {
                            return true;
                        }
                    }
                    price = 0m;
                    return false;
                }

                bool TryParsePriceFromLines(int startIndex, out decimal price, out int consumedLines)
                {
                    // 尝试跨 1-2 行拼接价格（应对 "3.50 5." / "00" 这类被拆开的 OCR）
                    consumedLines = 0;
                    price = 0m;
                    var sb = new List<string>();
                    for (int k = startIndex; k < Math.Min(startIndex + 2, count); k++)
                    {
                        sb.Add(lines[k].Text ?? string.Empty);
                        var joined = string.Join(" ", sb);
                        if (TryParsePriceLine(joined, out price) && price > 0m && price < 1000m)
                        {
                            consumedLines = k - startIndex;
                            return true;
                        }
                        // 兜底：移除空格后查找形如 21.91、1.65 的模式
                        var collapsed = Regex.Replace(joined, @"\s+", string.Empty);
                        var m = Regex.Match(collapsed, @"\d+\.\d{1,2}");
                        if (m.Success && decimal.TryParse(m.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price) && price > 0m && price < 1000m)
                        {
                            consumedLines = k - startIndex;
                            return true;
                        }
                    }
                    return false;
                }

                // 1. 识别进入商品区的条件
                if (!inItemsSection)
                {
                    // Coles：宽松识别 Description（考虑被拆分/拼写偏差）
                    var compact = lower.Replace(" ", "");
                    if (compact.Contains("description") || compact.Contains("descriptption") || compact.StartsWith("descript"))
                    {
                        inItemsSection = true;
                        seenDescriptionHeader = true;
                        pendingColesName = null;
                        pendingWooliesDesc = null;
                        continue;
                    }

                    // Woolworths：第一次遇到 "qty " 或 "qty" 也认为进入商品区
                    if (lower.StartsWith("qty ") || lower == "qty")
                    {
                        inItemsSection = true;
                    }
                    else if (IsLikelyProductLine(text))
                    {
                        // Coles：即便未识别到 Description 也允许通过商品行入场
                        inItemsSection = true;
                        pendingColesName = text;
                        pendingWooliesDesc = text;
                        continue;
                    }
                }

                // 如果还没进入商品区，跳过
                if (!inItemsSection)
                {
                    continue;
                }

                // 跳过表头/收据信息行（防止将其当成商品）
                if (IsHeaderLine(text))
                {
                    continue;
                }

                // 2. 商品区结束条件：遇到各种 "Total..."、"Subtotal"、"EFT"、"GST" 等
                if (lower.StartsWith("total for") ||
                    lower.StartsWith("total ") ||
                    lower.StartsWith("subtotal") ||
                    lower.StartsWith("eft") ||
                    lower.StartsWith("gst ") ||
                    lower.StartsWith("you saved") ||
                    lower.StartsWith("total savings") ||
                    lower.StartsWith("specials"))
                {
                    break;
                }

                // 2.x 如果已有 pendingColesName，尝试在当前行/后续一行中解析价格（Coles 被拆成单独的金额）
                if (!string.IsNullOrWhiteSpace(pendingColesName) &&
                    TryParsePriceFromLines(i, out var pendingPrice, out var consumed))
                {
                    items.Add(new ReceiptItem
                    {
                        ProductName = pendingColesName.Trim(),
                        Quantity = 1,
                        Price = pendingPrice,
                        Confidence = lines[i].Confidence
                    });
                    pendingColesName = null;
                    i += consumed;
                    continue;
                }

                // 2.y 如果当前行看起来是价格，且下一行是商品描述（OCR 顺序颠倒），也尝试配对
                if (string.IsNullOrWhiteSpace(pendingColesName) &&
                    TryParsePriceFromLines(i, out var priceAhead, out var consumedPrice) &&
                    i + 1 + consumedPrice < count &&
                    IsLikelyProductLine(lines[i + 1 + consumedPrice].Text) &&
                    !IsHeaderLine(lines[i + 1 + consumedPrice].Text))
                {
                    var name = lines[i + 1 + consumedPrice].Text.Trim();
                    items.Add(new ReceiptItem
                    {
                        ProductName = name,
                        Quantity = 1,
                        Price = priceAhead,
                        Confidence = lines[i + 1 + consumedPrice].Confidence
                    });
                    i += consumedPrice + 1;
                    continue;
                }

                // 2.1 Woolworths：当前行单独为 "Qty"，下一行才有 "10 @ $3.95"
                if (lower == "qty")
                {
                    if (i + 1 < count)
                    {
                        var merged = "Qty " + lines[i + 1].Text.Trim();
                        var wooliesItem = ParseWoolworthsQtyLine(merged, lines[i].Confidence, pendingWooliesDesc);
                        if (wooliesItem != null)
                        {
                            items.Add(wooliesItem);
                            pendingWooliesDesc = null;
                        }
                    }
                    continue;
                }

                // 3. Woolworths 模式：处理 "Qty ..." 行
                if (lower.StartsWith("qty "))
                {
                    ReceiptItem wooliesItem = ParseWoolworthsQtyLine(text, lines[i].Confidence, pendingWooliesDesc);
                    if (wooliesItem != null)
                    {
                        items.Add(wooliesItem);
                        pendingWooliesDesc = null;
                    }
                    continue;
                }

                // 4. Coles 模式：一行包含商品和价格，比如 "* BLUEBERRIES 170GRAM    3.50"

                // 行首可能有 *, %, ^ 等前缀，表示 special / taxable
                if (text.StartsWith("^"))
                {
                    // Woolworths 特价标记
                    text = text.Substring(1).TrimStart();
                    lower = text.ToLowerInvariant();
                }

                if (text.StartsWith("*"))
                {
                    text = text.Substring(1).TrimStart();
                    lower = text.ToLowerInvariant();
                }

                if (text.StartsWith("%"))
                {
                    text = text.Substring(1).TrimStart();
                    lower = text.ToLowerInvariant();
                }

                // Woolworths 场景：行形如 "10 @ $3.95" 但不带 Qty 前缀
                var qtyPriceItem = TryParseQtyAtPrice(text, lines[i].Confidence, pendingWooliesDesc);
                if (qtyPriceItem != null)
                {
                    items.Add(qtyPriceItem);
                    pendingWooliesDesc = null;
                    continue;
                }

                // Coles 风格：尝试按末尾价格解析
                // 如果行里包含 @，避免当作单行商品解析
                ReceiptItem colesItem = text.Contains("@")
                    ? null
                    : ParseColesLine(text, lines[i].Confidence);
                if (colesItem != null)
                {
                    items.Add(colesItem);
                    pendingColesName = colesItem.ProductName;
                    pendingWooliesDesc = colesItem.ProductName;
                }
                else
                {
                    // 仅当行像商品描述时才更新 pending 名称，避免被“98.75”“each”覆盖
                    if (IsLikelyProductLine(text) && !IsNoise(text))
                    {
                        pendingColesName = text;
                        pendingWooliesDesc = text;
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// 解析 Coles 单行商品，如 "BLUEBERRIES 170GRAM    3.50"
        /// 解析失败返回 null。
        /// </summary>
        private ReceiptItem? ParseColesLine(string text, float confidence)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // 先用正则提取末尾金额（允许 $ 前缀，1-3 位小数）
            var match = Regex.Match(text, @"^(?<name>.+?)\s+\$?(?<price>\d+\.\d{1,3})$");
            if (!match.Success)
            {
                return null;
            }

            var name = match.Groups["name"].Value.Trim();
            var pricePart = match.Groups["price"].Value.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (!decimal.TryParse(pricePart, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) ||
                price <= 0m)
            {
                return null;
            }

            return new ReceiptItem
            {
                ProductName = name,
                Price = price,
                Quantity = 1,
                Confidence = confidence
            };
        }

        /// <summary>
        /// 解析 Woolworths 的 Qty 行，例如：
        /// "Qty 25 @ $3.95 each          98.75"
        ///
        /// 规则：
        ///  - 数量 = 25
        ///  - 单价 = 3.95
        ///  - 商品名来源于上一行（lastDescriptionLine）
        /// 解析失败返回 null。
        /// </summary>
        private ReceiptItem ParseWoolworthsQtyLine(string text, float confidence, string lastDescriptionLine)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(lastDescriptionLine))
            {
                // 没有对应的商品描述行，就不生成 item
                return null;
            }

            string working = text.Trim();

            // 去掉开头的 "Qty" 或 "QTY"
            if (working.StartsWith("Qty ", StringComparison.OrdinalIgnoreCase))
            {
                working = working.Substring(3).TrimStart();
            }

            string[] parts = working.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            int quantity;
            decimal unitPrice = 0m;

            // 第一个 token 尝试解析数量
            var qtyToken = parts[0].Trim().TrimEnd('@');
            if (!Int32.TryParse(qtyToken, out quantity))
            {
                return null;
            }

            // 查找形如 "$3.95" 的 token 作为单价
            for (int i = 1; i < parts.Length; i++)
            {
                string token = parts[i];
                if (token.StartsWith("$"))
                {
                    string priceText = token.Substring(1).Trim();
                    decimal parsed;
                    bool ok = Decimal.TryParse(
                        priceText,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out parsed);

                    if (ok && parsed > 0m)
                    {
                        unitPrice = parsed;
                        break;
                    }
                }
            }

            if (quantity <= 0 || unitPrice <= 0m)
            {
                return null;
            }

            ReceiptItem item = new ReceiptItem();
            item.ProductName = lastDescriptionLine.Trim();
            item.Quantity = quantity;
            item.Price = unitPrice;   // 这里把 Price 定义为单价
            item.Confidence = confidence;

            return item;
        }

        private ReceiptItem? TryParseQtyAtPrice(string text, float confidence, string pendingDescription)
        {
            if (string.IsNullOrWhiteSpace(pendingDescription))
            {
                return null;
            }

            var match = Regex.Match(text, @"^(?<qty>\d+)\s*@\s*\$?(?<price>[\d.,]+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return null;
            }

            if (!int.TryParse(match.Groups["qty"].Value, out var qty) || qty <= 0)
            {
                return null;
            }

            var priceToken = match.Groups["price"].Value.Replace(",", string.Empty);
            if (!decimal.TryParse(priceToken, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var unitPrice) ||
                unitPrice <= 0m)
            {
                return null;
            }

            return new ReceiptItem
            {
                ProductName = pendingDescription.Trim(),
                Quantity = qty,
                Price = unitPrice,
                Confidence = confidence
            };
        }
    }
}

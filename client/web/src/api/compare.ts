import axios from "axios";

function read<T = any>(obj: any, ...keys: string[]): T | undefined {
  for (const k of keys) {
    if (obj && Object.prototype.hasOwnProperty.call(obj, k)) return obj[k] as T;
  }
  return undefined;
}

export interface CompareProduct {
  productId?: string;
  name: string;
  brand?: string | null;
  sizeValue?: number | null;
  sizeUnit?: string | null;
  size?: string | null;
  shopType: number;
  price?: number | null;
  pricePerUnit?: number | null;
}

export interface CompareMatches {
  source: CompareProduct;
  targets: CompareProduct[];
}

function normalizeProduct(raw: any): CompareProduct {
  const name = read<string>(raw, "name", "Name") ?? "";
  const price = read<number>(raw, "price", "Price", "currentPrice", "CurrentPrice");
  const productId = read<string>(raw, "productId", "ProductId");
  const shopType = Number(read<number | string>(raw, "shopType", "ShopType")) as number;
  const brand = read<string | null>(raw, "brand", "Brand") ?? null;
  const sizeExisting = read<string | null>(raw, "size", "Size");
  let size = sizeExisting ?? null;
  const sizeValue = read<number | null>(raw, "sizeValue", "SizeValue") ?? null;
  const sizeUnit = read<string | null>(raw, "sizeUnit", "SizeUnit") ?? null;
  if (!size) {
    if (sizeValue != null && sizeUnit) size = `${sizeValue} ${sizeUnit}`;
    else if (sizeValue != null) size = String(sizeValue);
    else if (sizeUnit) size = sizeUnit;
  }
  const pricePerUnit = read<number | null>(raw, "pricePerUnit", "PricePerUnit") ?? null;
  return { productId, name, brand, sizeValue, sizeUnit, size, shopType, price: price ?? null, pricePerUnit };
}

export async function fetchCompareMatches(keyword: string, sourceShop: number): Promise<CompareMatches | null> {
  const params: any = { keyword, sourceShop };
  const res = await axios.get("/api/Compare", { params });
  const data = res.data ?? {};
  const matches: any[] = read<any[]>(data, "matches", "Matches") ?? [];
  if (!Array.isArray(matches) || matches.length === 0) return null;

  // pick the match whose source name equals the keyword (case-insensitive), otherwise first
  const lowered = String(keyword || "").toLowerCase();
  let match: any = matches.find((m) => {
    const src = read<any>(m, "source", "Source");
    const srcName = src ? (read<string>(src, "name", "Name") ?? "") : "";
    return srcName.toLowerCase() === lowered;
  }) ?? matches[0];

  const rawSource = read<any>(match, "source", "Source");
  const rawTargets = read<any[]>(match, "targets", "Targets") ?? [];
  const source = normalizeProduct(rawSource);
  const targets = rawTargets.map(normalizeProduct);
  return { source, targets };
}

// Price history types and helpers
export interface PriceHistoryPoint {
  scrapedAt: string; // ISO date time
  currentPrice: number;
}

export async function fetchPriceHistory(name: string, shopType: number, offerType: number): Promise<PriceHistoryPoint[]> {
  const params = { name, shopType, offerType };
  const res = await axios.get("/api/Scraping/priceHistory", { params });
  const arr = Array.isArray(res.data) ? res.data : [];
  return arr
    .map((it: any) => ({
      scrapedAt: read<string>(it, "scrapedAt", "ScrapedAt")!,
      currentPrice: Number(read<number>(it, "currentPrice", "CurrentPrice")),
    }))
    .filter((p) => p.scrapedAt && Number.isFinite(p.currentPrice));
}

export async function fetchMergedHistory(name: string, shopType: number): Promise<PriceHistoryPoint[]> {
  const [a, b] = await Promise.allSettled([
    fetchPriceHistory(name, shopType, 0),
    fetchPriceHistory(name, shopType, 1),
  ]);
  const list: PriceHistoryPoint[] = [];
  if (a.status === "fulfilled") list.push(...a.value);
  if (b.status === "fulfilled") list.push(...b.value);
  // dedupe by timestamp; keep the lowest price for the same timestamp
  const map = new Map<string, number>();
  for (const p of list) {
    const key = p.scrapedAt;
    const existing = map.get(key);
    if (existing == null || p.currentPrice < existing) map.set(key, p.currentPrice);
  }
  return Array.from(map.entries())
    .map(([scrapedAt, currentPrice]) => ({ scrapedAt, currentPrice }))
    .sort((x, y) => new Date(x.scrapedAt).getTime() - new Date(y.scrapedAt).getTime());
}

export interface PairedSeriesPoint {
  date: string; // formatted date
  source?: number | null;
  target?: number | null;
}

export async function buildPairedSeries(source: { name: string; shopType: number }, target: { name: string; shopType: number }): Promise<PairedSeriesPoint[]> {
  const [srcHist, tgtHist] = await Promise.all([
    fetchMergedHistory(source.name, source.shopType),
    fetchMergedHistory(target.name, target.shopType),
  ]);

  const byDay = new Map<string, { source?: number; target?: number }>();

  const add = (list: PriceHistoryPoint[], key: "source" | "target") => {
    for (const p of list) {
      const day = new Date(p.scrapedAt);
      const label = day.toLocaleDateString();
      const slot = byDay.get(label) ?? {};
      (slot as any)[key] = p.currentPrice;
      byDay.set(label, slot);
    }
  };
  add(srcHist, "source");
  add(tgtHist, "target");

  return Array.from(byDay.entries())
    .map(([date, v]) => ({ date, source: v.source ?? null, target: v.target ?? null }))
    .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
}

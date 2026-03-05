import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || ""; // e.g. https://lhpjjzns6f.execute-api.ap-southeast-2.amazonaws.com

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
	matchType?: string | null;
}

export interface CompareMatches {
	source: CompareProduct;
	targets: CompareProduct[];
}

function normalizeProduct(raw: any): CompareProduct {
	const name = read<string>(raw, "name", "Name") ?? "";
	const price = read<number>(
		raw,
		"price",
		"Price",
		"latestPrice",
		"LatestPrice",
		"currentPrice",
		"CurrentPrice",
	);
	const productId = read<string>(raw, "productId", "ProductId");
	const shopTypeRaw = read<number | string>(raw, "shopType", "ShopType");
	const shopTypeNum = Number(shopTypeRaw);
	const shopType = Number.isFinite(shopTypeNum) ? shopTypeNum : NaN;
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
	const pricePerUnit =
		read<number | null>(raw, "pricePerUnit", "PricePerUnit") ?? null;
	return {
		productId,
		name,
		brand,
		sizeValue,
		sizeUnit,
		size,
		shopType,
		price: price ?? null,
		pricePerUnit,
	};
}

function normalizeMatchCandidate(raw: any): CompareProduct {
	const targetRaw = read<any>(raw, "target", "Target") ?? raw;
	const product = normalizeProduct(targetRaw);
	const latestPrice = read<number>(raw, "latestPrice", "LatestPrice");
	const pricePerUnit = read<number>(raw, "pricePerUnit", "PricePerUnit");
	const matchType = read<string | null>(raw, "matchType", "MatchType") ?? null;
	return {
		...product,
		price: latestPrice ?? product.price ?? null,
		pricePerUnit: pricePerUnit ?? product.pricePerUnit ?? null,
		matchType,
	};
}

export async function fetchCompareMatches(
	keyword: string,
	sourceShop: number,
): Promise<CompareMatches | null> {
	const params: any = { keyword, sourceShop };
	const url = `${API_BASE}/api/compare-cached`;
	const res = await axios.get(url, { params });
	const data = res.data ?? {};
	const matches: any[] = read<any[]>(data, "matches", "Matches") ?? [];
	if (!Array.isArray(matches) || matches.length === 0) return null;

	// pick the match whose source name equals the keyword (case-insensitive), otherwise first
	const lowered = String(keyword || "").toLowerCase();
	let match: any =
		matches.find((m) => {
			const src = read<any>(m, "source", "Source");
			const srcName = src ? (read<string>(src, "name", "Name") ?? "") : "";
			return srcName.toLowerCase() === lowered;
		}) ?? matches[0];

	const rawSource = read<any>(match, "source", "Source");
	const rawTargets = read<any[]>(match, "targets", "Targets") ?? [];
	const source = normalizeProduct(rawSource);
	const targets = rawTargets
		.map(normalizeMatchCandidate)
		.filter((t) => !Number.isFinite(source.shopType) || t.shopType !== source.shopType);
	return { source, targets };
}

export async function fetchCompareMatchesByProduct(
	sourceProductId: string,
): Promise<CompareProduct[]> {
	const params: any = { sourceProductId };
	const url = `${API_BASE}/api/compare-cached/by-product`;
	const res = await axios.get(url, { params });
	const data = res.data ?? {};
	const matches: any[] = read<any[]>(data, "matches", "Matches") ?? [];
	if (!Array.isArray(matches) || matches.length === 0) return [];
	return matches.map(normalizeMatchCandidate);
}

// Price history types and helpers
export interface PriceHistoryPoint {
	scrapedAt: string; // ISO date time
	currentPrice: number;
	promoText?: string | null;
}

export async function fetchPriceHistory(
	name: string,
	shopType: number,
	offerType?: number,
): Promise<PriceHistoryPoint[]> {
	const params: any = { name, shopType };
	if (offerType != null && Number.isFinite(offerType)) {
		params.offerType = offerType;
	}
	const url = `${API_BASE}/api/Scraping/priceHistory`;
	const res = await axios.get(url, { params });
	const arr = Array.isArray(res.data) ? res.data : [];
	return arr
		.map((it: any) => ({
			scrapedAt: read<string>(it, "scrapedAt", "ScrapedAt")!,
			currentPrice: Number(read<number>(it, "currentPrice", "CurrentPrice")),
			promoText: read<string | null>(it, "promoText", "PromoText") ?? null,
		}))
		.filter((p) => p.scrapedAt && Number.isFinite(p.currentPrice));
}

export interface MergedHistoryResult {
	points: PriceHistoryPoint[];
	latestPromoText?: string | null;
}

function getLatestPromoText(list: PriceHistoryPoint[]): string | null {
	for (let i = list.length - 1; i >= 0; i--) {
		const candidate = list[i]?.promoText;
		if (candidate && candidate.trim()) return candidate;
	}
	return null;
}

export async function fetchMergedHistory(
	name: string,
	shopType: number,
): Promise<MergedHistoryResult> {
	const list = await fetchPriceHistory(name, shopType);
	const latestPromoText = getLatestPromoText(list);
	// dedupe by timestamp; keep the lowest price for the same timestamp
	const map = new Map<string, number>();
	for (const p of list) {
		const key = p.scrapedAt;
		const existing = map.get(key);
		if (existing == null || p.currentPrice < existing) {
			map.set(key, p.currentPrice);
		}
	}
	const points = Array.from(map.entries())
		.map(([scrapedAt, currentPrice]) => ({ scrapedAt, currentPrice }))
		.sort(
			(x, y) =>
				new Date(x.scrapedAt).getTime() - new Date(y.scrapedAt).getTime(),
		);
	return { points, latestPromoText };
}

export interface PairedSeriesPoint {
	date: string; // formatted date
	source?: number | null;
	target?: number | null;
}

export interface PairedSeriesResult {
	series: PairedSeriesPoint[];
	sourcePromoText?: string | null;
	targetPromoText?: string | null;
}

export async function buildPairedSeries(
	source: { name: string; shopType: number },
	target: { name: string; shopType: number },
): Promise<PairedSeriesResult> {
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
	add(srcHist.points, "source");
	add(tgtHist.points, "target");

	const series = Array.from(byDay.entries())
		.map(([date, v]) => ({
			date,
			source: v.source ?? null,
			target: v.target ?? null,
		}))
		.sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
	return {
		series,
		sourcePromoText: srcHist.latestPromoText ?? null,
		targetPromoText: tgtHist.latestPromoText ?? null,
	};
}

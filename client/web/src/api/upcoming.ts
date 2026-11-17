import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";

// The endpoint takes at most 50 ids; a page of 50 cards plus their other-store matches can need 100.
const MAX_BATCH = 50;

function read<T = any>(obj: any, ...keys: string[]): T | undefined {
	for (const k of keys) {
		if (obj && Object.prototype.hasOwnProperty.call(obj, k)) return obj[k] as T;
	}
	return undefined;
}

/** BTS-163: a product's price in next week's catalogue (Wednesday to Tuesday). */
export interface UpcomingSpecial {
	shopType: number;
	price: number;
	/** "each" | "kg" | "pk" | "multibuy" | ... — multibuy and kg prices are not comparable with a shelf price. */
	priceUnit: string | null;
	wasPrice: number | null;
	saveAmount: number | null;
	promoText: string | null;
	unitPriceText: string | null;
	/** yyyy-MM-dd, Sydney date the price starts. */
	validFrom: string;
	validTo: string;
}

function normalize(raw: any): UpcomingSpecial | null {
	if (!raw) return null;
	const price = Number(read<number>(raw, "price", "Price"));
	const validFrom = read<string>(raw, "validFrom", "ValidFrom");
	if (!Number.isFinite(price) || !validFrom) return null;
	const num = (v: any) =>
		v == null || !Number.isFinite(Number(v)) ? null : Number(v);
	return {
		shopType: Number(read<number>(raw, "shopType", "ShopType")),
		price,
		priceUnit: read<string | null>(raw, "priceUnit", "PriceUnit") ?? null,
		wasPrice: num(read(raw, "wasPrice", "WasPrice")),
		saveAmount: num(read(raw, "saveAmount", "SaveAmount")),
		promoText: read<string | null>(raw, "promoText", "PromoText") ?? null,
		unitPriceText:
			read<string | null>(raw, "unitPriceText", "UnitPriceText") ?? null,
		validFrom,
		validTo: read<string>(raw, "validTo", "ValidTo") ?? validFrom,
	};
}

/** Next-week prices for the given products; ids without one map to null. */
export async function fetchUpcomingByProducts(
	productIds: string[],
): Promise<Record<string, UpcomingSpecial | null>> {
	const ids = Array.from(new Set(productIds.filter(Boolean)));
	const out: Record<string, UpcomingSpecial | null> = {};
	if (ids.length === 0) return out;

	const url = `${API_BASE}/api/upcoming-specials/by-products`;
	const batches: string[][] = [];
	for (let i = 0; i < ids.length; i += MAX_BATCH)
		batches.push(ids.slice(i, i + MAX_BATCH));

	const responses = await Promise.all(
		batches.map((batch) => axios.post(url, { productIds: batch })),
	);
	responses.forEach((res, i) => {
		const results =
			read<Record<string, any>>(res.data ?? {}, "results", "Results") ?? {};
		for (const id of batches[i]) out[id] = normalize(results[id]);
	});
	return out;
}

import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || ""; // e.g. https://lhpjjzns6f.execute-api.ap-southeast-2.amazonaws.com

export interface ProductRow {
	productId: string;
	name: string;
	price?: number;
	promoText?: string | null;
	shopType?: number | string | null;
	sizeValue?: number | null;
	sizeUnit?: string | null;
	imageUrl?: string | null;
	// Combined size string for display, e.g. "500 g" or "2 L"
	size?: string | null;
}

export interface FetchProductsParams {
	page: number; // 1-based
	pageSize: number;
	name?: string;
	shopType?: number;
}

export interface FetchProductsResult {
	total: number;
	items: ProductRow[];
}

export async function fetchProducts(
	params: FetchProductsParams,
): Promise<FetchProductsResult> {
	const url = `${API_BASE}/api/Products`;
	const res = await axios.get(url, { params });
	const data = res.data ?? {};

	// Support both camelCase and PascalCase payloads
	const rawItems: ProductRow[] = data.products ?? data.Products ?? [];
	// Build combined size field on the client for compatibility with different backends
	const items: ProductRow[] = rawItems.map((it) => {
		const imageUrl =
			(it as any).imageUrl ?? (it as any).ImageUrl ?? it.imageUrl ?? null;
		const promoText =
			(it as any).promoText ?? (it as any).PromoText ?? it.promoText ?? null;
		const sizeExisting = (it as any).size ?? (it as any).Size;
		if (sizeExisting) return { ...it, imageUrl, size: sizeExisting, promoText };

		const v = it.sizeValue;
		const u = it.sizeUnit;
		let size: string | null = null;
		if (v != null && u != null && u !== "") {
			// keep decimal as-is
			size = `${v} ${u}`;
		} else if (v != null) {
			size = String(v);
		} else if (u != null) {
			size = u;
		}
		return { ...it, imageUrl, size, promoText };
	});
	const total: number = data.count ?? data.Count ?? 0;

	return { total, items };
}

// ── AI search types ──────────────────────────────────────────────────────────

export interface ProductSearchItem {
	productId: string;
	name: string;
	shopType?: number | null;
	brand?: string | null;
	sizeValue?: number | null;
	sizeUnit?: string | null;
	imageUrl?: string | null;
	price?: number | null;
	promoText?: string | null;
}

export interface DescriptionSearchResponse {
	query: string;
	inferredProducts: string[];
	remainingSearches: number;
	products: ProductSearchItem[];
}

// ── AI search function ────────────────────────────────────────────────────────

export async function searchByDescription(
	query: string,
	topN: number = 10,
	shopType?: number,
): Promise<DescriptionSearchResponse> {
	const url = `${API_BASE}/api/Products/search-by-description`;
	const response = await fetch(url, {
		method: "POST",
		headers: { "Content-Type": "application/json" },
		body: JSON.stringify({ query, topN, shopType: shopType ?? null }),
	});

	if (response.status === 429) {
		const errorBody = await response.json().catch(() => ({}));
		const err = new Error(
			(errorBody as any).message ?? "Daily AI search limit reached. Try again tomorrow.",
		) as Error & { status: number };
		err.status = 429;
		throw err;
	}

	if (!response.ok) {
		throw new Error(`Search failed with status ${response.status}`);
	}

	return response.json() as Promise<DescriptionSearchResponse>;
}

import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || ""; // e.g. https://lhpjjzns6f.execute-api.ap-southeast-2.amazonaws.com

export interface ProductRow {
	productId: string;
	name: string;
	price?: number;
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
		const sizeExisting = (it as any).size ?? (it as any).Size;
		if (sizeExisting) return { ...it, imageUrl, size: sizeExisting };

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
		return { ...it, imageUrl, size };
	});
	const total: number = data.count ?? data.Count ?? 0;

	return { total, items };
}

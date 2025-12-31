import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";

export interface FavoriteItem {
	id: number;
	productId: string;
	productName: string;
	isActive: boolean;
	createdAt: string;
}

export async function fetchFavorites(): Promise<FavoriteItem[]> {
	const url = `${API_BASE}/api/Favorites`;
	const res = await axios.get(url);
	return res.data ?? [];
}

export async function addFavorite(productId: string): Promise<void> {
	const url = `${API_BASE}/api/Favorites/${productId}`;
	await axios.post(url);
}

export async function removeFavorite(productId: string): Promise<void> {
	const url = `${API_BASE}/api/Favorites/${productId}`;
	await axios.delete(url);
}

export async function updateFavorite(
	productId: string,
	isActive: boolean,
): Promise<void> {
	const url = `${API_BASE}/api/Favorites/${productId}`;
	await axios.put(url, { isActive });
}

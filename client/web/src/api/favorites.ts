import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";

export interface FavoriteItem {
	id: number;
	productId: number;
	createdAt: string;
}

export async function fetchFavorites(): Promise<FavoriteItem[]> {
	const url = `${API_BASE}/api/Favorites`;
	const res = await axios.get(url);
	return res.data ?? [];
}

export async function addFavorite(productId: number): Promise<void> {
	const url = `${API_BASE}/api/Favorites/${productId}`;
	await axios.post(url);
}

export async function removeFavorite(productId: number): Promise<void> {
	const url = `${API_BASE}/api/Favorites/${productId}`;
	await axios.delete(url);
}


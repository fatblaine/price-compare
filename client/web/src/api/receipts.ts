import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";

export interface ReceiptSummary {
	id: number;
	storeName: string;
	purchaseDate: string;
	totalAmount: number;
	uploadUrl?: string | null;
}

export interface ReceiptItem {
	id: number;
	receiptId: number;
	productName: string;
}

export interface ReceiptDetail {
	id: number;
	storeName: string;
	purchaseDate: string;
	totalAmount: number;
	uploadUrl?: string | null;
	items: ReceiptItem[];
}

export interface UploadAndParseResponse {
	receiptId: number;
	productNames: string[];
}

export async function fetchMyReceipts(): Promise<ReceiptSummary[]> {
	const url = `${API_BASE}/api/Receipts`;
	const res = await axios.get(url);
	return res.data ?? [];
}

export async function fetchReceiptDetail(
	id: number,
): Promise<ReceiptDetail | null> {
	const url = `${API_BASE}/api/Receipts/${id}`;
	const res = await axios.get(url);
	return res.data ?? null;
}

export async function uploadAndParseReceipt(
	file: File,
): Promise<UploadAndParseResponse> {
	const url = `${API_BASE}/api/Receipts/upload-and-parse`;
	const formData = new FormData();
	formData.append("file", file);

	const res = await axios.post(url, formData, {
		headers: {
			"Content-Type": "multipart/form-data",
		},
	});

	return res.data;
}

import axios from "axios";

const API_BASE = process.env.REACT_APP_API_BASE?.trim() || "";
const TOKEN_KEY = "pc_token";
const EMAIL_KEY = "pc_email";

export function getStoredToken(): string | null {
	const token = typeof window !== "undefined" ? localStorage.getItem(TOKEN_KEY) : null;
	if (token) {
		axios.defaults.headers.common.Authorization = `Bearer ${token}`;
	}
	return token;
}

export function getStoredEmail(): string | null {
	if (typeof window === "undefined") return null;
	return localStorage.getItem(EMAIL_KEY);
}

export function clearToken(): void {
	if (typeof window === "undefined") return;
	localStorage.removeItem(TOKEN_KEY);
	localStorage.removeItem(EMAIL_KEY);
	// eslint-disable-next-line @typescript-eslint/no-dynamic-delete
	delete axios.defaults.headers.common.Authorization;
}

export async function login(email: string, password: string): Promise<string> {
	const url = `${API_BASE}/api/Auth/login`;
	const res = await axios.post(url, { email, password });
	const token: string | undefined = res.data?.token;
	if (!token) {
		throw new Error("Login failed: invalid response from server");
	}

	if (typeof window !== "undefined") {
		localStorage.setItem(TOKEN_KEY, token);
		localStorage.setItem(EMAIL_KEY, email);
	}
	axios.defaults.headers.common.Authorization = `Bearer ${token}`;
	return token;
}

export async function register(
	email: string,
	password: string,
): Promise<string> {
	const url = `${API_BASE}/api/Auth/register`;
	const res = await axios.post(url, { email, password });
	const userId: string | undefined = res.data?.userId;
	if (!userId) {
		throw new Error("Register failed: invalid response from server");
	}
	return userId;
}
